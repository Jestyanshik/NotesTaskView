using System.Windows;
using System.Windows.Input;

namespace NotesTaskView.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string hint, string confirmText, string initialValue = "")
    {
        PromptTitle = title;
        Hint = hint;
        ConfirmText = confirmText;
        InitializeComponent();
        DataContext = this;
        ValueTextBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public string PromptTitle { get; }

    public string Hint { get; }

    public string ConfirmText { get; }

    public string Value { get; private set; } = string.Empty;

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        Value = ValueTextBox.Text;
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
            Value = ValueTextBox.Text;
            DialogResult = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
