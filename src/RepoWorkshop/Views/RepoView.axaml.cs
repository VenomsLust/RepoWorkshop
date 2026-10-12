using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using RepoWorkshop.ViewModels;

namespace RepoWorkshop.Views;

public partial class RepoView : UserControl
{
    public RepoView()
    {
        InitializeComponent();
        ChangesList.DoubleTapped += OnChangeDoubleTapped;
    }

    private void OnChangeDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Only react to double-clicks on an item, not on empty space or the scrollbar.
        if (e.Source is not Avalonia.Visual source || source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
            return;
        if (DataContext is RepoViewModel vm && vm.DiffChangeCommand.CanExecute(null))
            vm.DiffChangeCommand.Execute(null);
    }
}
