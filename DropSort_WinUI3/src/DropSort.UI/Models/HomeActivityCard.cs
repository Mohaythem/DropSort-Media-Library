using System.ComponentModel;
using DropSort.Application.Dto;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DropSort.UI.Models;

/// <summary>One active viewing item rendered by Home's compact Continue card.</summary>
public sealed class HomeActivityCard : INotifyPropertyChanged
{
    private readonly ArtworkLoader _artwork;
    private readonly ArtworkLoader _poster;
    private bool _isPlaying;

    public HomeActivityCard(ActiveViewingItem item)
    {
        MovieId = item.MovieId;
        ShowId = item.ShowId;
        EpisodeId = item.EpisodeId;
        Title = item.Title;
        Year = item.Year;
        SeasonNumber = item.SeasonNumber;
        EpisodeNumber = item.EpisodeNumber;
        EpisodeTitle = item.EpisodeTitle;
        Path = item.Path;
        _artwork = new ArtworkLoader(
            [item.BackdropReference, item.ArtworkReference != item.PosterReference ? item.ArtworkReference : null],
            () =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ArtworkSource)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PosterVisibility)));
            });
        _poster = new ArtworkLoader([item.PosterReference],
            () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PosterSource))));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int? MovieId { get; }
    public int? ShowId { get; }
    public int? EpisodeId { get; }
    public string Title { get; }
    public int? Year { get; }
    public int? SeasonNumber { get; }
    public int? EpisodeNumber { get; }
    public string? EpisodeTitle { get; }
    public string? Path { get; }
    public bool CanPlay => !string.IsNullOrWhiteSpace(Path);
    public bool IsPlaying => _isPlaying;
    public bool PlayEnabled => CanPlay && !IsPlaying;
    public ImageSource? ArtworkSource => _artwork.Source;
    public ImageSource? PosterSource => _poster.Source;
    public Visibility PosterVisibility => ArtworkSource is null ? Visibility.Visible : Visibility.Collapsed;

    private string ActivityIdentity => EpisodeId is int episodeId ? $"Episode_{episodeId}" : $"Movie_{MovieId ?? 0}";
    public string DetailsAutomationId => $"HomeContinueDetails_{ActivityIdentity}";
    public string PlayAutomationId => $"HomeContinuePlay_{ActivityIdentity}";

    public void SetPlaying(bool value)
    {
        if (_isPlaying == value) return;
        _isPlaying = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayEnabled)));
    }

    public string SecondaryLine
    {
        get
        {
            if (EpisodeNumber is not int episode)
            {
                return Year is int year ? LocalizationService.Number(year) : string.Empty;
            }

            var season = SeasonNumber ?? 0;
            return LocalizationService.Digits(LocalizationService.Format(
                "HomeContinueEpisodeFormat", season, episode, EpisodeTitle ?? string.Empty));
        }
    }

    public string PlayLabel => LocalizationService.Text("HomePlay");

    public string TypeLabel => EpisodeNumber is not null
        ? LocalizationService.Text("HomeEpisodeType")
        : LocalizationService.Text("HomeMovieType");

    public string AccessibleName => string.Join(", ", new[] { Title, SecondaryLine, TypeLabel }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}
