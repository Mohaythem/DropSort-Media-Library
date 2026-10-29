using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DropSort.Application.Services;

public interface IClipboardService
{
    bool TrySetText(string text);
}

public interface ITimerToken : IDisposable
{
    void Cancel();
}

public interface IInteractionTimerScheduler
{
    ITimerToken Schedule(TimeSpan delay, Action callback);
}

public interface IDoubleClickTimeProvider
{
    TimeSpan GetDoubleClickTime();
}

public sealed class WindowsDoubleClickTimeProvider : IDoubleClickTimeProvider
{
    [DllImport("user32.dll", EntryPoint = "GetDoubleClickTime", SetLastError = false)]
    private static extern uint NativeGetDoubleClickTime();

    public TimeSpan GetDoubleClickTime()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                uint ms = NativeGetDoubleClickTime();
                if (ms > 0)
                {
                    return TimeSpan.FromMilliseconds(ms);
                }
            }
        }
        catch
        {
            // Fallback for non-Windows or restricted environments
        }

        return TimeSpan.FromMilliseconds(500);
    }
}

public sealed class TaskDelayScheduler : IInteractionTimerScheduler
{
    public ITimerToken Schedule(TimeSpan delay, Action callback)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled && !cts.IsCancellationRequested)
            {
                callback();
            }
        }, TaskScheduler.Default);
        return new Token(cts);
    }

    private sealed class Token(CancellationTokenSource cts) : ITimerToken
    {
        public void Cancel()
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
        public void Dispose()
        {
            Cancel();
            cts.Dispose();
        }
    }
}

/// <summary>
/// Coordinates single-click clipboard copying, double-click inline editing,
/// auto-dismiss feedback, validation, and safe blur/cancellation policies
/// for custom display titles. Decoupled from UI frameworks for testability.
/// </summary>
public sealed class TitleInteractionController : IDisposable
{
    public const int MaxTitleLength = 500;
    public static readonly TimeSpan DefaultSingleClickDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan FeedbackDuration = TimeSpan.FromMilliseconds(1500);

    private readonly IClipboardService? _clipboard;
    private readonly IInteractionTimerScheduler _scheduler;
    private readonly Action<Action>? _uiDispatcher;
    private readonly IDoubleClickTimeProvider _doubleClickTimeProvider;

    private ITimerToken? _pendingClickToken;
    private ITimerToken? _pendingFeedbackToken;
    private long _interactionGeneration;

    public TimeSpan SingleClickDelay => _doubleClickTimeProvider.GetDoubleClickTime();

    public string CanonicalTitle { get; private set; } = string.Empty;
    public string? CustomTitle { get; private set; }
    public string DisplayTitle => !string.IsNullOrWhiteSpace(CustomTitle) ? CustomTitle : CanonicalTitle;

    public bool IsEditing { get; private set; }
    public bool IsCopyBadgeVisible { get; private set; }

    public Action<string>? OnDisplayTitleChanged { get; set; }
    public Action<bool>? OnEditModeChanged { get; set; }
    public Action<bool>? OnCopyBadgeVisibilityChanged { get; set; }
    public Action<string>? OnError { get; set; }
    public Func<string?, bool>? SaveHandler { get; set; }
    public Func<string, string>? ErrorMessageProvider { get; set; }

    public TitleInteractionController(
        IClipboardService? clipboard = null,
        IInteractionTimerScheduler? scheduler = null,
        Action<Action>? uiDispatcher = null,
        IDoubleClickTimeProvider? doubleClickTimeProvider = null)
    {
        _clipboard = clipboard;
        _scheduler = scheduler ?? new TaskDelayScheduler();
        _uiDispatcher = uiDispatcher;
        _doubleClickTimeProvider = doubleClickTimeProvider ?? new WindowsDoubleClickTimeProvider();
    }

    public void Initialize(string canonicalTitle, string? customTitle)
    {
        CancelPendingTimers();
        CanonicalTitle = canonicalTitle?.Trim() ?? string.Empty;
        CustomTitle = string.IsNullOrWhiteSpace(customTitle) ? null : customTitle.Trim();
        IsEditing = false;
        IsCopyBadgeVisible = false;
    }

    public void HandleSingleClick()
    {
        if (IsEditing) return;

        CancelPendingTimers();
        var currentGen = Volatile.Read(ref _interactionGeneration);
        _pendingClickToken = _scheduler.Schedule(SingleClickDelay, () =>
        {
            RunOnUi(() =>
            {
                if (Volatile.Read(ref _interactionGeneration) != currentGen) return;
                if (IsEditing) return;
                ExecuteCopy(currentGen);
            });
        });
    }

    public void HandleDoubleClick()
    {
        if (IsEditing) return;

        CancelPendingTimers();
        DismissFeedback();

        IsEditing = true;
        RunOnUi(() => OnEditModeChanged?.Invoke(true));
    }

    public void HandleKeyF2()
    {
        if (IsEditing) return;
        HandleDoubleClick();
    }

    public void HandleKeyCtrlC()
    {
        if (IsEditing) return;
        var currentGen = Volatile.Read(ref _interactionGeneration);
        RunOnUi(() => ExecuteCopy(currentGen));
    }

    public bool HandleKeyEnter(string? newText)
    {
        if (!IsEditing) return false;

        string? overrideValue;
        if (string.IsNullOrWhiteSpace(newText))
        {
            overrideValue = null;
        }
        else
        {
            var trimmed = newText.Trim();
            if (trimmed.Length > MaxTitleLength)
            {
                var tooLongMsg = ErrorMessageProvider?.Invoke("CustomTitleTooLong")
                    ?? $"Title cannot exceed {MaxTitleLength} characters.";
                RunOnUi(() => OnError?.Invoke(tooLongMsg));
                return false;
            }

            overrideValue = string.Equals(trimmed, CanonicalTitle, StringComparison.Ordinal)
                ? null
                : trimmed;
        }

        try
        {
            var success = SaveHandler?.Invoke(overrideValue) ?? true;
            if (success)
            {
                CustomTitle = overrideValue;
                IsEditing = false;
                RunOnUi(() =>
                {
                    OnDisplayTitleChanged?.Invoke(DisplayTitle);
                    OnEditModeChanged?.Invoke(false);
                });
                return true;
            }

            var errMsg = ErrorMessageProvider?.Invoke("CustomTitleSaveFailed") ?? "Failed to save custom display title.";
            RunOnUi(() => OnError?.Invoke(errMsg));
            return false;
        }
        catch (Exception ex)
        {
            RunOnUi(() => OnError?.Invoke(ex.Message));
            return false;
        }
    }

    public void HandleKeyEscape()
    {
        if (!IsEditing) return;

        IsEditing = false;
        RunOnUi(() => OnEditModeChanged?.Invoke(false));
    }

    public void HandleBlur(string? currentEditText)
    {
        if (!IsEditing) return;

        var candidate = currentEditText?.Trim() ?? string.Empty;
        var current = CustomTitle ?? CanonicalTitle;

        if (string.Equals(candidate, current, StringComparison.Ordinal))
        {
            // Input unchanged, revert cleanly without saving
            HandleKeyEscape();
            return;
        }

        // On blur attempt save; on failure, preserve unsaved input and editing mode so user doesn't lose edits.
        HandleKeyEnter(currentEditText);
    }

    private void ExecuteCopy(long expectedGeneration)
    {
        if (Volatile.Read(ref _interactionGeneration) != expectedGeneration) return;
        if (IsEditing) return;

        var textToCopy = DisplayTitle;
        if (string.IsNullOrEmpty(textToCopy) || _clipboard == null) return;

        try
        {
            if (_clipboard.TrySetText(textToCopy))
            {
                IsCopyBadgeVisible = true;
                RunOnUi(() =>
                {
                    if (Volatile.Read(ref _interactionGeneration) != expectedGeneration) return;
                    OnCopyBadgeVisibilityChanged?.Invoke(true);
                });

                _pendingFeedbackToken?.Cancel();
                _pendingFeedbackToken?.Dispose();
                var feedbackGen = Volatile.Read(ref _interactionGeneration);
                _pendingFeedbackToken = _scheduler.Schedule(FeedbackDuration, () =>
                {
                    RunOnUi(() =>
                    {
                        if (Volatile.Read(ref _interactionGeneration) != feedbackGen) return;
                        DismissFeedback();
                    });
                });
            }
        }
        catch
        {
            // Clipboard failure must remain harmless and not crash
        }
    }

    private void DismissFeedback()
    {
        if (!IsCopyBadgeVisible) return;
        IsCopyBadgeVisible = false;
        RunOnUi(() => OnCopyBadgeVisibilityChanged?.Invoke(false));
    }

    private void CancelPendingTimers()
    {
        Interlocked.Increment(ref _interactionGeneration);
        _pendingClickToken?.Cancel();
        _pendingClickToken?.Dispose();
        _pendingClickToken = null;
        _pendingFeedbackToken?.Cancel();
        _pendingFeedbackToken?.Dispose();
        _pendingFeedbackToken = null;
    }

    private void RunOnUi(Action action)
    {
        if (_uiDispatcher is not null)
        {
            _uiDispatcher(action);
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        CancelPendingTimers();
    }
}
