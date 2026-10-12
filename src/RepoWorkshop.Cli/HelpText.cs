using System.Text;
using System.Text.Json.Nodes;

namespace RepoWorkshop.Cli;

/// <summary>Formats the result of the API's <c>help</c> method for reading in a terminal.</summary>
public static class HelpText
{
    public static string Format(JsonNode? result, string? singleMethod)
    {
        var methods = result?["methods"]?.AsArray() ?? [];
        var sb = new StringBuilder();

        if (singleMethod is null)
        {
            sb.AppendLine("Repo Workshop API. Usage: rw <method> [--name value]...");
            sb.AppendLine();
            var width = methods.Max(m => m!["name"]!.GetValue<string>().Length) + 2;
            foreach (var m in methods)
                sb.AppendLine($"  {m!["name"]!.GetValue<string>().PadRight(width)}{m["summary"]!.GetValue<string>()}");
            sb.AppendLine();
            sb.AppendLine("Run \"rw help <method>\" for its parameters. Results are JSON; errors go to stderr with exit code 1.");
            sb.AppendLine("Options: --flag (true) or --flag false; repeat an option for a list (--paths a --paths b);");
            sb.AppendLine("--json '{...}' passes parameters as JSON. \"repo\" defaults to the repository containing the current directory.");
            sb.AppendLine("Client options: --no-launch (don't start the app), --timeout <seconds> (default 600), --raw (help as JSON).");
            return sb.ToString();
        }

        foreach (var m in methods)
        {
            var changes = m!["changesState"]?.GetValue<bool>() == true ? "  (changes state)" : "";
            sb.AppendLine($"rw {m["name"]!.GetValue<string>()}{changes}");
            sb.AppendLine($"  {m["summary"]!.GetValue<string>()}");
            var ps = m["params"]?.AsArray() ?? [];
            if (ps.Count == 0)
            {
                sb.AppendLine("  (no parameters)");
                continue;
            }
            sb.AppendLine();
            foreach (var p in ps)
            {
                var flag = $"--{p!["name"]!.GetValue<string>()} <{p["type"]!.GetValue<string>()}>";
                var required = p["required"]?.GetValue<bool>() == true ? "(required) " : "";
                sb.AppendLine($"  {flag,-28}{required}{p["description"]!.GetValue<string>()}");
            }
        }
        return sb.ToString();
    }
}
