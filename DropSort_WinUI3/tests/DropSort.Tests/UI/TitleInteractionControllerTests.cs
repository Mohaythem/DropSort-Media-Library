using System;
using System.Collections.Generic;
using DropSort.Application.Services;
using Xunit;

namespace DropSort.Tests.UI;

public sealed class TitleInteractionControllerTests
{
    private sealed class TestClipboard : IClipboardService
    {
        public string? LastText { get; set; }
        public bool ThrowOnCopy { get; set; }
        public bool ReturnFailure { get; set; }

        public bool TrySetText(string text)
        {
            if (ThrowOnCopy) throw new InvalidOperationException("Clipboard locked");
            if (ReturnFailure) return false;
            LastText = text;
            return true;
        }
    }

    private sealed class TestDoubleClickTimeProvider(TimeSpan interval) : IDoubleClickTimeProvider
    {
        public TimeSpan Interval { get; set; } = interval;
        public TimeSpan GetDoubleClickTime() => Interval;
    }

    private sealed class ManualScheduler : IInteractionTimerScheduler
    {
        private readonly List<ScheduledItem> _items = [];

        public int ScheduledCount => _items.Count;
        public IReadOnlyList<ScheduledItem> Items => _items;

        public ITimerToken Schedule(TimeSpan delay, Action callback)
        {
            var item = new ScheduledItem(delay, callback);
            _items.Add(item);
            return item;
        }

        public void TriggerAll()
        {
            var pending = _items.ToArray();
            _items.Clear();
            foreach (var item in pending)
            {
                if (!item.IsCanceled)
                {
                    item.Callback();
                }
            }
        }

        public sealed class ScheduledItem(TimeSpan delay, Action callback) : ITimerToken
        {
            public TimeSpan Delay { get; } = delay;
            public Action Callback { get; } = callback;
            public bool IsCanceled { get; private set; }

            public void Cancel() => IsCanceled = true;
            public void Dispose() => Cancel();
        }
    }

    [Fact]
    public void DisplayTitle_ReturnsCanonical_WhenNoCustomTitle()
    {
        var controller = new TitleInteractionController();
        controller.Initialize("Inception", null);

        Assert.Equal("Inception", controller.DisplayTitle);
        Assert.Null(controller.CustomTitle);
        Assert.Equal("Inception", controller.CanonicalTitle);
    }

    [Fact]
    public void DisplayTitle_ReturnsCustom_WhenCustomTitleProvided()
    {
        var controller = new TitleInteractionController();
        controller.Initialize("Inception", "My Favorite Mindbender");

        Assert.Equal("My Favorite Mindbender", controller.DisplayTitle);
        Assert.Equal("My Favorite Mindbender", controller.CustomTitle);
        Assert.Equal("Inception", controller.CanonicalTitle);
    }

    [Fact]
    public void SingleClick_CopiesToClipboard_AfterSchedulerDelay()
    {
        var clipboard = new TestClipboard();
        var scheduler = new ManualScheduler();
        var badgeStates = new List<bool>();

        var controller = new TitleInteractionController(clipboard, scheduler, ui => ui());
        controller.Initialize("Breaking Bad", "BB (Custom)");
        controller.OnCopyBadgeVisibilityChanged = visible => badgeStates.Add(visible);

        controller.HandleSingleClick();

        // Not copied yet because timer has not elapsed
        Assert.Null(clipboard.LastText);
        Assert.Empty(badgeStates);

        // When scheduler elapses:
        scheduler.TriggerAll();

        Assert.Equal("BB (Custom)", clipboard.LastText);
        Assert.Contains(true, badgeStates);
        Assert.True(controller.IsCopyBadgeVisible);

        // Feedback timer triggers auto-dismiss
        scheduler.TriggerAll();
        Assert.False(controller.IsCopyBadgeVisible);
        Assert.False(badgeStates[^1]);
    }

    [Fact]
    public void SingleClick_WhenClipboardFails_DoesNotShowCopyBadge()
    {
        var clipboard = new TestClipboard { ReturnFailure = true };
        var scheduler = new ManualScheduler();
        var badgeStates = new List<bool>();

        var controller = new TitleInteractionController(clipboard, scheduler, ui => ui());
        controller.Initialize("Breaking Bad", "BB (Custom)");
        controller.OnCopyBadgeVisibilityChanged = visible => badgeStates.Add(visible);

        controller.HandleSingleClick();
        scheduler.TriggerAll();

        Assert.False(controller.IsCopyBadgeVisible);
        Assert.DoesNotContain(true, badgeStates);
    }

    [Fact]
    public void SingleClick_WhenClipboardThrows_DoesNotCrashOrShowBadge()
    {
        var clipboard = new TestClipboard { ThrowOnCopy = true };
        var scheduler = new ManualScheduler();
        var badgeStates = new List<bool>();

        var controller = new TitleInteractionController(clipboard, scheduler, ui => ui());
        controller.Initialize("Breaking Bad", "BB (Custom)");
        controller.OnCopyBadgeVisibilityChanged = visible => badgeStates.Add(visible);

        controller.HandleSingleClick();
        scheduler.TriggerAll();

        Assert.False(controller.IsCopyBadgeVisible);
        Assert.DoesNotContain(true, badgeStates);
    }

    [Fact]
    public void DoubleClick_CancelsSingleClick_SoCopyNeverExecutes()
    {
        var clipboard = new TestClipboard();
        var scheduler = new ManualScheduler();
        var editStates = new List<bool>();

        var controller = new TitleInteractionController(clipboard, scheduler, ui => ui());
        controller.Initialize("The Matrix", null);
        controller.OnEditModeChanged = editing => editStates.Add(editing);

        // First click schedules delayed copy
        controller.HandleSingleClick();

        // Second click arrives within double-click window
        controller.HandleDoubleClick();

        Assert.True(controller.IsEditing);
        Assert.Contains(true, editStates);

        // Scheduler expires any pending timers
        scheduler.TriggerAll();

        // Clipboard copy was canceled and never executed!
        Assert.Null(clipboard.LastText);
        Assert.False(controller.IsCopyBadgeVisible);
    }

    [Fact]
    public void DoubleClick_LaterThan250ms_WithinWindowsInterval_DoesNotCopy()
    {
        var clipboard = new TestClipboard();
        var scheduler = new ManualScheduler();
        var timeProvider = new TestDoubleClickTimeProvider(TimeSpan.FromMilliseconds(500));

        var controller = new TitleInteractionController(clipboard, scheduler, ui => ui(), timeProvider);
        controller.Initialize("Oppenheimer", null);

        // First click schedules delayed copy with 500ms delay (Windows double-click time)
        controller.HandleSingleClick();
        Assert.Single(scheduler.Items);
        Assert.Equal(TimeSpan.FromMilliseconds(500), scheduler.Items[0].Delay);

        // Suppose 300ms has elapsed (greater than old 250ms hard-coded threshold, but within 500ms Windows interval).
        // Second click arrives now:
        controller.HandleDoubleClick();

        Assert.True(controller.IsEditing);

        // Now timer completes:
        scheduler.TriggerAll();

        // Must NOT copy to clipboard!
        Assert.Null(clipboard.LastText);
        Assert.False(controller.IsCopyBadgeVisible);
    }

    [Fact]
    public void KeyF2_EntersEditMode_Immediately()
    {
        var controller = new TitleInteractionController();
        controller.Initialize("Dune", null);

        controller.HandleKeyF2();

        Assert.True(controller.IsEditing);
    }

    [Fact]
    public void KeyCtrlC_CopiesImmediately_WithoutWaitingForDelay()
    {
        var clipboard = new TestClipboard();
        var scheduler = new ManualScheduler();

        var controller = new TitleInteractionController(clipboard, scheduler, ui => ui());
        controller.Initialize("Interstellar", "Interstellar (IMAX Cut)");

        controller.HandleKeyCtrlC();

        Assert.Equal("Interstellar (IMAX Cut)", clipboard.LastText);
        Assert.True(controller.IsCopyBadgeVisible);
    }

    [Fact]
    public void HandleKeyEnter_SavesValidCustomTitle_AndUpdatesDisplayTitle()
    {
        string? savedToDb = null;
        var controller = new TitleInteractionController();
        controller.Initialize("The Dark Knight", null);
        controller.SaveHandler = title =>
        {
            savedToDb = title;
            return true;
        };

        controller.HandleDoubleClick();
        var saved = controller.HandleKeyEnter("TDK - Nolan Masterpiece");

        Assert.True(saved);
        Assert.False(controller.IsEditing);
        Assert.Equal("TDK - Nolan Masterpiece", savedToDb);
        Assert.Equal("TDK - Nolan Masterpiece", controller.DisplayTitle);
        Assert.Equal("The Dark Knight", controller.CanonicalTitle);
    }

    [Fact]
    public void HandleKeyEnter_EmptyOrWhitespace_ClearsOverrideToNull()
    {
        string? savedToDb = "initial";
        var controller = new TitleInteractionController();
        controller.Initialize("The Dark Knight", "TDK Override");
        controller.SaveHandler = title =>
        {
            savedToDb = title;
            return true;
        };

        controller.HandleDoubleClick();
        var saved = controller.HandleKeyEnter("   ");

        Assert.True(saved);
        Assert.False(controller.IsEditing);
        Assert.Null(savedToDb);
        Assert.Null(controller.CustomTitle);
        Assert.Equal("The Dark Knight", controller.DisplayTitle);
    }

    [Fact]
    public void HandleKeyEnter_MatchingCanonicalTitle_ClearsOverrideToNull()
    {
        string? savedToDb = "initial";
        var controller = new TitleInteractionController();
        controller.Initialize("The Dark Knight", "TDK Override");
        controller.SaveHandler = title =>
        {
            savedToDb = title;
            return true;
        };

        controller.HandleDoubleClick();
        var saved = controller.HandleKeyEnter("The Dark Knight");

        Assert.True(saved);
        Assert.Null(savedToDb);
        Assert.Null(controller.CustomTitle);
        Assert.Equal("The Dark Knight", controller.DisplayTitle);
    }

    [Fact]
    public void HandleKeyEscape_CancelsEdit_WithoutSaving()
    {
        var saved = false;
        var controller = new TitleInteractionController();
        controller.Initialize("Avatar", "Avatar Custom");
        controller.SaveHandler = _ =>
        {
            saved = true;
            return true;
        };

        controller.HandleDoubleClick();
        Assert.True(controller.IsEditing);

        controller.HandleKeyEscape();

        Assert.False(controller.IsEditing);
        Assert.False(saved);
        Assert.Equal("Avatar Custom", controller.DisplayTitle);
    }

    [Fact]
    public void HandleBlur_WhenUnchanged_CancelsEditCleanly()
    {
        var saved = false;
        var controller = new TitleInteractionController();
        controller.Initialize("Avatar", "Avatar Custom");
        controller.SaveHandler = _ =>
        {
            saved = true;
            return true;
        };

        controller.HandleDoubleClick();
        controller.HandleBlur("Avatar Custom");

        Assert.False(controller.IsEditing);
        Assert.False(saved);
    }

    [Fact]
    public void HandleBlur_WhenChanged_CommitsSaveSafely()
    {
        string? savedToDb = null;
        var controller = new TitleInteractionController();
        controller.Initialize("Avatar", null);
        controller.SaveHandler = title =>
        {
            savedToDb = title;
            return true;
        };

        controller.HandleDoubleClick();
        controller.HandleBlur("Avatar (Director's Cut)");

        Assert.False(controller.IsEditing);
        Assert.Equal("Avatar (Director's Cut)", savedToDb);
        Assert.Equal("Avatar (Director's Cut)", controller.DisplayTitle);
    }

    [Fact]
    public void HandleBlur_WhenSaveFails_RetainsEditingMode_AndDoesNotLoseUserEdits()
    {
        string? errorReported = null;
        var controller = new TitleInteractionController();
        controller.Initialize("Gladiator", null);
        controller.OnError = err => errorReported = err;
        controller.SaveHandler = _ => false; // Persistence failure
        controller.ErrorMessageProvider = key => $"Error: {key}";

        controller.HandleDoubleClick();
        Assert.True(controller.IsEditing);

        // User typed new text, blur fired:
        controller.HandleBlur("Gladiator: Extended Edition");

        // Blur must NOT call Escape to discard edits! Editing mode must remain active:
        Assert.True(controller.IsEditing);
        Assert.NotNull(errorReported);
        Assert.Equal("Error: CustomTitleSaveFailed", errorReported);
        Assert.Equal("Gladiator", controller.CanonicalTitle);
    }

    [Fact]
    public void TitleExceedingMaxLength500_FailsValidation_DoesNotTruncateSilently()
    {
        string? errorReported = null;
        string? savedToDb = null;
        var controller = new TitleInteractionController();
        controller.Initialize("Short Title", null);
        controller.OnError = err => errorReported = err;
        controller.SaveHandler = t => { savedToDb = t; return true; };
        controller.ErrorMessageProvider = key => $"Key: {key}";

        controller.HandleDoubleClick();
        var excessivelyLong = new string('A', 501);
        var result = controller.HandleKeyEnter(excessivelyLong);

        Assert.False(result);
        Assert.Null(savedToDb); // Not saved!
        Assert.True(controller.IsEditing);
        Assert.Equal("Key: CustomTitleTooLong", errorReported);
    }

    [Fact]
    public void ClipboardFailure_HandledGracefully_WithoutCrashing()
    {
        var clipboard = new TestClipboard { ReturnFailure = true };
        var scheduler = new ManualScheduler();

        var controller = new TitleInteractionController(clipboard, scheduler, ui => ui());
        controller.Initialize("Matrix", null);

        controller.HandleKeyCtrlC();

        Assert.False(controller.IsCopyBadgeVisible);
    }

    [Fact]
    public void ClipboardException_HandledGracefully_WithoutCrashing()
    {
        var clipboard = new TestClipboard { ThrowOnCopy = true };
        var scheduler = new ManualScheduler();

        var controller = new TitleInteractionController(clipboard, scheduler, ui => ui());
        controller.Initialize("Matrix", null);

        controller.HandleKeyCtrlC();

        Assert.False(controller.IsCopyBadgeVisible);
    }

    [Fact]
    public void PersistenceFailure_ReportsError_DoesNotCorruptState()
    {
        string? errorReported = null;
        var controller = new TitleInteractionController();
        controller.Initialize("Inception", null);
        controller.OnError = err => errorReported = err;
        controller.SaveHandler = _ => throw new InvalidOperationException("Disk full");

        controller.HandleDoubleClick();
        var result = controller.HandleKeyEnter("New Title");

        Assert.False(result);
        Assert.NotNull(errorReported);
        Assert.Equal("Disk full", errorReported);
        Assert.Equal("Inception", controller.DisplayTitle);
    }

    [Fact]
    public void PageDeactivation_DisposesController_AndReinitializeAllowsReuse()
    {
        var clipboard = new TestClipboard();
        var scheduler = new ManualScheduler();
        var controller = new TitleInteractionController(clipboard, scheduler, ui => ui());

        controller.Initialize("First Movie", "First Custom");
        controller.HandleSingleClick();

        // Navigating away from page invokes Deactivate -> Dispose:
        controller.Dispose();

        // Scheduler expires stale timers from previous page visit:
        scheduler.TriggerAll();
        Assert.Null(clipboard.LastText); // Stale copy was canceled!

        // Page is reused when navigating to second movie:
        controller.Initialize("Second Movie", null);
        Assert.Equal("Second Movie", controller.DisplayTitle);
        Assert.False(controller.IsEditing);

        // New interaction on reused controller works completely normally:
        controller.HandleSingleClick();
        scheduler.TriggerAll();
        Assert.Equal("Second Movie", clipboard.LastText);
    }

    [Fact]
    public void InvalidateQueuedCallbacks_WhenNavigatingOrReinitializing()
    {
        var clipboard = new TestClipboard();
        var scheduler = new ManualScheduler();
        var controller = new TitleInteractionController(clipboard, scheduler, ui => ui());

        controller.Initialize("Movie A", null);
        controller.HandleSingleClick();

        // Before timer fires, user navigates to Movie B:
        controller.Initialize("Movie B", null);

        // Timer for Movie A fires now:
        scheduler.TriggerAll();

        // Must NOT copy Movie A!
        Assert.Null(clipboard.LastText);
    }
}
