using System;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using DropSort.Application.Dto;
using DropSort.Application.Text;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DropSort.UI.Views;

/// <summary>
/// Picks an existing TV series and the season/episode to move a local item into. Season/episode are
/// pre-filled from the filename when they could be inferred, and the user can correct them. It only
/// gathers the choice; the catalog change is done by the caller through the repair service.
/// <para>
/// The Season/Episode fields are plain digit boxes that always present ASCII digits: a WinUI NumberBox
/// shapes its value with the current culture's numeral system, so in Arabic "29" would display as "٢٩".
/// These boxes instead hold the literal ASCII text and fold any pasted Arabic/Persian digits to ASCII,
/// so the number the user sees and types is always 0-9 while the value stays a plain integer.
/// </para>
/// </summary>
public sealed class MoveToSeriesDialog : ContentDialog
{
    private const int MaxDigits = 3; // seasons/episodes are 0-999

    private readonly ComboBox _showBox;
    private readonly TextBox _seasonBox;
    private readonly TextBox _episodeBox;
    private bool _syncing; // guards the normalizer's own Text write from re-entering itself

    private MoveToSeriesDialog(IReadOnlyList<TvShowListItem> shows, int? inferredSeason, int? inferredEpisode)
    {
        Title = LocalizationService.Text("AddOrMoveToSeries");
        PrimaryButtonText = LocalizationService.Text("MoveToSeriesConfirm");
        CloseButtonText = LocalizationService.Text("Cancel");
        DefaultButton = ContentDialogButton.Primary;
        RequestedTheme = ThemeService.ElementTheme;
        FlowDirection = LocalizationService.FlowDirection;

        var stack = new StackPanel { Spacing = 12, Width = 380 };

        _showBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = LocalizationService.Text("SelectSeries"),
        };
        foreach (var show in shows)
        {
            var label = show.Year is int year and > 0
                ? $"{show.Title} ({year.ToString(CultureInfo.InvariantCulture)})"
                : show.Title;
            _showBox.Items.Add(new ComboBoxItem { Content = label, Tag = show.Id });
        }

        if (shows.Count > 0)
        {
            _showBox.SelectedIndex = 0;
        }

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Text("SelectSeries"),
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SecondaryCaptionTextStyle"],
        });
        stack.Children.Add(_showBox);

        var numbers = new Grid { ColumnSpacing = 12 };
        numbers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        numbers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Season defaults to 1 (a sensible flat-show default); the episode is left empty when it could not
        // be inferred, so the user must enter it rather than the dialog silently guessing a wrong number.
        _seasonBox = DigitBox(LocalizationService.Text("Season"), (inferredSeason ?? 1).ToString(CultureInfo.InvariantCulture));
        Grid.SetColumn(_seasonBox, 0);
        _episodeBox = DigitBox(LocalizationService.Text("Episode"),
            inferredEpisode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        Grid.SetColumn(_episodeBox, 1);

        numbers.Children.Add(_seasonBox);
        numbers.Children.Add(_episodeBox);
        stack.Children.Add(numbers);

        if (shows.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = LocalizationService.Text("NoSeriesInLibrary"),
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SecondaryBodyTextStyle"],
            });
        }

        _showBox.SelectionChanged += (_, _) => UpdatePrimaryEnabled();
        Content = stack;
        UpdatePrimaryEnabled();
    }

    private TextBox DigitBox(string header, string initial)
    {
        var box = new TextBox
        {
            Header = header,
            Text = initial,
            MaxLength = MaxDigits,
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } },
            // Force an invariant ASCII numeric context, so the digits render as 0-9 in every state — before,
            // during and after editing — even when the dialog is Arabic/RTL. Without this the editing
            // surface inherits the Arabic language/RTL context and applies national digit substitution,
            // which showed the digits shaped (or as missing-glyph squares) once the field was focused.
            // Only these two numeric fields are made invariant; the rest of the dialog stays Arabic.
            FlowDirection = FlowDirection.LeftToRight,
            TextAlignment = TextAlignment.Left,
            Language = "en-US",
            FontFamily = new FontFamily("Segoe UI"),
        };
        box.TextChanged += OnDigitTextChanged;
        return box;
    }

    /// <summary>
    /// Forces the box to ASCII digits: folds any pasted Arabic/Persian digits and drops non-digits, so the
    /// field never shows or keeps a localized/invalid character. Re-entrancy is guarded by comparing text.
    /// </summary>
    private void OnDigitTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || sender is not TextBox box)
        {
            return;
        }

        var normalized = AsciiDigits.OnlyAsciiDigits(box.Text);
        if (normalized.Length > MaxDigits)
        {
            normalized = normalized[..MaxDigits];
        }

        // Only rewrite when the text actually contained a non-ASCII-digit (a pasted Arabic/Persian digit or
        // a stray character). Normal ASCII typing is left untouched, so the caret and rendering never
        // glitch. The guard flag keeps this single write from re-entering the handler.
        if (!string.Equals(box.Text, normalized, StringComparison.Ordinal))
        {
            _syncing = true;
            box.Text = normalized;
            box.SelectionStart = normalized.Length;
            _syncing = false;
        }

        UpdatePrimaryEnabled();
    }

    /// <summary>Confirm is available only with a chosen series and a real (non-empty) episode number.</summary>
    private void UpdatePrimaryEnabled() =>
        IsPrimaryButtonEnabled = _showBox.SelectedItem is ComboBoxItem && _episodeBox.Text.Length > 0;

    public static async Task<(int ShowId, int Season, int Episode)?> ShowAsync(
        FrameworkElement parent,
        IReadOnlyList<TvShowListItem> shows,
        int? inferredSeason,
        int? inferredEpisode)
    {
        var dialog = new MoveToSeriesDialog(shows, inferredSeason, inferredEpisode)
        {
            XamlRoot = parent.XamlRoot,
        };

        if (await ContentDialogCoordinator.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        if (dialog._showBox.SelectedItem is not ComboBoxItem { Tag: int showId })
        {
            return null;
        }

        // The boxes hold only ASCII digits (0-999). An empty season means the default first season.
        var season = ParseDigits(dialog._seasonBox.Text, fallback: 1);
        var episode = ParseDigits(dialog._episodeBox.Text, fallback: 1);
        return (showId, season, episode);
    }

    private static int ParseDigits(string text, int fallback) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, 999)
            : fallback;
}
