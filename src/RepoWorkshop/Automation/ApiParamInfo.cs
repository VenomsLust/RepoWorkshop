namespace RepoWorkshop.Automation;

/// <summary>A parameter of an API method, as listed by <c>help</c>.</summary>
/// <param name="Name">The parameter name, e.g. "repo".</param>
/// <param name="Type">Its type, e.g. "string", "bool", "int" or "string[]".</param>
/// <param name="Required">Whether it must be passed.</param>
/// <param name="Description">What it means.</param>
public sealed record ApiParamInfo(string Name, string Type, bool Required, string Description);
