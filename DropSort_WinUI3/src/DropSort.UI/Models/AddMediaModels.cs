using System.ComponentModel;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.UI.Services;

namespace DropSort.UI.Models;

/// <summary>
/// One row in the mixed Add Media review. The discovery record remains immutable and the selection
/// lives here, so cancelling or changing the selection can never mutate the catalog.
/// </summary>
public sealed class DetectedMediaDisplayRecord : INotifyPropertyChanged
{
    private bool _isSelected;

    public DetectedMediaDisplayRecord(DiscoveredMedia item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _isSelected = CanSelect;
    }

    public DiscoveredMedia Item { get; }

    public bool CanSelect => Item.Classification is
        DiscoveryClassification.MovieCandidate or DiscoveryClassification.TvEpisodeCandidate;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var selected = CanSelect && value;
            if (_isSelected == selected)
            {
                return;
            }

            _isSelected = selected;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string TypeLabel => Item.Classification switch
    {
        DiscoveryClassification.MovieCandidate => LocalizationService.Text("Movie"),
        DiscoveryClassification.TvEpisodeCandidate => LocalizationService.Text("TvEpisode"),
        DiscoveryClassification.TvEpisodeSkipped => LocalizationService.Text("TvEpisode"),
        DiscoveryClassification.UnknownMedia => LocalizationService.Text("UnknownMedia"),
        _ => LocalizationService.Text("DiscoveryError"),
    };

    public string Title => Item.ParsedMedia?.Title is { Length: > 0 } title
        ? title
        : Path.GetFileNameWithoutExtension(Item.Path);

    public string FileName => Path.GetFileName(Item.Path);

    public string Details
    {
        get
        {
            var parsed = Item.ParsedMedia;
            if (parsed?.MediaType == MediaType.TvEpisode
                && parsed.SeasonNumber is int season
                && parsed.EpisodeNumber is int episode)
            {
                return ShowFormatting.EpisodeCode(season, episode);
            }

            var details = new List<string>(2);
            if (parsed?.Year is int year)
            {
                details.Add(LocalizationService.Number(year));
            }

            if (!string.IsNullOrWhiteSpace(parsed?.Resolution))
            {
                details.Add(parsed.Resolution!);
            }

            return string.Join(" · ", details);
        }
    }

    public string StatusLabel => CanSelect
        ? LocalizationService.Text("Ready")
        : LocalizationService.Text("NeedsReview");

    public bool NeedsReview => !CanSelect;

    public event PropertyChangedEventHandler? PropertyChanged;
}
