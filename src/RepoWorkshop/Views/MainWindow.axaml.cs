using System.Collections.Specialized;
using Avalonia.Controls;

namespace RepoWorkshop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Keep the newest log entry in view.
        if (LogList.Items is INotifyCollectionChanged items)
        {
            items.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && LogList.ItemCount > 0)
                    LogList.ScrollIntoView(LogList.ItemCount - 1);
            };
        }
    }
}
