using System;
using System.Collections.Generic;

namespace RepoWorkshop.Models;

/// <summary>A named group of repositories that are managed together.</summary>
public sealed class ProjectModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public List<RepoModel> Repos { get; set; } = [];
}
