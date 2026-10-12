using System.Text;
using System.Text.Json.Nodes;

namespace RepoWorkshop.Cli;

/// <summary>Formats the result of the API's <c>help</c> method for reading in a terminal.</summary>
public static class HelpText {
    /// <summary>Formats the method list, or one method's parameters when <paramref name="singleMethod"/> is set.</summary>
    public static string Format(JsonNode? result, string? singleMethod) {
        JsonArray methods = result?["methods"]?.AsArray() ?? [];
        StringBuilder sb = new();

        if (singleMethod is null) {
            sb.AppendLine("Repo Workshop API. Usage: rw <method> [--name value]...");
            sb.AppendLine();
            int width = methods.Max(m => m!["name"]!.GetValue<string>().Length) + 2;
            foreach (JsonNode? m in methods) {
                sb.AppendLine($"  {m!["name"]!.GetValue<string>().PadRight(width)}{m["summary"]!.GetValue<string>()}");
            }
            sb.AppendLine();
            sb.AppendLine("Run \"rw help <method>\" for its parameters. Results are JSON; errors go to stderr with exit code 1.");
            sb.AppendLine("Options: --flag (true) or --flag false; repeat an option for a list (--paths a --paths b);");
            sb.AppendLine("--json '{...}' passes parameters as JSON. \"repo\" defaults to the repository containing the current directory.");
            sb.AppendLine("--message-file <path> and --json-file <path> read the message / parameters from a file (- for stdin),");
            sb.AppendLine("which avoids shell quoting problems with multi-line text.");
            sb.AppendLine("Client options: --no-launch (don't start the app), --timeout <seconds> (default 600), --raw (help as JSON).");
            return sb.ToString();
        }

        foreach (JsonNode? m in methods) {
            string changes = m!["changesState"]?.GetValue<bool>() == true ? "  (changes state)" : "";
            sb.AppendLine($"rw {m["name"]!.GetValue<string>()}{changes}");
            sb.AppendLine($"  {m["summary"]!.GetValue<string>()}");
            JsonArray ps = m["params"]?.AsArray() ?? [];
            if (ps.Count == 0) {
                sb.AppendLine("  (no parameters)");
                continue;
            }
            sb.AppendLine();
            foreach (JsonNode? p in ps) {
                string flag = $"--{p!["name"]!.GetValue<string>()} <{p["type"]!.GetValue<string>()}>";
                string required = p["required"]?.GetValue<bool>() == true ? "(required) " : "";
                sb.AppendLine($"  {flag,-28}{required}{p["description"]!.GetValue<string>()}");
            }
        }
        return sb.ToString();
    }
}
