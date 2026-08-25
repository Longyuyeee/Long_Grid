using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LongGrid.App.Controls;

public sealed partial class ProductPageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(ProductPageHeader),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(
            nameof(Subtitle),
            typeof(string),
            typeof(ProductPageHeader),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionContentProperty =
        DependencyProperty.Register(
            nameof(ActionContent),
            typeof(object),
            typeof(ProductPageHeader),
            new PropertyMetadata(null));

    public ProductPageHeader()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }
}
