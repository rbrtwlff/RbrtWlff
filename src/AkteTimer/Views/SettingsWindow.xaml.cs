using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AkteTimer.Services;
using AkteTimer.ViewModels;

namespace AkteTimer.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsService settingsService, HotkeyService hotkeyService)
    {
        InitializeComponent();
        var viewModel = new SettingsViewModel(settingsService, hotkeyService);
        viewModel.RequestClose += (_, _) => Hide();
        DataContext = viewModel;
        PreviewKeyDown += (_, args) =>
        {
            if (DataContext is not SettingsViewModel vm || !vm.IsRecording)
            {
                return;
            }

            var key = args.Key == Key.System ? args.SystemKey : args.Key;
            vm.CaptureHotkey(key, Keyboard.Modifiers);
            args.Handled = true;
        };
        Closing += HandleClosing;
    }

    private void HandleClosing(object? sender, CancelEventArgs e)
    {
        if (Application.Current?.Dispatcher.HasShutdownStarted == true)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
