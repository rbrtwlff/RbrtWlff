using System;
using System.ComponentModel;
using System.Windows;
using AkteTimer.Services;
using AkteTimer.ViewModels;

namespace AkteTimer.Views;

public partial class ReportsWindow : Window
{
    private readonly ReportsViewModel _viewModel;
    private readonly TimeEntryService _timeEntryService;
    private readonly SettingsService _settingsService;

    public ReportsWindow(TimeEntryService timeEntryService, SettingsService settingsService)
    {
        _timeEntryService = timeEntryService;
        _settingsService = settingsService;
        InitializeComponent();
        _viewModel = new ReportsViewModel(_timeEntryService);
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        _timeEntryService.StateChanged += OnStateChanged;
    }

    private void OnEntryDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid grid)
        {
            return;
        }

        ReportEntryViewModel? entryViewModel = grid.SelectedItem as ReportEntryViewModel;
        if (entryViewModel == null)
        {
            var row = System.Windows.Controls.ItemsControl.ContainerFromElement(
                grid,
                e.OriginalSource as DependencyObject) as System.Windows.Controls.DataGridRow;
            entryViewModel = row?.Item as ReportEntryViewModel;
        }

        if (entryViewModel == null)
        {
            return;
        }

        try
        {
            var viewModel = new EditTimeEntryViewModel(
                entryViewModel.Entry,
                _timeEntryService.GetAllMatters(),
                TimeEntryService.DefaultHashtags);

            var dialog = new EditTimeEntryWindow(_timeEntryService, viewModel)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                _viewModel.RefreshEntries();
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Eintrag konnte nicht geöffnet werden: {ex.Message}",
                "Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var placement = _settingsService.GetReportsWindowPlacement();
        if (placement == null)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        WindowState = placement.State == WindowState.Minimized ? WindowState.Normal : placement.State;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _timeEntryService.StateChanged -= OnStateChanged;
        var state = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        var bounds = state == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var placement = new ReportsWindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, state);
        _settingsService.SetReportsWindowPlacement(placement);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => _viewModel.RefreshEntries());
            return;
        }

        _viewModel.RefreshEntries();
    }
}
