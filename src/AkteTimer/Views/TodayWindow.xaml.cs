using System.Windows;
using AkteTimer.Services;
using AkteTimer.ViewModels;

namespace AkteTimer.Views;

public partial class TodayWindow : Window
{
    private readonly TodayViewModel _viewModel;
    private readonly TimeEntryService _timeEntryService;

    public TodayWindow(TimeEntryService timeEntryService)
    {
        _timeEntryService = timeEntryService;
        InitializeComponent();
        _viewModel = new TodayViewModel(_timeEntryService);
        DataContext = _viewModel;
        Activated += (_, _) => _viewModel.Refresh();
    }

    private void OnEntryDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid grid || grid.SelectedItem is not TodayEntryViewModel entryViewModel)
        {
            return;
        }

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
            _viewModel.Refresh();
        }
    }
}
