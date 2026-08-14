using System.Windows;
using System.Windows.Controls;
using NeFs.AclAuditor.ViewModels;

namespace NeFs.AclAuditor;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void TreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is FolderNodeViewModel node)
            vm.SelectedNode = node;
    }
}
