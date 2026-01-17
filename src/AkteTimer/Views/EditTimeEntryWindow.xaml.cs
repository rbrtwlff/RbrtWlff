using System.Windows;
using AkteTimer.Services;
using AkteTimer.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace AkteTimer.Views;

public partial class EditTimeEntryWindow : Window
{
    private readonly TimeEntryService _timeEntryService;
    private readonly EditTimeEntryViewModel _viewModel;

    public EditTimeEntryWindow(TimeEntryService timeEntryService, EditTimeEntryViewModel viewModel)
    {
        _timeEntryService = timeEntryService;
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryGetStartEndLocal(out var startLocal, out var endLocal, out var error))
        {
            MessageBox.Show(error ?? "Ungültige Zeiten.", "Validierung", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (endLocal < startLocal)
        {
            MessageBox.Show("Ende darf nicht vor Start liegen.", "Validierung", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.MatterFileRef))
        {
            MessageBox.Show("Bitte eine Akte angeben.", "Validierung", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _timeEntryService.UpdateTimeEntry(
                _viewModel.EntryId,
                _viewModel.MatterFileRef.Trim(),
                startLocal,
                endLocal,
                _viewModel.Hashtag?.Trim(),
                _viewModel.Note?.Trim());
            DialogResult = true;
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Validierung", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSplit(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryGetStartEndLocal(out var startLocal, out var endLocal, out var error))
        {
            MessageBox.Show(error ?? "Ungültige Zeiten.", "Validierung", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (endLocal <= startLocal)
        {
            MessageBox.Show("Ende muss nach dem Start liegen.", "Validierung", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_viewModel.TryGetSplitLocal(out var splitLocal, out error))
        {
            MessageBox.Show(error ?? "Ungültige Split-Zeit.", "Validierung", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (splitLocal <= startLocal || splitLocal >= endLocal)
        {
            MessageBox.Show("Split-Zeit muss zwischen Start und Ende liegen.", "Validierung", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _timeEntryService.SplitTimeEntry(_viewModel.EntryId, splitLocal);
            DialogResult = true;
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Validierung", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
