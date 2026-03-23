using Avalonia.Controls;
using Avalonia.Interactivity;
using KeyVaultExplorer.Models;
using KeyVaultExplorer.ViewModels;
using KeyVaultExplorer.Views.Pages;

namespace KeyVaultExplorer.Views.CustomControls;

public partial class ToolBar : UserControl
{
    private MainViewModel _mainViewModel;

    public ToolBar()
    {
        InitializeComponent();
        Loaded += ToolBar_Loaded;
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ToolBar_Loaded;
        _mainViewModel = Defaults.Locator.GetRequiredService<MainViewModel>();

        // Set DataContext on the ComboBox so XAML bindings resolve to MainViewModel
        var tenantCombo = this.FindControl<ComboBox>("TenantSelector");
        if (tenantCombo is not null)
            tenantCombo.DataContext = _mainViewModel;
    }

    private void SettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Control control = (Control)sender!;
        control.RaiseEvent(new RoutedEventArgs(MainView.NavigateSettingsEvent));
    }

    private void SubscriptionsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Control control = (Control)sender!;
        control.RaiseEvent(new RoutedEventArgs(MainView.NavigateSubscriptionsEvent));
    }

    private void IsPaneToggledButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Control control = (Control)sender!;
        control.RaiseEvent(new RoutedEventArgs(TabViewPage.PaneToggledRoutedEvent));
    }
}
