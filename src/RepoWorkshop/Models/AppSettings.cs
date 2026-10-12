using System;
using System.Collections.Generic;

namespace RepoWorkshop.Models;

/// <summary>Everything Repo Workshop saves between sessions.</summary>
public sealed class AppSettings {
    /// <summary>Explicit path to WinMergeU.exe; auto-detected when empty.</summary>
    public string? WinMergePath { get; set; }

    /// <summary>When comparing branches, diff from their merge base rather than tip-to-tip.</summary>
    public bool CompareFromMergeBase { get; set; } = true;

    /// <summary>"Dark", "Light" or "System".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Whether the named-pipe API (used by the rw command and LLM agents) is running.</summary>
    public bool ApiEnabled { get; set; } = true;

    /// <summary>The project selected when the app last closed.</summary>
    public Guid? LastProjectId { get; set; }

    /// <summary>All projects, in display order.</summary>
    public List<ProjectModel> Projects { get; set; } = [];
}
