using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RepoWorkshop.Automation;

/// <summary>An API method: its documentation and handler.</summary>
/// <param name="Name">The method name, e.g. "repo.status".</param>
/// <param name="Summary">One-line description, shown by <c>help</c>.</param>
/// <param name="Params">The parameters it accepts.</param>
/// <param name="ChangesState">Whether it changes anything (such calls are noted in the log).</param>
/// <param name="Handler">Runs the call and returns a JSON-serializable result.</param>
public sealed record ApiMethodInfo(
    string Name,
    string Summary,
    IReadOnlyList<ApiParamInfo> Params,
    bool ChangesState,
    Func<ApiCall, Task<object?>> Handler);
