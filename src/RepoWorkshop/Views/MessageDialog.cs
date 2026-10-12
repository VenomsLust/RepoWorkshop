using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace RepoWorkshop.Views;

/// <summary>A small modal dialog for messages, confirmations and single-line text prompts.</summary>
public sealed class MessageDialog : Window
{
    private readonly TextBox? _input;

    /// <param name="input">Initial text for a prompt; pass null for a message or confirmation without input.</param>
    /// <param name="cancelText">Pass null for a message box with only an OK button.</param>
    public MessageDialog(string title, string message, string? input, string okText, string? cancelText)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        panel.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        if (input is not null)
        {
            _input = new TextBox { Text = input };
            _input.AttachedToVisualTree += (_, _) =>
            {
                _input.Focus();
                _input.SelectAll();
            };
            panel.Children.Add(_input);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = okText, IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => Close(true);
        buttons.Children.Add(ok);
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }
        panel.Children.Add(buttons);

        Content = panel;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(false);
        };
    }

    public string InputText => _input?.Text ?? "";
}
