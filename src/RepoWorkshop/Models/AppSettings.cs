using System;
using System.Collections.Generic;

namespace RepoWorkshop.Models;

public sealed class AppSettings
{
    /// <summary>Explicit path to WinMergeU.exe; auto-detected when empty.</summary>
    public string? WinMergePath { get; set; }

    /// <summary>When comparing branches, diff from their merge base rather than tip-to-tip.</summary>
    public bool CompareFromMergeBase { get; set; } = true;

    /// <summary>"Dark", "Light" or "System".</summary>
    public string Theme { get; set; } = "Dark";

    public Guid? LastProjectId { get; set; }

    public List<ProjectModel> Projects { get; set; } = [];
}
