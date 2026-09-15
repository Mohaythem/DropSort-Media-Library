using System;
using System.Globalization;
using System.Threading.Tasks;
using DropSort.Domain.Metadata.Contracts;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DropSort.UI.Views;

public sealed class MatchMediaDialog : ContentDialog
{
    private readonly TextBox _titleBox;
    private readonly TextBox _yearBox;
    private readonly Button _searchButton;
    private readonly ListView _candidateList;
    private readonly ProgressRing _progressRing;
    private readonly TextBlock _statusText;
    private readonly StackPanel _statusPanel;

    private readonly bool _isMovie;
    private CancellationTokenSource? _searchCts;
    private bool _closed;
    private bool _isSearching;

    public MovieCandidate? SelectedMovieCandidate { get; private set; }
    public TvCandidate? SelectedTvCandidate { get; private set; }

    private MatchMediaDialog(bool isMovie, string initialTitle, int? initialYear)
    {
        _isMovie = isMovie;

        Title = LocalizationService.Text("MatchWithTmdb");
        PrimaryButtonText = LocalizationService.Text("ApplyMatch");
        CloseButtonText = LocalizationService.Text("Cancel");
        IsPrimaryButtonEnabled = false;
        DefaultButton = ContentDialogButton.Primary;
        RequestedTheme = ThemeService.ElementTheme;
        FlowDirection = LocalizationService.FlowDirection;

        var contentStack = new StackPanel
        {
            Spacing = 12,
            Width = 460
        };

        // Search inputs row
        var searchGrid = new Grid { ColumnSpacing = 8 };
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleBox = new TextBox
        {
            PlaceholderText = LocalizationService.Text("Title"),
            Text = initialTitle ?? string.Empty
        };
        _titleBox.KeyDown += OnInputKeyDown;
        Grid.SetColumn(_titleBox, 0);

        _yearBox = new TextBox
        {
            PlaceholderText = LocalizationService.Text("Year"),
            Text = initialYear.HasValue && initialYear.Value > 0 ? initialYear.Value.ToString(CultureInfo.InvariantCulture) : string.Empty
        };
        _yearBox.KeyDown += OnInputKeyDown;
        Grid.SetColumn(_yearBox, 1);

        _searchButton = new Button
        {
            Content = LocalizationService.Text("SearchTmdb"),
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentControlButtonStyle"]
        };
        _searchButton.Click += async (_, _) => await PerformSearchAsync();
        Grid.SetColumn(_searchButton, 2);

        searchGrid.Children.Add(_titleBox);
        searchGrid.Children.Add(_yearBox);
        searchGrid.Children.Add(_searchButton);
        contentStack.Children.Add(searchGrid);

        // Status / progress indicator
        _statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Visibility = Visibility.Collapsed
        };
        _progressRing = new ProgressRing
        {
            Width = 16,
            Height = 16,
            IsActive = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SecondaryBodyTextStyle"]
        };
        _statusPanel.Children.Add(_progressRing);
        _statusPanel.Children.Add(_statusText);
        contentStack.Children.Add(_statusPanel);

        // Candidates list
        _candidateList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 280,
            MinHeight = 120
        };
        _candidateList.SelectionChanged += OnCandidateSelectionChanged;
        contentStack.Children.Add(_candidateList);

        Content = contentStack;
        PrimaryButtonClick += OnPrimaryButtonClick;
        Closed += (_, _) =>
        {
            _closed = true;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        };
        Opened += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_titleBox.Text))
            {
                await PerformSearchAsync();
            }
        };
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = PerformSearchAsync();
        }
    }

    private void OnCandidateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = _candidateList.SelectedItem != null;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_candidateList.SelectedItem is ListViewItem item)
        {
            if (_isMovie)
            {
                SelectedMovieCandidate = item.Tag as MovieCandidate;
            }
            else
            {
                SelectedTvCandidate = item.Tag as TvCandidate;
            }
        }
    }

    private async Task PerformSearchAsync()
    {
        if (_closed || _isSearching) return;

        var title = _titleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title)) return;

        int? year = int.TryParse(_yearBox.Text.Trim(), out var parsedYear) && parsedYear > 0 ? parsedYear : null;

        _isSearching = true;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var cancellationToken = _searchCts.Token;
        _searchButton.IsEnabled = false;
        _candidateList.Items.Clear();
        IsPrimaryButtonEnabled = false;

        _statusPanel.Visibility = Visibility.Visible;
        _progressRing.IsActive = true;
        _statusText.Text = LocalizationService.Text("SearchingTmdb");

        try
        {
            if (_isMovie)
            {
                var decision = await Task.Run(() => AppServices.Matching.SearchMovieCandidatesAsync(title, year, cancellationToken));
                if (_closed || cancellationToken.IsCancellationRequested) return;
                _progressRing.IsActive = false;

                if (decision.RankedCandidates.Length == 0)
                {
                    _statusText.Text = LocalizationService.Text("NoMatchesFound");
                }
                else
                {
                    _statusPanel.Visibility = Visibility.Collapsed;
                    foreach (var scored in decision.RankedCandidates)
                    {
                        var candidate = scored.Candidate;
                        var item = new ListViewItem
                        {
                            Content = BuildCandidateView(candidate.Title, candidate.Year, scored.Score, candidate.Overview),
                            Tag = candidate,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch
                        };
                        _candidateList.Items.Add(item);
                    }
                }
            }
            else
            {
                var decision = await Task.Run(() => AppServices.Matching.SearchTvCandidatesAsync(title, year, cancellationToken));
                if (_closed || cancellationToken.IsCancellationRequested) return;
                _progressRing.IsActive = false;

                if (decision.RankedCandidates.Length == 0)
                {
                    _statusText.Text = LocalizationService.Text("NoMatchesFound");
                }
                else
                {
                    _statusPanel.Visibility = Visibility.Collapsed;
                    foreach (var scored in decision.RankedCandidates)
                    {
                        var candidate = scored.Candidate;
                        var item = new ListViewItem
                        {
                            Content = BuildCandidateView(candidate.Title, candidate.FirstAirYear, scored.Score, candidate.Overview),
                            Tag = candidate,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch
                        };
                        _candidateList.Items.Add(item);
                    }
                }
            }
        }
        catch (OperationCanceledException error)
        {
            if (!_closed && !cancellationToken.IsCancellationRequested)
            {
                _progressRing.IsActive = false;
                _statusText.Text = MetadataErrorText.For(error);
            }
        }
        catch (Exception ex)
        {
            if (!_closed && !cancellationToken.IsCancellationRequested)
            {
                _progressRing.IsActive = false;
                _statusText.Text = MetadataErrorText.For(ex);
            }
        }
        finally
        {
            if (!_closed && !cancellationToken.IsCancellationRequested)
            {
                _isSearching = false;
                _searchButton.IsEnabled = true;
            }
        }
    }

    private static UIElement BuildCandidateView(string title, int? year, double score, string? overview)
    {
        var rootGrid = new Grid
        {
            Margin = new Thickness(0, 4, 0, 4),
            RowSpacing = 4,
        };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (!string.IsNullOrWhiteSpace(overview))
        {
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var headerGrid = new Grid { ColumnSpacing = 8 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(titleBlock);

        if (year.HasValue && year.Value > 0)
        {
            var yearBlock = new TextBlock
            {
                Text = $"({year.Value})",
                Opacity = 0.7,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            titlePanel.Children.Add(yearBlock);
        }
        headerGrid.Children.Add(titlePanel);

        var scorePct = Math.Clamp((int)Math.Round(score * 100), 0, 100);
        var scoreBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SubtleFillColorSecondaryBrush"]
        };
        Grid.SetColumn(scoreBorder, 1);
        var scoreBlock = new TextBlock
        {
            Text = $"{scorePct}%",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        };
        scoreBorder.Child = scoreBlock;
        headerGrid.Children.Add(scoreBorder);

        rootGrid.Children.Add(headerGrid);

        if (!string.IsNullOrWhiteSpace(overview))
        {
            var overviewBlock = new TextBlock
            {
                Text = overview,
                FontSize = 12,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(overviewBlock, 1);
            rootGrid.Children.Add(overviewBlock);
        }

        return rootGrid;
    }

    public static async Task<MovieCandidate?> ShowForMovieAsync(FrameworkElement parent, string initialTitle, int? initialYear)
    {
        var dialog = new MatchMediaDialog(isMovie: true, initialTitle, initialYear)
        {
            XamlRoot = parent.XamlRoot
        };

        var result = await ContentDialogCoordinator.ShowAsync(dialog);
        return result == ContentDialogResult.Primary ? dialog.SelectedMovieCandidate : null;
    }

    public static async Task<TvCandidate?> ShowForTvShowAsync(FrameworkElement parent, string initialTitle, int? initialYear)
    {
        var dialog = new MatchMediaDialog(isMovie: false, initialTitle, initialYear)
        {
            XamlRoot = parent.XamlRoot
        };

        var result = await ContentDialogCoordinator.ShowAsync(dialog);
        return result == ContentDialogResult.Primary ? dialog.SelectedTvCandidate : null;
    }
}
