using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RepoWorkshop.Views;

/// <summary>A small modal dialog for messages, confirmations and single-line text prompts.</summary>
public sealed class MessageDialog : Window {
    /// <summary>Width of the dialog.</summary>
    private const double DialogWidth = 460;

    /// <summary>Minimum width of each button.</summary>
    private const double ButtonWidth = 80;

    /// <summary>The text box of a prompt; null for a message or confirmation.</summary>
    private readonly TextBox? _input;

    /// <summary>Builds the dialog; close it with true (OK) or false (Cancel/Escape).</summary>
    /// <param name="title">Window title.</param>
    /// <param name="message">The message shown above the buttons.</param>
    /// <param name="input">Initial text for a prompt; pass null for a message or confirmation without input.</param>
    /// <param name="okText">Caption of the default button.</param>
    /// <param name="cancelText">Caption of the cancel button; pass null for a message box with only an OK button.</param>
    public MessageDialog(string title, string message, string? input, string okText, string? cancelText) {
        Title = title;
        Width = DialogWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        StackPanel panel = new() { Margin = new Thickness(20), Spacing = 14 };
        panel.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

        if (input is not null) {
            _input = new TextBox { Text = input };
            _input.AttachedToVisualTree += OnInputAttached;
            panel.Children.Add(_input);
        }

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        Button ok = new() { Content = okText, IsDefault = true, MinWidth = ButtonWidth, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => Close(true);
        buttons.Children.Add(ok);
        if (cancelText is not null) {
            Button cancel = new() { Content = cancelText, IsCancel = true, MinWidth = ButtonWidth, HorizontalContentAlignment = HorizontalAlignment.Center };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }
        panel.Children.Add(buttons);

        Content = panel;
        KeyDown += OnDialogKeyDown;
    }

    /// <summary>The text entered in a prompt.</summary>
    public string InputText => _input?.Text ?? "";

    /// <summary>Focuses the prompt and selects its text once shown.</summary>
    private void OnInputAttached(object? sender, VisualTreeAttachmentEventArgs e) {
        _input!.Focus();
        _input.SelectAll();
    }

    /// <summary>Escape cancels.</summary>
    private void OnDialogKeyDown(object? sender, KeyEventArgs e) {
        if (e.Key == Key.Escape) {
            Close(false);
        }
    }
}
