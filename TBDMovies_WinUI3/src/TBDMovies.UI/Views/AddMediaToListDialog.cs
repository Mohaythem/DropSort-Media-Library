using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TBDMovies.Domain.Library.Personal;
using TBDMovies.UI.Services;
using Microsoft.UI.Xaml.Controls;

namespace TBDMovies.UI.Views;

/// <summary>One library item offered by the picker: a movie or a TV show already registered in TBDMovies.</summary>
internal sealed record MediaPickerEntry(CustomListMediaKind Kind, int Id, string Title, int? Year, bool AlreadyMember);

/// <summary>A picked library item to add to the list.</summary>
internal readonly record struct MediaPick(CustomListMediaKind Kind, int Id);

/// <summary>
/// A lightweight picker for adding existing library items - movies and TV shows - to a custom list. It
/// never imports files, scans folders or fetches metadata: it lists items already registered in TBDMovies,
/// tags each with its media type, disables the ones already in the list, and returns what the user chose.
/// Native, searchable and multi-select.
/// </summary>
internal sealed class AddMediaToListDialog
{
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Multiple };
    private readonly IReadOnlyList<MediaPickerEntry> _entries;
    private readonly HashSet<MediaPick> _selected = [];
    private bool _suppress;
    private string _filter = string.Empty;

    private AddMediaToListDialog(IReadOnlyList<MediaPickerEntry> entries)
    {
        _entries = entries;
        _list.SelectionChanged += OnSelectionChanged;
        Render();
    }

    /// <summary>Shows the picker and returns the items the user chose, or an empty list when cancelled.</summary>
    public static Task<IReadOnlyList<MediaPick>> ShowAsync(
        Microsoft.UI.Xaml.XamlRoot xamlRoot,
        IReadOnlyList<MediaPickerEntry> entries) =>
        new AddMediaToListDialog(entries).RunAsync(xamlRoot);

    // PLACEHOLDER_APPEND
    private async Task<IReadOnlyList<MediaPick>> RunAsync(Microsoft.UI.Xaml.XamlRoot xamlRoot)
    {
        var search = new AutoSuggestBox
        {
            PlaceholderText = LocalizationService.Text("SearchLibrary"),
            QueryIcon = new SymbolIcon(Symbol.Find),
        };
        search.TextChanged += (sender, _) =>
        {
            _filter = sender.Text?.Trim() ?? string.Empty;
            Render();
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(search);
        panel.Children.Add(new ScrollViewer
        {
            Content = _list,
            MaxHeight = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            RequestedTheme = ThemeService.ElementTheme,
            FlowDirection = LocalizationService.FlowDirection,
            Title = LocalizationService.Text("SelectMedia"),
            Content = panel,
            PrimaryButtonText = LocalizationService.Text("Add"),
            CloseButtonText = LocalizationService.Text("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await ContentDialogCoordinator.ShowAsync(dialog);
        return result == ContentDialogResult.Primary ? _selected.ToArray() : [];
    }

    /// <summary>Rebuilds the visible rows for the current filter, re-applying the running selection.</summary>
    private void Render()
    {
        _suppress = true;
        _list.Items.Clear();
        foreach (var entry in _entries.Where(Matches))
        {
            var kindLabel = LocalizationService.Text(entry.Kind == CustomListMediaKind.Show ? "TvShow" : "Movie");
            var title = entry.Year is int year and > 0 ? $"{entry.Title} ({year})" : entry.Title;
            var label = $"{kindLabel} · {title}";
            if (entry.AlreadyMember) label += " — " + LocalizationService.Text("AlreadyInList");
            var item = new ListViewItem
            {
                Content = LocalizationService.Digits(label),
                Tag = new MediaPick(entry.Kind, entry.Id),
                IsEnabled = !entry.AlreadyMember,
            };
            _list.Items.Add(item);
            if (!entry.AlreadyMember && _selected.Contains(new MediaPick(entry.Kind, entry.Id)))
                _list.SelectedItems.Add(item);
        }
        _suppress = false;
    }

    private bool Matches(MediaPickerEntry entry)
    {
        if (_filter.Length == 0) return true;
        if (entry.Title.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)) return true;
        return entry.Year is int year
            && year.ToString(CultureInfo.InvariantCulture).Contains(_filter, StringComparison.Ordinal);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        foreach (var item in e.RemovedItems.OfType<ListViewItem>())
            if (item.Tag is MediaPick pick) _selected.Remove(pick);
        foreach (var item in e.AddedItems.OfType<ListViewItem>())
            if (item.Tag is MediaPick pick) _selected.Add(pick);
    }
}
