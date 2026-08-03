using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LongGrid.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new SizeInt32(1180, 760));

        ShellNavigation.SelectedItem = ShellNavigation.MenuItems[0];
    }

    private void ShellNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer?.Tag as string) ?? "overview";

        OverviewPanel.Visibility = tag == "overview" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = tag == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        SafetyPanel.Visibility = tag == "safety" ? Visibility.Visible : Visibility.Collapsed;
    }
}
