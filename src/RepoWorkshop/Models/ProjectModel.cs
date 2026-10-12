using System;
using System.Collections.Generic;

namespace RepoWorkshop.Models;

/// <summary>A named group of repositories that are managed together.</summary>
public sealed class ProjectModel {
    /// <summary>Stable identifier, independent of the name.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The project's repositories, in display order.</summary>
    public List<RepoModel> Repos { get; set; } = [];
}
