using System.Windows;

namespace AkteTimer.Views;

public partial class BillingWizardWindow : Window
{
    public BillingWizardWindow(long batchId)
    {
        InitializeComponent();
        BatchText.Text = $"Abrechnungsbatch #{batchId}";
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
