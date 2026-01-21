using System.Windows;
using System.Windows.Input;

namespace AkteTimer.Views;

public partial class NotePromptWindow : Window
{
    public NotePromptWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NoteBox.Focus();
            NoteBox.SelectAll();
        };
        PreviewKeyDown += HandleKeyDown;
    }

    public string NoteText => NoteBox.Text;

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        DialogResult = !string.IsNullOrWhiteSpace(NoteBox.Text);
        e.Handled = true;
    }
}
