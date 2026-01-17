using System.Windows;
using AkteTimer.Services;
using AkteTimer.ViewModels;

namespace AkteTimer.Views;

public partial class ReportsWindow : Window
{
    private readonly ReportsViewModel _viewModel;
    private readonly TimeEntryService _timeEntryService;

    public ReportsWindow(TimeEntryService timeEntryService)
    {
        _timeEntryService = timeEntryService;
        InitializeComponent();
        _viewModel = new ReportsViewModel(_timeEntryService);
        DataContext = _viewModel;
    }

    private void OnEntryDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid grid || grid.SelectedItem is not ReportEntryViewModel entryViewModel)
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
            _viewModel.RefreshEntries();
        }
    }
}
