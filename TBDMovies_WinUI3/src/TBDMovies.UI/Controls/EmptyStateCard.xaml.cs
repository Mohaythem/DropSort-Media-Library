using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TBDMovies.UI.Controls;

/// <summary>
/// The shared empty-result surface used by Library, My Lists and Check Library so that every
/// "nothing here" state has identical geometry and typography.
/// </summary>
public sealed partial class EmptyStateCard : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(EmptyStateCard), new PropertyMetadata(""));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyStateCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(EmptyStateCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(EmptyStateCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsActionVisibleProperty = DependencyProperty.Register(
        nameof(IsActionVisible), typeof(bool), typeof(EmptyStateCard), new PropertyMetadata(false));

    public static readonly DependencyProperty IsFramelessProperty = DependencyProperty.Register(
        nameof(IsFrameless), typeof(bool), typeof(EmptyStateCard), new PropertyMetadata(false, OnIsFramelessChanged));

    public EmptyStateCard()
    {
        this.InitializeComponent();
        UpdatePresentation();
    }

    public event RoutedEventHandler? ActionClick;

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public bool IsActionVisible
    {
        get => (bool)GetValue(IsActionVisibleProperty);
        set => SetValue(IsActionVisibleProperty, value);
    }

    public bool IsFrameless
    {
        get => (bool)GetValue(IsFramelessProperty);
        set => SetValue(IsFramelessProperty, value);
    }

    private static void OnIsFramelessChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((EmptyStateCard)dependencyObject).UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        if (FramedState is null || FramelessState is null)
        {
            return;
        }

        var frameless = IsFrameless;
        FramedState.Visibility = frameless ? Visibility.Collapsed : Visibility.Visible;
        FramelessState.Visibility = frameless ? Visibility.Visible : Visibility.Collapsed;
        HorizontalAlignment = frameless
            ? Microsoft.UI.Xaml.HorizontalAlignment.Stretch
            : Microsoft.UI.Xaml.HorizontalAlignment.Left;
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e) => ActionClick?.Invoke(this, e);
}
