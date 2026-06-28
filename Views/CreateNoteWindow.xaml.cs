using System.Windows;
using System.Windows.Input;

namespace NotesTaskView.Views;

public partial class CreateNoteWindow : Window
{
    public CreateNoteWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => TitleTextBox.Focus();
    }

    public string? NoteTitle { get; private set; }

    private void CreateButton_OnClick(object sender, RoutedEventArgs e)
    {
        NoteTitle = TitleTextBox.Text;
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NoteTitle = TitleTextBox.Text;
            DialogResult = true;
            e.Handled = true;
        }

        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
