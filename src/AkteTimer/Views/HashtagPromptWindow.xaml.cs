using System.Windows;
using System.Windows.Input;
using AkteTimer.ViewModels;

namespace AkteTimer.Views;

public partial class HashtagPromptWindow : Window
{
    private readonly IReadOnlyList<string> _tags;
    public string? SelectedHashtag { get; private set; }

    public HashtagPromptWindow(IReadOnlyList<string> tags, string defaultHashtag)
    {
        InitializeComponent();
        _tags = tags;
        DataContext = new HashtagPromptViewModel(tags, defaultHashtag);
        PreviewKeyDown += HandleKeyDown;
    }

    private void HandleTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagChipViewModel chip })
        {
            SelectedHashtag = chip.Tag;
            DialogResult = true;
        }
    }

    private void HandleSkipClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void HandleKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        var index = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D0 or Key.NumPad0 => 7,
            _ => -1
        };

        if (index < 0 || index >= _tags.Count)
        {
            return;
        }

        SelectedHashtag = _tags[index];
        DialogResult = true;
        e.Handled = true;
    }
}
