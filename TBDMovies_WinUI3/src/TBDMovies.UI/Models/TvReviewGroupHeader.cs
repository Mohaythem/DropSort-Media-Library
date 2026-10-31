using System.ComponentModel;
using TBDMovies.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TBDMovies.UI.Models;

public sealed class TvReviewGroupHeader(string key, string title, int? season, IReadOnlyList<DetectedMediaDisplayRecord> members) : INotifyPropertyChanged
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public int? Season { get; } = season;
    public TvReviewGroupState<DetectedMediaDisplayRecord> State { get; } = new(members);
    public bool Busy { get; set; }
    public bool IsEnabled => !Busy;
    public bool? Selected => State.Selection(r => r.CanAdd, r => r.IsSelected);
    public bool CanSelect => !Busy && State.Members.Any(r => r.CanAdd);
    public string Label => Season is int n ? n < 0 ? LocalizationService.Text("NeedsReview") : LocalizationService.Format("TvBatchSeason", n) : Title;
    public string Count => LocalizationService.Format("TvBatchFiles", State.Members.Count);
    public string ExpandLabel => LocalizationService.Text(State.IsExpanded ? "TvBatchCollapse" : "TvBatchExpand");
    public string ChevronGlyph => State.IsExpanded ? "\uE70D"
        : LocalizationService.FlowDirection == FlowDirection.RightToLeft ? "\uE76B" : "\uE76C";
    public string CorrectLabel => LocalizationService.Text("TvBatchCorrect");
    public string AutomationName => Label + " · " + Count;
    public void Refresh()
    {
        foreach (var name in new[] { nameof(Label), nameof(Count), nameof(Selected), nameof(CanSelect), nameof(IsEnabled), nameof(ExpandLabel), nameof(ChevronGlyph), nameof(CorrectLabel), nameof(AutomationName) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class AddMediaTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FileTemplate { get; set; }
    public DataTemplate? GroupTemplate { get; set; }
    protected override DataTemplate SelectTemplateCore(object item) => (item is TvReviewGroupHeader ? GroupTemplate : FileTemplate)!;
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
