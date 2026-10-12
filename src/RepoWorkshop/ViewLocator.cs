using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RepoWorkshop.ViewModels;

namespace RepoWorkshop;

/// <summary>Finds the view for a view model by name: RepoWorkshop.ViewModels.XViewModel → RepoWorkshop.Views.XView.</summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate {
    /// <summary>Creates the view for <paramref name="param"/>, or a "Not Found" placeholder.</summary>
    public Control? Build(object? param) {
        if (param is null) {
            return null;
        }

        string name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        Type? type = Type.GetType(name);
        if (type is not null) {
            return (Control)Activator.CreateInstance(type)!;
        }
        return new TextBlock { Text = "Not Found: " + name };
    }

    /// <summary>Handles every view model.</summary>
    public bool Match(object? data) => data is ViewModelBase;
}
