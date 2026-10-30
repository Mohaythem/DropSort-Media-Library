using DropSort.Application.Dto;
using DropSort.Application.Services;
using DropSort.Domain.Media.Discovery;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Views;

public sealed partial class AddMediaPage
{
    private readonly Dictionary<string, TvReviewGroupHeader> _tvHeaders = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _importCancellation;
    private bool _isConfirming;
    private bool _confirmationCancelled;
    private long _presentationVersion;

    private void LocalOnlyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (Root is null || _isScanning || _isAdding || _isReviewingMetadata || _rows.Count == 0) return;
        BeginMetadataReview();
    }

    private static Dictionary<string, (TvBatchFileCandidate? Candidate, string? Provenance)> AnalyzeDetected(
        IReadOnlyList<DiscoveredMedia> items, string? root)
    {
        var result = new Dictionary<string, (TvBatchFileCandidate?, string?)>(StringComparer.OrdinalIgnoreCase);
        if (items.Count == 0) return result;
        var service = new TvBatchAnalysisService();
        // Picked files can be on different drives. Analyze each picked parent independently.
        var batches = root is not null ? new[] { (Root: root, Items: items) }
            : items.GroupBy(i => Path.GetDirectoryName(i.Path)!, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Root: TvBatchAnalysisService.TryExtractSeasonFolder(Path.GetFileName(g.Key), out _)
                    ? Path.GetDirectoryName(g.Key) ?? g.Key : g.Key, Items: (IReadOnlyList<DiscoveredMedia>)g.ToArray()));
        foreach (var batch in batches)
        {
            var analysis = service.Analyze(batch.Root, batch.Items);
            var provenance = analysis.Series.SelectMany(s => s.Files.Select(f => (f.OriginalItem.Path, s.PathProvenance)))
                .ToDictionary(x => x.Path, x => x.PathProvenance, StringComparer.OrdinalIgnoreCase);
            foreach (var file in analysis.AllFiles)
                result[file.OriginalItem.Path] = (file, provenance.GetValueOrDefault(file.OriginalItem.Path) ?? batch.Root);
        }
        return result;
    }

    private TvReviewGroupHeader Header(string key, string title, int? season, IReadOnlyList<DetectedMediaDisplayRecord> members)
    {
        if (!_tvHeaders.TryGetValue(key, out var header))
        {
            header = new TvReviewGroupHeader(key, title, season, members);
            _tvHeaders.Add(key, header);
        }
        header.State.Members = members;
        header.Busy = _isAdding || _isReviewingMetadata;
        header.Refresh();
        return header;
    }

    private void BuildGroupedQueue()
    {
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows.Where(r => _visibleMembers.Contains(r)))
        {
            if (!row.IsTv) { _visibleRows.Add(row); continue; }
            if (!emitted.Add(row.TvGroupKey)) continue;
            var allMembers = _rows.Where(r => r.IsTv && r.TvGroupKey == row.TvGroupKey).ToArray();
            var series = Header("show|" + row.TvGroupKey, row.Title, null, allMembers);
            _visibleRows.Add(series);
            if (!series.State.IsExpanded) continue;
            foreach (var seasonGroup in allMembers.Where(r => _visibleMembers.Contains(r))
                .GroupBy(r => r.TvAnalysis?.SeasonNumber ?? -1).OrderBy(g => g.Key))
            {
                var season = Header("season|" + row.TvGroupKey + "|" + seasonGroup.Key, row.Title, seasonGroup.Key,
                    allMembers.Where(r => (r.TvAnalysis?.SeasonNumber ?? -1) == seasonGroup.Key).ToArray());
                _visibleRows.Add(season);
                if (season.State.IsExpanded)
                    foreach (var file in seasonGroup.OrderBy(r => r.TvAnalysis?.EpisodeNumber).ThenBy(r => r.FileName, StringComparer.OrdinalIgnoreCase))
                        _visibleRows.Add(file);
            }
        }
    }

    private void RefreshGroupHeaders()
    {
        foreach (var header in _tvHeaders.Values)
        {
            header.Busy = _isAdding || _isReviewingMetadata;
            header.Refresh();
        }
    }

    private void GroupExpand_Click(object sender, RoutedEventArgs e)
    {
        if (_isAdding || (sender as FrameworkElement)?.Tag is not TvReviewGroupHeader header) return;
        header.State.IsExpanded = !header.State.IsExpanded;
        RebuildVisibleQueue();
        Refresh();
    }

    private void GroupSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_isAdding || _isReviewingMetadata || sender is not CheckBox box || box.Tag is not TvReviewGroupHeader header) return;
        _batchPresentation = true;
        header.State.Select(header.Selected != true, r => r.CanAdd, (r, selected) => r.IsSelected = selected);
        _batchPresentation = false;
        Refresh();
    }

    private async void CorrectTv_Click(object sender, RoutedEventArgs e)
    {
        if (_isAdding || _isReviewingMetadata) return;
        var item = (sender as FrameworkElement)?.Tag;
        var members = item is TvReviewGroupHeader header ? header.State.Members
            : item is DetectedMediaDisplayRecord row ? [row] : Array.Empty<DetectedMediaDisplayRecord>();
        await CorrectTvRowsAsync(members, item is DetectedMediaDisplayRecord);
    }

    private async Task CorrectTvRowsAsync(IReadOnlyList<DetectedMediaDisplayRecord> members, bool singleFile)
    {
        if (members.Count == 0 || _isAdding || _isReviewingMetadata) return;
        try
        {
            if (singleFile && members[0].Item.Classification == DiscoveryClassification.TvEpisodeUnsupported)
            {
                await ShowMessageAsync(LocalizationService.Text("NeedsReview"), LocalizationService.Text("TvWarningUnsupportedMultiEpisode"));
                return;
            }
            var first = members[0];
            var title = new TextBox { Header = LocalizationService.Text("TvBatchTitle"), Text = first.Title };
            var season = new NumberBox { Header = LocalizationService.Text("TvBatchSeasonNumber"), Minimum = 0, Maximum = 999,
                Value = first.TvAnalysis?.SeasonNumber ?? double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            var episode = new NumberBox { Header = LocalizationService.Text("TvBatchEpisodeNumber"), Minimum = 0, Maximum = 999,
                Value = first.TvAnalysis?.EpisodeNumber ?? double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            var target = new ComboBox { Header = LocalizationService.Text("TvBatchExisting"), HorizontalAlignment = HorizontalAlignment.Stretch,
                DisplayMemberPath = "Title" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(title, "TvCorrectionTitle");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(season, "TvCorrectionSeason");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(episode, "TvCorrectionEpisode");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(target, "TvCorrectionTarget");
            var shows = await Task.Run(() => AppServices.Tv.ListShows());
            target.Items.Add(new TvTargetChoice(null, LocalizationService.Text("TvBatchLocalIdentity")));
            foreach (var show in shows) target.Items.Add(new TvTargetChoice(show.Id,
                show.Year is int year ? show.Title + " (" + LocalizationService.Number(year) + ")" : show.Title));
            target.SelectedItem = target.Items.Cast<TvTargetChoice>().FirstOrDefault(s => s.Id == first.ExistingShowId) ?? target.Items[0];
            var content = new StackPanel { Spacing = 12, MinWidth = 260 };
            content.Children.Add(title);
            var oneSeason = members.All(r => r.TvAnalysis?.SeasonNumber == first.TvAnalysis?.SeasonNumber);
            if (singleFile || oneSeason) content.Children.Add(season);
            if (singleFile) content.Children.Add(episode);
            content.Children.Add(target);
            var validation = new TextBlock { TextWrapping = TextWrapping.Wrap };
            content.Children.Add(validation);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ThemeService.ElementTheme, FlowDirection = LocalizationService.FlowDirection,
                Title = LocalizationService.Text("TvBatchCorrect"), Content = content,
                PrimaryButtonText = LocalizationService.Text("Save"), CloseButtonText = LocalizationService.Text("Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(title.Text) || ((singleFile || oneSeason) && !ValidNumber(season.Value))
                    || singleFile && !ValidNumber(episode.Value))
                {
                    args.Cancel = true;
                    validation.Text = LocalizationService.Text("TvBatchCorrectionRequired");
                }
            };
            if (await ContentDialogCoordinator.ShowAsync(dialog) != ContentDialogResult.Primary) return;
            CancelMetadataReview();
            _batchPresentation = true;
            foreach (var row in members.Where(r => r.Status != ImportItemStatus.AlreadyInLibrary))
            {
                var candidate = row.TvAnalysis ?? new TvBatchFileCandidate(row.Item, row.Item, row.Title, null, null,
                    TvEvidenceSource.Filename, TvEvidenceSource.None, TvEvidenceSource.None, [], false, true, false, row.FileName);
                var corrected = candidate.WithCorrection(title.Text,
                    singleFile || oneSeason ? (int)season.Value : candidate.SeasonNumber,
                    singleFile ? (int)episode.Value : candidate.EpisodeNumber);
                row.CorrectTv(corrected, (target.SelectedItem as TvTargetChoice)?.Id);
            }
            _batchPresentation = false;
            RebuildVisibleQueue();
            BeginMetadataReview();
            Refresh();
        }
        catch (Exception error)
        {
            _batchPresentation = false;
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    private static bool ValidNumber(double value) => double.IsFinite(value) && value is >= 0 and <= 999 && value == Math.Truncate(value);
    private sealed record TvTargetChoice(int? Id, string Title);
}
