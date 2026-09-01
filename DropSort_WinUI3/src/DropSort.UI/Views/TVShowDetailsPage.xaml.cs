using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Views;

/// <summary>
/// TV show details. Season / episode rows are built as display records so the templates never call
/// the localization service; episode actions stay inert until the V2 backend exists.
/// </summary>
public sealed partial class TVShowDetailsPage : Page, ILocalizableView
{
    private TVShowRecord _show = DemoData.Shows[0];
    private string _returnDestination = "library";

    public TVShowDetailsPage()
    {
        InitializeComponent();
        SetShow(_show);
    }

    public event EventHandler? BackRequested;

    public void SetReturnDestination(string destination)
    {
        _returnDestination = destination;
        UpdateBackLabel();
    }

    public void SetShow(TVShowRecord show)
    {
        _show = show;
        ShowTitleText.Text = show.Title;
        ShowMetaText.Text = show.MetaLine;
        OverviewText.Text = show.Overview;
        ProgressValueText.Text = show.ProgressLine;
        PlayNextButton.IsEnabled = show.NextPlayableEpisode is not null;
        ApplyLocalization();
    }

    public void ApplyLocalization()
    {
        Root.FlowDirection = LocalizationService.FlowDirection;

        UpdateBackLabel();
        KindText.Text = LocalizationService.Text("TvShow");
        ProgressLabelText.Text = LocalizationService.Text("EpisodesWatched");
        PlayNextText.Text = LocalizationService.Text("PlayNextEpisode");
        AddToListText.Text = LocalizationService.Text("AddToList");
        MoreButton.SetValue(AutomationProperties.NameProperty, LocalizationService.Text("MoreOptions"));
        ToolTipService.SetToolTip(MoreButton, LocalizationService.Text("MoreOptions"));
        MarkShowWatchedItem.Text = LocalizationService.Text("MarkWatched");
        OpenShowFolderItem.Text = LocalizationService.Text("OpenFolder");
        SeasonsTitleText.Text = LocalizationService.Text("Seasons");
        SeasonsHelpText.Text = LocalizationService.Text("SeasonsHelp");

        SeasonsRepeater.ItemsSource = BuildSeasons(_show);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void UpdateBackLabel()
    {
        BackText.Text = LocalizationService.Text(_returnDestination switch
        {
            "home" => "BackToHome",
            "lists" => "BackToMyLists",
            _ => "BackToLibrary",
        });
    }

    /// <summary>The first season starts expanded, exactly like the design source.</summary>
    private static IReadOnlyList<SeasonDisplayRecord> BuildSeasons(TVShowRecord show)
    {
        var labels = new EpisodeActionLabels(
            LocalizationService.Text("Watched"),
            LocalizationService.Text("Missing"),
            LocalizationService.Text("PlayEpisode"),
            LocalizationService.Text("OpenFolder"),
            LocalizationService.Text("MoreOptions"));

        return [.. show.Seasons.Select((season, index) => new SeasonDisplayRecord(
            show.Id,
            season.Number,
            ShowFormatting.SeasonArtLabel(season.Number),
            LocalizationService.Format("SeasonFormat", season.Number),
            LocalizationService.Format(
                "SeasonMetaFormat",
                season.Episodes.Count,
                season.WatchedCount,
                season.LocalCount),
            season.Episodes.Count == 0 ? 0 : 100.0 * season.WatchedCount / season.Episodes.Count,
            index == 0,
            [.. season.Episodes.Select(episode => new EpisodeDisplayRecord(
                show.Id,
                season.Number,
                episode.Number,
                ShowFormatting.EpisodeThumbnailLabel(episode.Number),
                episode.Title,
                ShowFormatting.EpisodeCode(season.Number, episode.Number) + " · " + episode.Runtime,
                episode.Watched,
                episode.HasLocalFile,
                labels))]))];
    }
}
