using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using NotesTaskView.Models;

namespace NotesTaskView.Views;

public partial class MoveToFolderWindow : Window
{
    public MoveToFolderWindow(IEnumerable<FolderItem> folders)
    {
        Folders = new ObservableCollection<FolderItem>(folders.Select(folder => new FolderItem
        {
            Name = folder.Name,
            FullPath = folder.FullPath,
            RelativePath = string.IsNullOrWhiteSpace(folder.RelativePath) ? "В корень" : folder.RelativePath,
            LastModified = folder.LastModified,
            ItemCount = folder.ItemCount
        }));

        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            FoldersListBox.SelectedIndex = 0;
            FoldersListBox.Focus();
        };
    }

    public ObservableCollection<FolderItem> Folders { get; }

    public FolderItem? SelectedFolder { get; private set; }

    private void MoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void FoldersListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Confirm();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
        }
    }

    private void Confirm()
    {
        if (FoldersListBox.SelectedItem is not FolderItem folder)
        {
            return;
        }

        SelectedFolder = folder;
        DialogResult = true;
    }
}
