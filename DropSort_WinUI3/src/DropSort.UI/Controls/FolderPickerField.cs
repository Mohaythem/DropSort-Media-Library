using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Controls;

/// <summary>A native button that presents a selected folder without accepting typed paths.</summary>
public sealed class FolderPickerField : Button
{
    private readonly TextBlock _label = new()
    {
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(FolderPickerField), new PropertyMetadata(string.Empty, OnTextChanged));
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(FolderPickerField), new PropertyMetadata(string.Empty, OnTextChanged));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string PlaceholderText { get => (string)GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }

    public FolderPickerField()
    {
        Content = _label;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        UpdateLabel();
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((FolderPickerField)sender).UpdateLabel();

    private void UpdateLabel()
    {
        var selected = !string.IsNullOrWhiteSpace(Text);
        var text = selected ? Text : PlaceholderText ?? string.Empty;
        _label.Text = text;
        _label.Opacity = selected ? 1 : 0.6;
        AutomationProperties.SetName(this, text);
        AutomationProperties.SetHelpText(this, PlaceholderText ?? string.Empty);
        ToolTipService.SetToolTip(this, text);
    }
}
