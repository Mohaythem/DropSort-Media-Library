using DropSort.Application.Dto;
using DropSort.Application.Services;
using DropSort.Domain.Library.Movies;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DropSort.UI.Views;

/// <summary>One compact editor per action. Prepares read-only previews; never executes.</summary>
internal sealed class MovieOrganizationDialog : ContentDialog
{
    private readonly ComboBox _files = new();
    private readonly TextBlock _current = PathText();
    private readonly TextBox _name = new();
    private readonly TextBlock _extension = new();
    private readonly TextBox _folder = new();
    private readonly Button _choose = new();
    private readonly InfoBar _error = new() { Severity = InfoBarSeverity.Error, IsClosable = false };
    private readonly MovieDetails _movie;
    private readonly bool _move;
    private readonly Func<bool> _isCurrent;
    private readonly DropSort.Application.Contracts.IOrganizationUiActions _organization;
    private MediaFile? _singleFile;
    private string? _selectedDestination;
    private bool _preparing, _dismissed;
    private OrganizationPreview? _preview;
    private MediaFile? File => _singleFile ?? (_files.SelectedItem as FileChoice)?.File;

    internal MovieOrganizationDialog(MovieDetails movie, bool move, Func<bool> isCurrent)
    {
        _movie = movie; _move = move; _isCurrent = isCurrent;
        _organization = AppServices.Organization;
        Title = LocalizationService.Text(move ? "MovieMoveTitle" : "MovieRenameTitle");
        PrimaryButtonText = LocalizationService.Text(move ? "ReviewMovieMove" : "ReviewMovieRename");
        CloseButtonText = LocalizationService.Text("Cancel");
        DefaultButton = ContentDialogButton.Primary; // Enter reviews; it cannot execute from this editor.
        RequestedTheme = ThemeService.ElementTheme;
        FlowDirection = LocalizationService.FlowDirection;
        AutomationProperties.SetAutomationId(this, move ? "MovieMoveEditor" : "MovieRenameEditor");
        var body = new StackPanel { Spacing = 12, MaxWidth = 440 };
        var files = movie.MediaFiles.Where(f => f.Status == MediaFileStatus.Present).ToArray();
        if (files.Length == 1)
        {
            _singleFile = files[0];
            _current.MaxLines = 2;
            _current.TextTrimming = TextTrimming.CharacterEllipsis;
            body.Children.Add(_current);
        }
        else
        {
            _files.Header = LocalizationService.Text("OrganizationSelectedFile");
            _files.PlaceholderText = LocalizationService.Text("ChooseMovieFile");
            _files.HorizontalAlignment = HorizontalAlignment.Stretch;
            _files.FlowDirection = FlowDirection.LeftToRight;
            foreach (var file in files) _files.Items.Add(new FileChoice(file));
            AutomationProperties.SetAutomationId(_files, "OrganizationPhysicalFile");
            AutomationProperties.SetName(_files, LocalizationService.Text("OrganizationSelectedFile"));
            _files.SelectionChanged += (_, _) => ResetFile();
            body.Children.Add(_files); // Explicit choice: never preselect an ambiguous movie file.
        }
        if (move)
        {
            body.Children.Add(new TextBlock { Text = LocalizationService.Text("MovieDestinationFolder") });
            var destination = new Grid { ColumnSpacing = 8 };
            destination.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            destination.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _folder.IsReadOnly = true;
            _folder.FlowDirection = FlowDirection.LeftToRight;
            _folder.Language = "en-US";
            _folder.FontFamily = new FontFamily("Segoe UI");
            _folder.PlaceholderText = LocalizationService.Text("OrganizationChooseFolderFirst");
            AutomationProperties.SetAutomationId(_folder, "OrganizationDestinationFolder");
            AutomationProperties.SetName(_folder, LocalizationService.Text("MovieDestinationFolder"));
            _choose.Content = LocalizationService.Text("MovieBrowseFolder");
            AutomationProperties.SetAutomationId(_choose, "OrganizationChooseFolder");
            _choose.Click += ChooseFolder;
            Grid.SetColumn(_choose, 1);
            destination.Children.Add(_folder); destination.Children.Add(_choose);
            body.Children.Add(destination);
            body.Children.Add(new TextBlock { Text = LocalizationService.Text("MovieFolderCopyHelp"), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
        }
        _name.Header = LocalizationService.Text(move ? "MovieMoveFilename" : "OrganizationFilename");
        _name.FlowDirection = FlowDirection.LeftToRight;
        _name.Language = "en-US";
        _name.FontFamily = new FontFamily("Segoe UI");
        _name.TextAlignment = TextAlignment.Left;
        AutomationProperties.SetAutomationId(_name, "OrganizationBaseName");
        AutomationProperties.SetName(_name, LocalizationService.Text(move ? "MovieMoveFilename" : "OrganizationFilename"));
        _name.TextChanged += (_, _) => { _error.IsOpen = false; UpdateReview(); };
        body.Children.Add(_name);
        _extension.FlowDirection = FlowDirection.LeftToRight;
        _extension.IsTextSelectionEnabled = true;
        body.Children.Add(_extension);
        if (!move) body.Children.Add(new TextBlock { Text = LocalizationService.Text("MovieRenameStays"), TextWrapping = TextWrapping.Wrap });
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive);
        body.Children.Add(_error);
        Content = new ScrollViewer { Content = body, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12, 0, 12, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        PrimaryButtonClick += PreparePreview;
        Closed += (_, _) => _dismissed = true;
        Opened += (_, _) => (_singleFile is null ? (Control)_files : _name).Focus(FocusState.Programmatic);
        ResetFile();
    }

    // Reuse this instance on Back so the chosen file, folder and entered name are retained.
    internal async Task<OrganizationPreview?> ShowEditorAsync()
    {
        _dismissed = false; _preview = null;
        var handedOff = false;
        try
        {
            var result = await ContentDialogCoordinator.ShowAsync(this);
            if (result == ContentDialogResult.Primary && _isCurrent()) { handedOff = true; return _preview; }
            return null;
        }
        finally
        {
            if (_preview is { } preview && !handedOff) _organization.DiscardOrganizationPreview(preview.PreviewId);
        }
    }

    private async void ChooseFolder(object sender, RoutedEventArgs args)
    {
        _choose.IsEnabled = false;
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);
            var folder = await picker.PickSingleFolderAsync();
            if (!_dismissed && _isCurrent() && folder is not null)
            {
                _selectedDestination = folder.Path;
                _folder.Text = folder.Path; // Entire value can be selected/copied; confirmation wraps it.
                ToolTipService.SetToolTip(_folder, folder.Path);
                _error.IsOpen = false;
            }
        }
        catch { ShowError("OrganizationFolderUnavailable"); }
        finally { _choose.IsEnabled = true; UpdateReview(); }
    }

    private void ResetFile()
    {
        _error.IsOpen = false;
        if (File is { } file)
        {
            _current.Text = LocalizationService.Format("MovieCurrentFilename", Path.GetFileName(file.CurrentPath));
            ToolTipService.SetToolTip(_current, Path.GetFileName(file.CurrentPath));
            _name.MaxLength = Math.Max(1, 255 - Path.GetExtension(file.CurrentPath).Length);
            _name.Text = _move ? Path.GetFileNameWithoutExtension(file.CurrentPath)
                : MovieOrganizationNaming.SuggestBaseName(_movie.CanonicalTitle ?? _movie.Title, _movie.Year, _movie.HasTrustworthyMetadata, Path.GetFileName(file.CurrentPath));
            _extension.Text = LocalizationService.Format("OrganizationExtensionFormat", Path.GetExtension(file.CurrentPath));
        }
        _name.IsEnabled = File is not null;
        UpdateReview();
    }
    private void UpdateReview() => IsPrimaryButtonEnabled = !_preparing && File is not null
        && (_move ? _selectedDestination is not null : !string.IsNullOrWhiteSpace(_name.Text));

    private async void PreparePreview(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        _preparing = true; UpdateReview();
        _files.IsEnabled = _name.IsEnabled = _choose.IsEnabled = false;
        try
        {
            if (!_isCurrent() || File is not { } file) { args.Cancel = true; return; }
            // Empty means keep the current name; whitespace is an invalid entered filename.
            var basename = _move && string.IsNullOrEmpty(_name.Text) ? Path.GetFileNameWithoutExtension(file.CurrentPath) : _name.Text;
            if (!IsValidBaseName(basename)) throw new ArgumentException();
            var destinationRoot = _move ? _selectedDestination : Path.GetDirectoryName(file.CurrentPath);
            var filename = basename + Path.GetExtension(file.CurrentPath);
            var preview = await Task.Run(() => _organization.PrepareOrganization(file.Id, destinationRoot!, filename));
            if (_dismissed || !_isCurrent()) { _organization.DiscardOrganizationPreview(preview.PreviewId); args.Cancel = true; return; }
            _preview = preview;
        }
        catch (Exception error)
        {
            args.Cancel = true;
            ShowError(error switch
            {
                DropSort.FileSystem.Safety.DestinationExistsException or DropSort.FileSystem.Safety.CaseInsensitiveCollisionException
                    or OrganizationDestinationConflictException => "OrganizationCollision",
                DropSort.FileSystem.Safety.SameFileException => "OrganizationSameFile",
                ArgumentException => "OrganizationInvalidName",
                _ => "OrganizationPreviewUnavailable"
            });
        }
        finally
        {
            _preparing = false;
            _files.IsEnabled = _choose.IsEnabled = true;
            _name.IsEnabled = File is not null;
            UpdateReview();
            if (args.Cancel && !_dismissed && _isCurrent()) _name.Focus(FocusState.Programmatic);
            deferral.Complete();
        }
    }
    private void ShowError(string key)
    {
        _error.Message = LocalizationService.Text(key);
        AutomationProperties.SetName(_error, _error.Message);
        _error.IsOpen = true;
        _error.StartBringIntoView();
        FrameworkElementAutomationPeer.FromElement(_error)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private static bool IsValidBaseName(string name) => !string.IsNullOrWhiteSpace(name) && name == name.Trim()
        && !name.EndsWith('.') && !name.EndsWith(' ') && !name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c));
    internal static TextBlock PathText() => new()
    {
        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
        FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Left,
        FontFamily = new FontFamily("Segoe UI"), Language = "en-US"
    };
    private sealed record FileChoice(MediaFile File) { public override string ToString() => File.CurrentPath; }
}
