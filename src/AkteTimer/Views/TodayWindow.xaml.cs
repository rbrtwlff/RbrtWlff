using System.Windows;
using AkteTimer.Services;
using AkteTimer.ViewModels;

namespace AkteTimer.Views;

public partial class TodayWindow : Window
{
    private readonly TodayViewModel _viewModel;

    public TodayWindow(TimeEntryService timeEntryService)
    {
        InitializeComponent();
        _viewModel = new TodayViewModel(timeEntryService);
        DataContext = _viewModel;
        Activated += (_, _) => _viewModel.Refresh();
    }
}
