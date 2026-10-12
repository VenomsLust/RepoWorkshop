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
public static class ItemDoubleClick {
    /// <summary>The command to run on double-click.</summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<ListBox, ICommand?>("Command", typeof(ItemDoubleClick));

    /// <summary>Hooks list boxes up when the property is set.</summary>
    static ItemDoubleClick() {
        CommandProperty.Changed.AddClassHandler<ListBox>(OnCommandChanged);
    }

    /// <summary>Gets the command attached to <paramref name="list"/>.</summary>
    public static ICommand? GetCommand(ListBox list) => list.GetValue(CommandProperty);

    /// <summary>Attaches a command to <paramref name="list"/>.</summary>
    public static void SetCommand(ListBox list, ICommand? value) => list.SetValue(CommandProperty, value);

    /// <summary>Subscribes to double-taps while a command is attached.</summary>
    private static void OnCommandChanged(ListBox list, AvaloniaPropertyChangedEventArgs e) {
        list.DoubleTapped -= OnDoubleTapped;
        if (e.NewValue is not null) {
            list.DoubleTapped += OnDoubleTapped;
        }
    }

    /// <summary>Runs the command for the double-clicked item, ignoring empty space and the scrollbar.</summary>
    private static void OnDoubleTapped(object? sender, TappedEventArgs e) {
        if (sender is not ListBox list || e.Source is not Visual source) {
            return;
        }

        ListBoxItem? item = source.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        ICommand? command = GetCommand(list);
        if (item?.DataContext is { } data && command?.CanExecute(data) == true) {
            command.Execute(data);
        }
    }
}
