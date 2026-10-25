// Playbook params. Each value comes from, in order: name=value on the command line,
// the environment variable named by `env:` (e.g. from .env), then the YAML default.
//
//   params:
//     term: { required: true, env: NWEA_TERM, describe: 'e.g. "Fall 2026-2027"' }
//     testedSeasons: [Fall, Spring]      # a list becomes "Fall, Spring"
//     downloadDir: downloads/nwea        # a plain default

namespace PlaybookRunner;

public static class Params
{
    private static readonly string[] SpecKeys = ["required", "default", "describe", "env"];

    private static string ToText(StepArg v) =>
        v.IsList ? string.Join(", ", v.Items.Select(i => i.Text)) : v.Text;

    private static bool IsSpec(StepArg v) => v.IsMap && SpecKeys.Any(v.Has);

    /// <summary>Merge YAML params, environment and CLI values into string vars. Missing required params throw.</summary>
    public static IReadOnlyDictionary<string, string> Resolve(
        StepArg spec,
        IReadOnlyDictionary<string, string> cli,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var environment = env ?? new Dictionary<string, string>();
        var resolved = spec.Entries
            .Where(e => !cli.ContainsKey(e.Key))
            .Select(e => (name: e.Key, value: ResolveOne(e.Value, environment), raw: e.Value))
            .ToList();
        var missing = resolved
            .Where(r => r.value is null && IsSpec(r.raw) && r.raw["required"].Truthy)
            .Select(r => MissingText(r.name, r.raw))
            .ToList();
        if (missing.Count > 0)
        {
            throw new PlaybookException(
                $"Missing required input(s): {string.Join("; ", missing)}. Pass them as name=\"value\".");
        }
        var fromYaml = resolved
            .Where(r => r.value is not null)
            .Select(r => KeyValuePair.Create(r.name, r.value!));
        return new Dictionary<string, string>(fromYaml.Concat(cli));
    }

    private static string? ResolveOne(StepArg raw, IReadOnlyDictionary<string, string> env)
    {
        if (!IsSpec(raw)) return raw.IsNull ? null : ToText(raw);
        var envName = raw["env"].Text;
        if (envName.Length > 0 && env.TryGetValue(envName, out var fromEnv) && fromEnv.Length > 0) return fromEnv;
        return raw["default"].IsNull ? null : ToText(raw["default"]);
    }

    private static string MissingText(string name, StepArg raw)
    {
        var envName = raw["env"].Text;
        var where = envName.Length > 0 ? $", or {envName}= in .env" : "";
        var describe = raw["describe"].Text;
        return describe.Length > 0 ? $"{name} ({describe}{where})" : $"{name}{where}";
    }
}
