using TBDMovies.Domain.Library.Personal;
using TBDMovies.UI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace TBDMovies.UI.Services;

/// <summary>One context menu implementation shared by every media card surface.</summary>
public static class MediaCardContextMenu
{
    public static bool ShowForContext(FrameworkElement owner, ContextRequestedEventArgs e,
        bool horizontalMovies = false, Action? onChanged = null, Action? removeFromList = null)
    {
        var source = ResolveSource(owner, e);
        if (source is null) return false;
        var point = e.TryGetPosition(owner, out var position) ? position : new Windows.Foundation.Point();
        var menu = new MenuFlyout();
        try
        {
            switch (source)
            {
                case MovieRecord movie: BuildMovieMenu(menu, owner, movie, horizontalMovies, onChanged, removeFromList); break;
                case ShowCardItem show: BuildShowMenu(menu, owner, show, horizontalMovies, onChanged, removeFromList); break;
                case ListMediaItem list when list.Movie is { } movie: BuildMovieMenu(menu, owner, movie, horizontalMovies, onChanged, removeFromList); break;
                case ListMediaItem list when list.Show is { } show:
                    AddImageItem(menu, owner, list.PosterSource, list.Title);
                    BuildShowActions(menu, owner, show, onChanged);
                    AddRemove(menu, removeFromList); break;
                case HomeActivityCard activity: BuildActivityMenu(menu, owner, activity, onChanged); break;
                case WatchHistoryCardRecord history: BuildHistoryMenu(menu, owner, history, onChanged); break;
                default: return false;
            }
        }
        catch (Exception error) { _ = ShowErrorAsync(owner, error); return false; }
        if (menu.Items.Count == 0) return false;
        e.Handled = true;
        menu.ShowAt(owner, new FlyoutShowOptions { Position = point });
        return true;
    }

    private static object? ResolveSource(FrameworkElement owner, ContextRequestedEventArgs e)
    {
        DependencyObject? element = e.OriginalSource as DependencyObject;
        while (element is not null)
        {
            if (element is FrameworkElement current)
            {
                if (current.DataContext is MovieRecord or ShowCardItem or ListMediaItem or HomeActivityCard or WatchHistoryCardRecord)
                    return current.DataContext;
                if (current.Tag is MovieRecord taggedMovie) return taggedMovie;
                if (current.Tag is ShowCardItem taggedShow) return taggedShow;
                if (current.Tag is ListMediaItem taggedList) return taggedList;
                if (current.Tag is HomeActivityCard taggedActivity) return taggedActivity;
                if (current.Tag is WatchHistoryCardRecord taggedHistory) return taggedHistory;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        if (!e.TryGetPosition(owner, out _) && FocusManager.GetFocusedElement(owner.XamlRoot) is FrameworkElement focused)
        {
            for (DependencyObject? current = focused; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is FrameworkElement item && item.DataContext is not null) return item.DataContext;
        }
        return null;
    }

    private static void BuildMovieMenu(MenuFlyout menu, FrameworkElement owner, MovieRecord movie,
        bool horizontal, Action? changed, Action? remove)
    {
        AddImageItem(menu, owner, horizontal ? movie.HorizontalArtworkSource ?? movie.PosterSource : movie.PosterSource, movie.Title);
        var state = AppServices.Personal.GetPersonalSnapshot(movie.Id);
        var fav = new ToggleMenuFlyoutItem { Text = LocalizationService.Text(state.Preference == PersonalPreference.Liked ? "InFavorites" : "AddToFavorites"), IsChecked = state.Preference == PersonalPreference.Liked };
        fav.Click += (_, _) => RunSafe(owner, () => { if (state.Preference == PersonalPreference.Liked) AppServices.Personal.ClearPersonalPreference(movie.Id); else AppServices.Personal.SetPersonalPreference(movie.Id, PersonalPreference.Liked); changed?.Invoke(); });
        menu.Items.Add(fav);
        var watch = new MenuFlyoutItem { Text = LocalizationService.Text(state.Watchlisted ? "RemoveFromWatchlist" : "AddToWatchlist") };
        watch.Click += (_, _) => RunSafe(owner, () => { if (state.Watchlisted) AppServices.Personal.RemoveFromWatchlist(movie.Id); else AppServices.Personal.AddToWatchlist(movie.Id); changed?.Invoke(); });
        menu.Items.Add(watch);
        AddMovieLists(menu, owner, movie.Id, changed);
        AddRemove(menu, remove);
    }

    private static void BuildShowMenu(MenuFlyout menu, FrameworkElement owner, ShowCardItem card,
        bool horizontal, Action? changed, Action? remove)
    {
        var show = card.Show;
        AddImageItem(menu, owner, horizontal ? card.HorizontalArtworkSource ?? card.PosterSource : card.PosterSource, card.Title);
        var state = AppServices.ShowPersonal.GetState(show.Id);
        var fav = new ToggleMenuFlyoutItem { Text = LocalizationService.Text(state.IsFavorite ? "InFavorites" : "AddToFavorites"), IsChecked = state.IsFavorite };
        fav.Click += (_, _) => RunSafe(owner, () => { AppServices.ShowPersonal.SetFavorite(show.Id, !state.IsFavorite); changed?.Invoke(); });
        menu.Items.Add(fav);
        var watch = new MenuFlyoutItem { Text = LocalizationService.Text(state.WatchLater ? "RemoveFromWatchlist" : "AddToWatchlist") };
        watch.Click += (_, _) => RunSafe(owner, () => { AppServices.ShowPersonal.SetWatchLater(show.Id, !state.WatchLater); changed?.Invoke(); });
        menu.Items.Add(watch);
        AddShowLists(menu, owner, show.Id, changed);
        AddRemove(menu, remove);
    }

    private static void BuildActivityMenu(MenuFlyout menu, FrameworkElement owner, HomeActivityCard card, Action? changed)
    {
        AddImageItem(menu, owner, card.ArtworkSource ?? card.PosterSource, card.Title);
        if (card.MovieId is int movieId && AppServices.Library.ListMovies().FirstOrDefault(x => x.Id == movieId) is { } movie)
            BuildMovieActions(menu, owner, LibraryProjection.ToCard(movie), changed);
        else if (card.ShowId is int showId && AppServices.Tv.ListShows().FirstOrDefault(x => x.Id == showId) is { } show)
            BuildShowActions(menu, owner, TvProjection.ToCard(show), changed);
    }

    private static void BuildHistoryMenu(MenuFlyout menu, FrameworkElement owner, WatchHistoryCardRecord card, Action? changed)
    {
        AddImageItem(menu, owner, card.ArtworkSource, card.Title);
        if (!card.IsAvailable) return;
        if (card.MediaKind == WatchEventMediaKind.Movie && card.MovieId is int movieId && AppServices.Library.ListMovies().FirstOrDefault(x => x.Id == movieId) is { } movie)
            BuildMovieActions(menu, owner, LibraryProjection.ToCard(movie), changed);
        else if (card.MediaKind == WatchEventMediaKind.Episode && card.ShowId is int showId && AppServices.Tv.ListShows().FirstOrDefault(x => x.Id == showId) is { } show)
            BuildShowActions(menu, owner, TvProjection.ToCard(show), changed);
    }

    private static void BuildMovieActions(MenuFlyout menu, FrameworkElement owner, MovieRecord movie, Action? changed)
    {
        var state = AppServices.Personal.GetPersonalSnapshot(movie.Id);
        var fav = new ToggleMenuFlyoutItem { Text = LocalizationService.Text(state.Preference == PersonalPreference.Liked ? "InFavorites" : "AddToFavorites"), IsChecked = state.Preference == PersonalPreference.Liked };
        fav.Click += (_, _) => RunSafe(owner, () => { if (state.Preference == PersonalPreference.Liked) AppServices.Personal.ClearPersonalPreference(movie.Id); else AppServices.Personal.SetPersonalPreference(movie.Id, PersonalPreference.Liked); changed?.Invoke(); });
        menu.Items.Add(fav);
        var watch = new MenuFlyoutItem { Text = LocalizationService.Text(state.Watchlisted ? "RemoveFromWatchlist" : "AddToWatchlist") };
        watch.Click += (_, _) => RunSafe(owner, () => { if (state.Watchlisted) AppServices.Personal.RemoveFromWatchlist(movie.Id); else AppServices.Personal.AddToWatchlist(movie.Id); changed?.Invoke(); });
        menu.Items.Add(watch);
        AddMovieLists(menu, owner, movie.Id, changed);
    }

    private static void BuildShowActions(MenuFlyout menu, FrameworkElement owner, TVShowRecord show, Action? changed)
    {
        var state = AppServices.ShowPersonal.GetState(show.Id);
        var fav = new ToggleMenuFlyoutItem { Text = LocalizationService.Text(state.IsFavorite ? "InFavorites" : "AddToFavorites"), IsChecked = state.IsFavorite };
        fav.Click += (_, _) => RunSafe(owner, () => { AppServices.ShowPersonal.SetFavorite(show.Id, !state.IsFavorite); changed?.Invoke(); });
        menu.Items.Add(fav);
        var watch = new MenuFlyoutItem { Text = LocalizationService.Text(state.WatchLater ? "RemoveFromWatchlist" : "AddToWatchlist") };
        watch.Click += (_, _) => RunSafe(owner, () => { AppServices.ShowPersonal.SetWatchLater(show.Id, !state.WatchLater); changed?.Invoke(); });
        menu.Items.Add(watch);
        AddShowLists(menu, owner, show.Id, changed);
    }

    private static void AddImageItem(MenuFlyout menu, FrameworkElement owner, ImageSource? source, string title)
    {
        var item = new MenuFlyoutItem { Text = LocalizationService.Text("ViewImage"), IsEnabled = source is not null };
        item.Click += async (_, _) => { try { if (source is not null) await ArtworkViewer.ShowAsync(owner, source, title); } catch (Exception error) { await ShowErrorAsync(owner, error); } };
        menu.Items.Add(item);
    }

    private static void AddRemove(MenuFlyout menu, Action? remove)
    {
        if (remove is null) return;
        var item = new MenuFlyoutItem { Text = LocalizationService.Text("RemoveFromList") };
        item.Click += (_, _) => remove();
        menu.Items.Add(item);
    }

    private static void AddMovieLists(MenuFlyout menu, FrameworkElement owner, int id, Action? changed)
    {
        var sub = new MenuFlyoutSubItem { Text = LocalizationService.Text("AddToList") };
        foreach (var list in AppServices.Personal.ListCustomLists())
        {
            var entry = new ToggleMenuFlyoutItem { Text = list.Name, IsChecked = AppServices.Personal.GetCustomListIdsForMovie(id).Contains(list.Id) };
            entry.Click += (_, _) => RunSafe(owner, () => { if (AppServices.Personal.GetCustomListIdsForMovie(id).Contains(list.Id)) AppServices.Personal.RemoveMovieFromCustomList(list.Id, id); else AppServices.Personal.AddMovieToCustomList(list.Id, id); changed?.Invoke(); });
            sub.Items.Add(entry);
        }
        if (sub.Items.Count == 0) sub.Items.Add(new MenuFlyoutItem { Text = LocalizationService.Text("NoCustomLists"), IsEnabled = false });
        menu.Items.Add(sub);
    }

    private static void AddShowLists(MenuFlyout menu, FrameworkElement owner, int id, Action? changed)
    {
        var sub = new MenuFlyoutSubItem { Text = LocalizationService.Text("AddToList") };
        foreach (var list in AppServices.Personal.ListCustomLists())
        {
            var entry = new ToggleMenuFlyoutItem { Text = list.Name, IsChecked = AppServices.Personal.GetCustomListIdsForShow(id).Contains(list.Id) };
            entry.Click += (_, _) => RunSafe(owner, () => { if (AppServices.Personal.GetCustomListIdsForShow(id).Contains(list.Id)) AppServices.Personal.RemoveShowFromCustomList(list.Id, id); else AppServices.Personal.AddShowToCustomList(list.Id, id); changed?.Invoke(); });
            sub.Items.Add(entry);
        }
        if (sub.Items.Count == 0) sub.Items.Add(new MenuFlyoutItem { Text = LocalizationService.Text("NoCustomLists"), IsEnabled = false });
        menu.Items.Add(sub);
    }

    private static void RunSafe(FrameworkElement owner, Action action)
    {
        try { action(); }
        catch (Exception error) { _ = ShowErrorAsync(owner, error); }
    }

    private static async Task ShowErrorAsync(FrameworkElement owner, Exception error)
    {
        if (owner.XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            Title = LocalizationService.Text("Failed"),
            Content = MetadataErrorText.For(error),
            CloseButtonText = LocalizationService.Text("Close"),
            FlowDirection = LocalizationService.FlowDirection,
        };
        try { await ContentDialogCoordinator.ShowAsync(dialog); } catch { }
    }
}

internal static class ArtworkViewer
{
    public static async Task ShowAsync(FrameworkElement owner, ImageSource source, string title)
    {
        if (owner.XamlRoot is not { } root) return;
        // Decode the actual asset independently of the card's rendering/decode hints.
        var original = source is BitmapImage { UriSource: { } uri } ? new BitmapImage(uri) : source;
        var image = new Image { Source = original, Stretch = Stretch.Uniform, FlowDirection = FlowDirection.LeftToRight };
        var content = new Border { Child = image, RequestedTheme = owner.ActualTheme, Style = FindStyle<Border>(owner, "ArtworkViewerCanvasStyle") };
        var presenter = new Style(typeof(FlyoutPresenter)) { BasedOn = FindStyle<FlyoutPresenter>(owner, "ArtworkViewerPresenterStyle") };
        presenter.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, owner.ActualTheme));
        presenter.Setters.Add(new Setter(FrameworkElement.FlowDirectionProperty, LocalizationService.FlowDirection));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, title);
        var viewer = new Flyout { Content = content, Placement = FlyoutPlacementMode.Full, FlyoutPresenterStyle = presenter, LightDismissOverlayMode = LightDismissOverlayMode.On };
        FlyoutPresenter? surface = null;
        var frame = 2 / root.RasterizationScale; // Two physical pixels on every monitor.
        presenter.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(frame)));
        void Resize()
        {
            frame = 2 / root.RasterizationScale;
            var bitmap = original as BitmapSource;
            if (bitmap is null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                bitmap = source as BitmapSource;
            var size = ArtworkViewerLayout.Fit(root.Size.Width, root.Size.Height,
                bitmap?.PixelWidth ?? 0, bitmap?.PixelHeight ?? 0);
            image.Width = content.Width = size.Width;
            image.Height = content.Height = size.Height;
            // Full placement centers the flyout but also stretches its presenter. Bound the
            // presenter itself, not only its child, so its frame hugs the original image.
            if (surface is not null)
            {
                surface.Width = surface.MaxWidth = size.Width + frame * 2;
                surface.Height = surface.MaxHeight = size.Height + frame * 2;
                surface.BorderThickness = new Thickness(frame);
            }
        }
        void OnOpened(object? sender, object args)
        {
            for (DependencyObject? parent = content; parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is FlyoutPresenter found) { surface = found; break; }
            Resize();
        }
        // Also constrain the initial Full-placement pass, before the presenter is in the tree.
        var initial = ArtworkViewerLayout.Fit(root.Size.Width, root.Size.Height,
            (source as BitmapSource)?.PixelWidth ?? 0, (source as BitmapSource)?.PixelHeight ?? 0);
        presenter.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, initial.Width + frame * 2));
        presenter.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, initial.Height + frame * 2));
        var closed = new TaskCompletionSource<bool>();
        viewer.Closed += (_, _) => closed.TrySetResult(true);
        TypedEventHandler<XamlRoot, XamlRootChangedEventArgs> rootChanged = (_, _) => Resize();
        RoutedEventHandler imageOpened = (_, _) => Resize();
        root.Changed += rootChanged; image.ImageOpened += imageOpened; viewer.Opened += OnOpened;
        try { Resize(); viewer.ShowAt(owner); await closed.Task; }
        finally { root.Changed -= rootChanged; image.ImageOpened -= imageOpened; viewer.Opened -= OnOpened; }
    }

    private static Style? FindStyle<T>(FrameworkElement owner, string key) where T : FrameworkElement
        => owner.Resources.TryGetValue(key, out var local) && local is Style style ? style : Microsoft.UI.Xaml.Application.Current.Resources[key] as Style;
}
