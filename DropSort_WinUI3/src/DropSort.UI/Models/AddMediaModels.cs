using System.ComponentModel;
using DropSort.Application.Dto;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.Domain.Metadata.Contracts;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;

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
    private ImportMetadataPreview? _metadataPreview;

    public DetectedMediaDisplayRecord(DiscoveredMedia item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _isSelected = CanSelect;
    }

    public DiscoveredMedia Item { get; }

    public bool CanSelect => Item.Classification is
        DiscoveryClassification.MovieCandidate or DiscoveryClassification.TvEpisodeCandidate;

    /// <summary>
    /// Whether this row is eligible for a new registration. A path already present in the catalog
    /// remains visible for honest feedback, but cannot be selected or sent through metadata lookup.
    /// </summary>
    public bool CanAdd => CanSelect && _status != ImportItemStatus.AlreadyInLibrary;

    /// <summary>
    /// True when the row can be added immediately without any review: a selectable, not-already-in-library
    /// item whose metadata state is either the clean initial state or a confident match. Needs-review,
    /// no-match, metadata-failure and already-in-library rows are deliberately excluded, so
    /// "Select ready to add" never picks up an item that still needs the user's attention.
    /// </summary>
    public bool IsReadyToAdd => CanAdd
        && _status is ImportItemStatus.ReadyToAdd or ImportItemStatus.Matched;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var selected = CanAdd && value;
            if (_isSelected == selected)
            {
                return;
            }

            _isSelected = selected;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    /// <summary>Whether the row can still be changed while an import is running.</summary>
    public bool IsSelectionEnabled => CanAdd && !_isImporting;

    public ImportItemStatus Status => _status;

    /// <summary>
    /// Metadata found during the review phase. This is staged UI state only; it is not persisted until
    /// the user confirms Add Selected.
    /// </summary>
    public ImportMetadataPreview? MetadataPreview => _metadataPreview;

    public string PreviewTitle => _metadataPreview?.MovieCandidate?.Title
        ?? _metadataPreview?.TvCandidate?.Title
        ?? string.Empty;

    public string PreviewYear
    {
        get
        {
            var year = _metadataPreview?.MovieCandidate?.Year
                ?? _metadataPreview?.TvCandidate?.FirstAirYear;
            return year is int value ? LocalizationService.Number(value) : string.Empty;
        }
    }

    public string PreviewDetails
    {
        get
        {
            var candidate = (object?)_metadataPreview?.MovieCandidate
                ?? _metadataPreview?.TvCandidate;
            var overview = candidate switch
            {
                MovieCandidate movie => movie.Overview,
                TvCandidate show => show.Overview,
                _ => null,
            };
            var rating = candidate switch
            {
                MovieCandidate movie => movie.Rating,
                TvCandidate show => show.Rating,
                _ => null,
            };
            var poster = candidate switch
            {
                MovieCandidate movie => movie.PosterReference,
                TvCandidate show => show.PosterReference,
                _ => null,
            };
            var details = new List<string>(3);
            if (rating is double value)
            {
                details.Add(value.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture));
            }
            if (!string.IsNullOrWhiteSpace(overview))
            {
                details.Add(overview!);
            }
            if (!string.IsNullOrWhiteSpace(poster))
            {
                details.Add(LocalizationService.Text("PosterAvailable"));
            }
            return string.Join(" · ", details);
        }
    }

    public Visibility ManualMatchVisibility => _status is ImportItemStatus.NeedsMatch or ImportItemStatus.NoMatchFound
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RetryVisibility => _status is ImportItemStatus.MetadataError
        or ImportItemStatus.MetadataUnavailable
        or ImportItemStatus.MetadataCancelled
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PreviewVisibility => !string.IsNullOrWhiteSpace(PreviewTitle)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ManualMatchLabel => _status == ImportItemStatus.NeedsMatch
        ? LocalizationService.Text("ReviewMatch")
        : LocalizationService.Text("MatchManually");

    public string RetryLabel => LocalizationService.Text("Retry");

    /// <summary>
    /// Whether the user may flip this row between Movie and TV. Only the two registerable candidate kinds
    /// can be overridden; unresolved / error / already-in-library rows cannot.
    /// </summary>
    public bool CanOverrideType => Item.Classification is
        DiscoveryClassification.MovieCandidate or DiscoveryClassification.TvEpisodeCandidate;

    public Visibility OverrideTypeVisibility =>
        CanOverrideType && _status != ImportItemStatus.AlreadyInLibrary ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Offers the opposite classification: a movie can be treated as TV, and a TV episode as a movie.</summary>
    public string OverrideTypeLabel => Item.Classification == DiscoveryClassification.MovieCandidate
        ? LocalizationService.Text("TreatAsTv")
        : LocalizationService.Text("TreatAsMovie");

    public void SetImporting(bool importing)
    {
        if (_isImporting == importing)
        {
            return;
        }

        _isImporting = importing;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionEnabled)));
    }

    public string? RegistrationFailure { get; private set; }

    public void SetRegistrationResult(ImportItemStatus status, string? failure)
    {
        RegistrationFailure = failure;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RegistrationFailure)));
        SetImportStatus(status);
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanAdd)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsReadyToAdd)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionEnabled)));
        RaiseMetadataProperties();
    }

    /// <summary>Marks an exact path already present in the catalog without invoking metadata.</summary>
    public void SetAlreadyInLibrary()
    {
        _metadataPreview = null;
        IsSelected = false;
        SetImportStatus(ImportItemStatus.AlreadyInLibrary);
        RaiseMetadataProperties();
    }

    public void SetMetadataReviewing()
    {
        _metadataPreview = null;
        SetImportStatus(ImportItemStatus.Matching);
        RaiseMetadataProperties();
    }

    public void SetMetadataCancelled()
    {
        if (_status != ImportItemStatus.Matching)
        {
            return;
        }

        SetMetadataPreview(new ImportMetadataPreview(
            ImportItemStatus.MetadataCancelled,
            Failure: "Metadata matching was cancelled."));
    }

    public void SetMetadataPreview(ImportMetadataPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.Status == ImportItemStatus.AlreadyInLibrary)
        {
            SetAlreadyInLibrary();
            return;
        }

        _metadataPreview = preview;
        SetImportStatus(preview.Status);
        RaiseMetadataProperties();
    }

    private void RaiseMetadataProperties()
    {
        foreach (var name in new[]
        {
            nameof(MetadataPreview), nameof(PreviewTitle), nameof(PreviewYear), nameof(PreviewDetails),
            nameof(ManualMatchVisibility), nameof(RetryVisibility), nameof(PreviewVisibility),
            nameof(ManualMatchLabel), nameof(RetryLabel), nameof(AutomationName),
            nameof(OverrideTypeVisibility), nameof(OverrideTypeLabel),
            nameof(LocalEligibilityLabel), nameof(ComparisonLabel), nameof(ComparisonVisibility),
            nameof(IdentityLabel), nameof(DetailsLabel)
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public void RefreshLocalization()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutomationName)));
        RaiseMetadataProperties();
    }

    public string TypeLabel => Item.Classification switch
    {
        DiscoveryClassification.MovieCandidate => LocalizationService.Text("Movie"),
        DiscoveryClassification.TvEpisodeCandidate => LocalizationService.Text("TvEpisode"),
        DiscoveryClassification.TvEpisodeUnsupported => LocalizationService.Text("TvEpisodeUnsupported"),
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

    public string StatusLabel => Item.Issue?.Code == DiscoveryErrorCode.StorageUnavailable
        ? LocalizationService.Text("StorageUnavailable")
        : CanSelect
        ? _status switch
        {
            ImportItemStatus.ReadyToAdd => LocalizationService.Text("ReadyToAdd"),
            ImportItemStatus.Matching => LocalizationService.Text("Matching"),
            ImportItemStatus.Matched => LocalizationService.Text("Matched"),
            ImportItemStatus.NeedsMatch => LocalizationService.Text("NeedsMatch"),
            ImportItemStatus.NoMatchFound => LocalizationService.Text("NoMatchFound"),
            ImportItemStatus.MetadataUnavailable => LocalizationService.Text("MetadataUnavailable"),
            ImportItemStatus.MetadataError => LocalizationService.Text("MetadataError"),
            ImportItemStatus.MetadataCancelled => LocalizationService.Text("ImportMetadataCancelled"),
            ImportItemStatus.RegistrationFailed => LocalizationService.Text("RegistrationFailed"),
            ImportItemStatus.AlreadyInLibrary => LocalizationService.Text("AlreadyInLibrary"),
            _ => LocalizationService.Text("ReadyToAdd"),
        }
        : LocalizationService.Text("NeedsReview");

    public string AutomationName => string.Join(" · ",
        new[] { TypeLabel, Title, Details, FileName, PreviewTitle, PreviewYear }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public AddMediaReviewFacts ReviewFacts => new(CanSelect, Status, IsSelected, Item.FileSize is not null);
    public string IdentityLabel => string.Join(" · ", new[]
    {
        TypeLabel, Details, Item.ParsedMedia?.Year is null ? PreviewYear : string.Empty,
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string LocalEligibilityLabel => LocalizationService.Text(CanAdd ? "LocalFileCanAdd" : "LocalFileNeedsReview");
    public string DetailsLabel => LocalizationService.Text("Details");
    public string ComparisonLabel => LocalizationService.Format("ImportComparisonFormat", PreviewTitle, PreviewYear);
    public Visibility ComparisonVisibility => !string.IsNullOrWhiteSpace(PreviewTitle)
        && (!string.Equals(Title, PreviewTitle, StringComparison.CurrentCultureIgnoreCase)
            || Status == ImportItemStatus.NeedsMatch)
        ? Visibility.Visible : Visibility.Collapsed;

    public bool NeedsReview => !CanSelect;

    public event PropertyChangedEventHandler? PropertyChanged;
}
