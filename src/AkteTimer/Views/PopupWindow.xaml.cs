using System.Windows;
using System.Windows.Input;
using AkteTimer.Services;
using AkteTimer.ViewModels;

namespace AkteTimer.Views;

public partial class PopupWindow : Window
{
    private readonly TimeEntryService _timeEntryService;

    public PopupWindow(TimeEntryService timeEntryService)
    {
        InitializeComponent();
        _timeEntryService = timeEntryService;
        DataContext = new PopupViewModel(_timeEntryService);

        PreviewKeyDown += HandleKeyDown;
        Deactivated += (_, _) => Hide();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                FocusInput();
            }
        };
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
        Activate();
    }

    public void FocusInput()
    {
        FileRefBox.Focus();
        FileRefBox.SelectAll();
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            return;
        }

        if (DataContext is not PopupViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            vm.MoveSelection(-1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            vm.MoveSelection(1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && (FileRefBox.IsFocused || RecentMattersListBox.IsFocused))
        {
            var fileRef = vm.SelectedRecentMatter ?? vm.FileRefInput;
            fileRef = fileRef.Trim();
            if (string.IsNullOrWhiteSpace(fileRef))
            {
                return;
            }

            if (!_timeEntryService.IsValidFileRef(fileRef))
            {
                vm.UpdateStatus("Ungültiges Aktenzeichen");
                return;
            }

            var matter = _timeEntryService.GetMatterByFileRef(fileRef);
            if (matter == null)
            {
                var result = MessageBox.Show($"Akte {fileRef} existiert nicht. Erstellen?", "Akte anlegen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    FocusInput();
                    return;
                }

                matter = _timeEntryService.CreateMatter(fileRef);
            }

            _timeEntryService.SwitchMatter(matter);
            vm.ClearInput();
            vm.Refresh();
            return;
        }

        if (e.Key == Key.Enter)
        {
            _timeEntryService.ToggleStartPause();
            vm.Refresh();
        }
    }
}
