// Secrets come from .env, or from a command-line argument named in the playbook:
//
//   secrets:
//     NWEA_USER: { arg: user }   # user=... on the command line overrides .env
//     NWEA_PASS: { arg: pass }
//
// The older list form, secrets: [NWEA_USER, NWEA_PASS], still works.

namespace PlaybookRunner;

public sealed record SecretSpec(string Name, string? Arg = null);

public sealed record ResolvedSecrets(IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Missing);

public static class Secrets
{
    public static IReadOnlyList<SecretSpec> NormalizeSpecs(StepArg raw)
    {
        if (!raw.Truthy) return [];
        if (raw.IsList) return raw.Items.Select(i => new SecretSpec(i.Text)).ToList();
        if (raw.IsMap)
        {
            return raw.Entries
                .Select(e => new SecretSpec(e.Key, e.Value["arg"].IsNull ? null : e.Value["arg"].Text))
                .ToList();
        }
        throw new PlaybookException("\"secrets\" must be a list of names or a map of name: { arg }");
    }

    /// <summary>Look each secret up on the command line first, then in the environment.</summary>
    public static ResolvedSecrets Resolve(
        IReadOnlyList<SecretSpec> specs,
        IReadOnlyDictionary<string, string> cli,
        IReadOnlyDictionary<string, string> env)
    {
        var found = specs.Select(s => (s.Name, value: Lookup(s, cli, env))).ToList();
        return new ResolvedSecrets(
            found.Where(f => f.value.Length > 0).ToDictionary(f => f.Name, f => f.value),
            found.Where(f => f.value.Length == 0).Select(f => f.Name).ToList());
    }

    private static string Lookup(SecretSpec s, IReadOnlyDictionary<string, string> cli, IReadOnlyDictionary<string, string> env)
    {
        if (s.Arg is not null && cli.TryGetValue(s.Arg, out var fromCli) && fromCli.Length > 0) return fromCli;
        return env.TryGetValue(s.Name, out var fromEnv) ? fromEnv : "";
    }

    /// <summary>CLI vars without the secret arguments, so secrets never become ${vars}.</summary>
    public static IReadOnlyDictionary<string, string> WithoutSecretArgs(
        IReadOnlyDictionary<string, string> cli,
        IReadOnlyList<SecretSpec> specs)
    {
        var secretArgs = specs.Where(s => s.Arg is not null).Select(s => s.Arg!).ToHashSet();
        return cli.Where(kv => !secretArgs.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>Returns a function that replaces every secret value in a string with ***.</summary>
    public static Func<string, string> MakeMasker(IEnumerable<string> values)
    {
        var secrets = values.Where(v => v.Length > 0).ToList();
        return s => secrets.Aggregate(s, (t, v) => t.Replace(v, "***", StringComparison.Ordinal));
    }
}
