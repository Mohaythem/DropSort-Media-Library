using DropSort.UI.Services;
using Xunit;

namespace DropSort.Tests.UI;

public sealed class PostHandoffRequestTests
{
    [Fact]
    public void Search_dialog_sanitizes_late_failures_and_consumes_cancellation()
    {
        var source = File.ReadAllText(Path.Combine(FindUiRoot(), "Views", "MatchMediaDialog.cs"));

        Assert.DoesNotContain("_statusText.Text = ex.Message", source, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
        Assert.Contains("if (!_closed && !cancellationToken.IsCancellationRequested)", source, StringComparison.Ordinal);
        Assert.Contains("if (_closed || _isSearching) return;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void New_request_invalidates_previous_request_and_cancels_its_token()
    {
        using var lifetime = new PostHandoffRequestLifetime();
        var first = lifetime.Begin();
        var second = lifetime.Begin();

        Assert.False(first.IsCurrent);
        Assert.True(first.Token.IsCancellationRequested);
        Assert.True(second.IsCurrent);
    }

    [Fact]
    public void Cancel_invalidates_request_and_token()
    {
        using var lifetime = new PostHandoffRequestLifetime();
        var request = lifetime.Begin();

        lifetime.Cancel();

        Assert.False(request.IsCurrent);
        Assert.True(request.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Deferred_completion_can_update_only_while_request_is_current()
    {
        using var lifetime = new PostHandoffRequestLifetime();
        var request = lifetime.Begin();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = false;
        var worker = Task.Run(async () =>
        {
            await completion.Task;
            if (request.IsCurrent) applied = true;
        });

        lifetime.Cancel();
        completion.SetResult(true);
        await worker;

        Assert.False(applied);
    }

    [Fact]
    public async Task Deferred_completion_for_an_old_record_cannot_update_the_new_record()
    {
        using var lifetime = new PostHandoffRequestLifetime();
        var oldRequest = lifetime.Begin();
        var oldRecordId = 7;
        var currentRecordId = oldRecordId;
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var appliedRecordId = 0;
        var worker = Task.Run(async () =>
        {
            var result = await completion.Task;
            if (oldRequest.IsCurrent && oldRecordId == currentRecordId)
            {
                appliedRecordId = result;
            }
        });

        currentRecordId = 8;
        lifetime.Begin();
        completion.SetResult(oldRecordId);
        await worker;

        Assert.Equal(0, appliedRecordId);
    }

    private static string FindUiRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "DropSort.UI");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate DropSort.UI source root.");
    }
}
