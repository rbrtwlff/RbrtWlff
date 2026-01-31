using System;
using System.Windows;
using AkteTimer.Services;
using AkteTimer.ViewModels;

namespace AkteTimer.Views;

public partial class BillingWizardWindow : Window
{
    private readonly BillingWizardViewModel? _viewModel;

    public BillingWizardWindow(long batchId, DatabaseService databaseService)
    {
        InitializeComponent();
        try
        {
            _viewModel = new BillingWizardViewModel(databaseService, batchId);
            DataContext = _viewModel;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Der Abrechnungsassistent konnte nicht geladen werden: {ex.Message}", "Abrechnung", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
