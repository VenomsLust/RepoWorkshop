using System.Collections.Specialized;
using Avalonia.Controls;

namespace RepoWorkshop.Views;

/// <summary>The main window: projects on the left, the selected project on the right, log and status at the bottom.</summary>
public partial class MainWindow : Window {
    /// <summary>Builds the window and keeps the output log scrolled to the newest entry.</summary>
    public MainWindow() {
        InitializeComponent();
        if (LogList.Items is INotifyCollectionChanged items) {
            items.CollectionChanged += OnLogItemsChanged;
        }
    }

    /// <summary>Scrolls the log to a newly added entry.</summary>
    private void OnLogItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (e.Action == NotifyCollectionChangedAction.Add && LogList.ItemCount > 0) {
            LogList.ScrollIntoView(LogList.ItemCount - 1);
        }
    }
}
