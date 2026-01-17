using System.Windows;
using AkteTimer.Services;
using AkteTimer.ViewModels;

namespace AkteTimer.Views;

public partial class ReportsWindow : Window
{
    public ReportsWindow(TimeEntryService timeEntryService)
    {
        InitializeComponent();
        DataContext = new ReportsViewModel(timeEntryService);
    }
}
