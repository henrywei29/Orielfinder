using Microsoft.UI.Xaml.Controls;
using OrielFolder.Services;

namespace OrielFolder.Views;

public sealed partial class SettingsModeView
{
    private void ProductIconChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _settings == null) return;
        _settings.Set("ProductIcon", ProductIconCombo.SelectedIndex == 1 ? "classic" : "blue");
        foreach (var window in App.Current.GetRegisteredWindows())
            ProductIconService.Apply(window, _settings);
    }
}
