using System.ComponentModel;
using DropSort.Application.Dto;
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
    private bool _isImporting;
    private ImportItemStatus _status = ImportItemStatus.ReadyToAdd;

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

    /// <summary>Whether the row can still be changed while an import is running.</summary>
    public bool IsSelectionEnabled => CanSelect && !_isImporting;

    public ImportItemStatus Status => _status;

    public void SetImporting(bool importing)
    {
        if (_isImporting == importing)
        {
            return;
        }

        _isImporting = importing;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionEnabled)));
    }

    public void SetImportStatus(ImportItemStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
    }

    public void RefreshLocalization()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutomationName)));
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
        ? _status switch
        {
            ImportItemStatus.ReadyToAdd => LocalizationService.Text("ReadyToAdd"),
            ImportItemStatus.Matching => LocalizationService.Text("Matching"),
            ImportItemStatus.Matched => LocalizationService.Text("Matched"),
            ImportItemStatus.NeedsMatch => LocalizationService.Text("NeedsMatch"),
            ImportItemStatus.MetadataUnavailable => LocalizationService.Text("MetadataUnavailable"),
            ImportItemStatus.MetadataError => LocalizationService.Text("MetadataError"),
            ImportItemStatus.MetadataCancelled => LocalizationService.Text("ImportMetadataCancelled"),
            ImportItemStatus.RegistrationFailed => LocalizationService.Text("RegistrationFailed"),
            _ => LocalizationService.Text("ReadyToAdd"),
        }
        : LocalizationService.Text("NeedsReview");

    public string AutomationName => string.Join(" · ",
        new[] { TypeLabel, Title, Details, FileName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public bool NeedsReview => !CanSelect;

    public event PropertyChangedEventHandler? PropertyChanged;
}
