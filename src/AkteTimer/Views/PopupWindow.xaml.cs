using System.Windows;
using System.Windows.Input;
using AkteTimer.Services;
using AkteTimer.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace AkteTimer.Views;

public partial class PopupWindow : Window
{
    private readonly TimeEntryService _timeEntryService;
    private bool _allowClose;

    public PopupWindow(TimeEntryService timeEntryService, SettingsService settingsService)
    {
        InitializeComponent();
        _timeEntryService = timeEntryService;
        DataContext = new PopupViewModel(_timeEntryService, settingsService);

        PreviewKeyDown += HandleKeyDown;
        Deactivated += (_, _) => Hide();
        Closing += (_, args) =>
        {
            if (_allowClose)
            {
                return;
            }

            args.Cancel = true;
            Hide();
        };
        if (Application.Current != null)
        {
            Application.Current.ShutdownStarted += (_, _) => _allowClose = true;
        }
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

    public void PrepareForShutdown()
    {
        _allowClose = true;
    }

    private void HandleKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
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

            PromptForHashtagIfMissing();
            _timeEntryService.SwitchMatter(matter);
            vm.ClearInput();
            vm.Refresh();
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (_timeEntryService.IsRunning)
            {
                PromptForHashtagIfMissing();
            }
            _timeEntryService.ToggleStartPause();
            vm.Refresh();
        }
    }

    private void PromptForHashtagIfMissing()
    {
        if (!_timeEntryService.ShouldPromptForHashtag())
        {
            return;
        }

        var runningEntry = _timeEntryService.GetRunningEntry();
        if (runningEntry == null || !string.IsNullOrWhiteSpace(runningEntry.Hashtag))
        {
            return;
        }

        var prompt = new HashtagPromptWindow(TimeEntryService.DefaultHashtags, _timeEntryService.GetDefaultHashtag())
        {
            Owner = this
        };

        var result = prompt.ShowDialog();
        if (result == true && !string.IsNullOrWhiteSpace(prompt.SelectedHashtag))
        {
            _timeEntryService.SetEntryHashtag(runningEntry.Id, prompt.SelectedHashtag);
        }
    }
}
