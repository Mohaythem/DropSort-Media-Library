using System.Collections.ObjectModel;
using System.Globalization;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Views;

/// <summary>
/// Movie details. Preference / watchlist / watch history are view state only: there is no V2
/// backend yet, so the designed controls stay visible and native but write nothing back.
/// </summary>
public sealed partial class MovieDetailsPage : Page, ILocalizableView
{
    private readonly ObservableCollection<WatchHistoryDisplayRecord> _history = [];
    private MovieRecord _movie = DemoData.Movies[0];
    private string? _preference;
    private bool _inWatchlist;
    private string _returnDestination = "library";

    public MovieDetailsPage()
    {
        InitializeComponent();
        HistoryRepeater.ItemsSource = _history;
        WatchedDatePicker.MaxDate = DateTimeOffset.Now;
        SetMovie(_movie);
    }

    public event EventHandler? BackRequested;

    public void SetReturnDestination(string destination)
    {
        _returnDestination = destination;
        UpdateBackLabel();
    }

    public void SetMovie(MovieRecord movie)
    {
        _movie = movie;
        _preference = movie.Preference;
        _inWatchlist = movie.InWatchlist;

        MovieTitleText.Text = movie.Title;
        MovieMetaText.Text = movie.MetaLine;
        OverviewText.Text = movie.Overview;
        GenresRepeater.ItemsSource = movie.Genres;

        var hasFile = movie.HasLocalFile;
        FileNameText.Text = movie.FileName ?? string.Empty;
        FilePathText.Text = movie.FilePath ?? string.Empty;
        FileFactsText.Text = movie.FileFacts ?? string.Empty;
        FileFactsText.Visibility = string.IsNullOrWhiteSpace(movie.FileFacts) ? Visibility.Collapsed : Visibility.Visible;
        FilePresentPanel.Visibility = hasFile ? Visibility.Visible : Visibility.Collapsed;
        NoFilePanel.Visibility = hasFile ? Visibility.Collapsed : Visibility.Visible;
        PresentPill.Visibility = hasFile ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.IsEnabled = hasFile;
        OpenFolderButton.IsEnabled = hasFile;
        OrganizeButton.IsEnabled = hasFile;

        _history.Clear();
        foreach (var entry in movie.WatchHistory ?? [])
        {
            _history.Add(new WatchHistoryDisplayRecord(entry, LocalizationService.Text("RemoveWatchEvent")));
        }

        WatchedDatePicker.Date = null;
        MarkOnDateButton.IsEnabled = false;
        ApplyLocalization();
    }

    public void ApplyLocalization()
    {
        Root.FlowDirection = LocalizationService.FlowDirection;

        UpdateBackLabel();
        KindText.Text = LocalizationService.Text("Movie");
        OriginalTitleText.Text = string.IsNullOrWhiteSpace(_movie.OriginalTitle)
            ? string.Empty
            : LocalizationService.Format("OriginalTitleFormat", _movie.OriginalTitle);
        OriginalTitleText.Visibility = string.IsNullOrWhiteSpace(_movie.OriginalTitle)
            ? Visibility.Collapsed
            : Visibility.Visible;

        PlayText.Text = LocalizationService.Text("PlayMovie");
        OpenFolderText.Text = LocalizationService.Text("OpenFolder");
        OrganizeText.Text = LocalizationService.Text("OrganizeFile");
        MoreButton.SetValue(AutomationProperties.NameProperty, LocalizationService.Text("FileDetails"));
        ToolTipService.SetToolTip(MoreButton, LocalizationService.Text("FileDetails"));
        FileDetailsTitleText.Text = LocalizationService.Text("MediaFile");
        PresentText.Text = LocalizationService.Text("Present");
        NoFileText.Text = LocalizationService.Text("NoLocalFile");
        NoFileHelpText.Text = LocalizationService.Text("NoLocalFileHelp");
        OverviewTitleText.Text = LocalizationService.Text("Overview");

        ActivityTitleText.Text = LocalizationService.Text("YourActivity");
        ActivityHelpText.Text = LocalizationService.Text("ActivityHelp");
        PreferenceTitleText.Text = LocalizationService.Text("Preference");
        LikeText.Text = LocalizationService.Text("Like");
        ExcludeText.Text = LocalizationService.Text("Exclude");
        ClearPreferenceButton.Content = LocalizationService.Text("ClearPreference");
        WatchlistTitleText.Text = LocalizationService.Text("Watchlist");
        WatchingTitleText.Text = LocalizationService.Text("Watching");
        MarkWatchedText.Text = LocalizationService.Text("MarkWatched");
        WatchedDateLabel.Text = LocalizationService.Text("WatchedDate");
        WatchedDatePicker.PlaceholderText = LocalizationService.Text("PickDate");
        MarkOnDateButton.Content = LocalizationService.Text("MarkOnDate");
        HistoryTitleText.Text = LocalizationService.Text("WatchHistory");
        NoHistoryText.Text = LocalizationService.Text("NoWatchesHelp");

        UpdatePreferenceVisuals();
        UpdateWatchlistVisuals();
        RelabelHistory();
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

    private void LikeButton_Click(object sender, RoutedEventArgs e)
    {
        _preference = _preference == "liked" ? null : "liked";
        UpdatePreferenceVisuals();
    }

    private void ExcludeButton_Click(object sender, RoutedEventArgs e)
    {
        _preference = _preference == "blacklisted" ? null : "blacklisted";
        UpdatePreferenceVisuals();
    }

    private void ClearPreferenceButton_Click(object sender, RoutedEventArgs e)
    {
        _preference = null;
        UpdatePreferenceVisuals();
    }

    private void WatchlistButton_Click(object sender, RoutedEventArgs e)
    {
        _inWatchlist = !_inWatchlist;
        UpdateWatchlistVisuals();
    }

    private void MarkWatchedButton_Click(object sender, RoutedEventArgs e) => AddWatch(DateTimeOffset.Now);

    private void MarkOnDateButton_Click(object sender, RoutedEventArgs e)
    {
        if (WatchedDatePicker.Date is DateTimeOffset date)
        {
            AddWatch(date);
            WatchedDatePicker.Date = null;
            MarkOnDateButton.IsEnabled = false;
        }
    }

    private void WatchedDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        MarkOnDateButton.IsEnabled = sender.Date is not null;

    private void RemoveHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WatchHistoryDisplayRecord entry })
        {
            _history.Remove(entry);
            UpdateHistoryVisibility();
        }
    }

    private void AddWatch(DateTimeOffset date)
    {
        var label = LocalizationService.Text(_history.Count == 0 ? "FirstWatch" : "Rewatch");
        _history.Add(new WatchHistoryDisplayRecord(
            new WatchHistoryRecord(LocalizationService.Digits(date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)), label),
            LocalizationService.Text("RemoveWatchEvent")));
        UpdateHistoryVisibility();
    }

    /// <summary>Active states use the native accent / critical button styles, never literal colors.</summary>
    private void UpdatePreferenceVisuals()
    {
        LikeButton.Style = (Style)App.Current.Resources[
            _preference == "liked" ? "ActiveControlButtonStyle" : "SubtleControlButtonStyle"];
        LikeGlyph.Glyph = _preference == "liked" ? "\uEB52" : "\uEB51";
        ExcludeButton.Style = (Style)App.Current.Resources[
            _preference == "blacklisted" ? "DangerControlButtonStyle" : "SubtleControlButtonStyle"];
        ClearPreferenceButton.IsEnabled = _preference is not null;
    }

    private void UpdateWatchlistVisuals()
    {
        WatchlistButton.Style = (Style)App.Current.Resources[
            _inWatchlist ? "ActiveControlButtonStyle" : "SubtleControlButtonStyle"];
        WatchlistGlyph.Glyph = _inWatchlist ? "\uE735" : "\uE734";
        WatchlistText.Text = LocalizationService.Text(_inWatchlist ? "InWatchlist" : "AddToWatchlist");
    }

    /// <summary>Re-stamps the localized remove-button label on rows created under another language.</summary>
    private void RelabelHistory()
    {
        var removeLabel = LocalizationService.Text("RemoveWatchEvent");
        for (var index = 0; index < _history.Count; index++)
        {
            if (_history[index].RemoveLabel != removeLabel)
            {
                _history[index] = _history[index] with { RemoveLabel = removeLabel };
            }
        }

        UpdateHistoryVisibility();
    }

    private void UpdateHistoryVisibility()
    {
        NoHistoryText.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryRepeater.Visibility = _history.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
