using System;
using System.ComponentModel;
using System.Windows;
using AkteTimer.Services;
using AkteTimer.ViewModels;

namespace AkteTimer.Views;

public partial class DashboardWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private readonly TimeEntryService _timeEntryService;

    public DashboardWindow(TimeEntryService timeEntryService, DatabaseService databaseService)
    {
        _timeEntryService = timeEntryService;
        InitializeComponent();
        _viewModel = new DashboardViewModel(timeEntryService, databaseService);
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        _timeEntryService.StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Refresh();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _timeEntryService.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => _viewModel.Refresh());
            return;
        }

        _viewModel.Refresh();
    }
}
