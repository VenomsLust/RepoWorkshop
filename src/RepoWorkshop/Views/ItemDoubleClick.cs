using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace RepoWorkshop.Views;

/// <summary>
/// Attached property that runs a command when a list item is double-clicked,
/// passing the item's data as the command parameter.
/// </summary>
public static class ItemDoubleClick
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<ListBox, ICommand?>("Command", typeof(ItemDoubleClick));

    static ItemDoubleClick()
    {
        CommandProperty.Changed.AddClassHandler<ListBox>((list, e) =>
        {
            list.DoubleTapped -= OnDoubleTapped;
            if (e.NewValue is not null)
                list.DoubleTapped += OnDoubleTapped;
        });
    }

    public static ICommand? GetCommand(ListBox list) => list.GetValue(CommandProperty);

    public static void SetCommand(ListBox list, ICommand? value) => list.SetValue(CommandProperty, value);

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Ignore double-clicks on empty space or the scrollbar.
        if (sender is not ListBox list || e.Source is not Visual source)
            return;
        var item = source.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        var command = GetCommand(list);
        if (item?.DataContext is { } data && command?.CanExecute(data) == true)
            command.Execute(data);
    }
}
