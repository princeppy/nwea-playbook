// A value read from the playbook YAML: null, string, bool, long, double, a list or a map.
// Step arguments are loose by nature ({ click: { testid: x } }, waitForIdle: 2000, ...),
// so steps read them through this wrapper instead of fixed classes.

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PlaybookRunner;

public sealed class StepArg
{
    public static readonly StepArg Null = new(null);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // log {"css":"[a=\"b\"]"} like JSON.stringify
    };

    public StepArg(object? value) => Value = value;

    /// <summary>The raw value: null, string, bool, long, double, IReadOnlyList or IReadOnlyDictionary.</summary>
    public object? Value { get; }

    public bool IsNull => Value is null;
    public bool IsMap => Value is IReadOnlyDictionary<string, object?>;
    public bool IsList => Value is IReadOnlyList<object?>;
    public bool IsString => Value is string;

    /// <summary>A map entry, or <see cref="Null"/> when this is not a map or has no such key.</summary>
    public StepArg this[string key] =>
        Value is IReadOnlyDictionary<string, object?> m && m.TryGetValue(key, out var v) ? new StepArg(v) : Null;

    public bool Has(string key) => Value is IReadOnlyDictionary<string, object?> m && m.ContainsKey(key);

    /// <summary>List items; empty when this is not a list (so a missing "before:" is no steps).</summary>
    public IReadOnlyList<StepArg> Items =>
        Value is IReadOnlyList<object?> l ? l.Select(v => new StepArg(v)).ToList() : [];

    /// <summary>Map entries in YAML order; empty when this is not a map.</summary>
    public IReadOnlyList<KeyValuePair<string, StepArg>> Entries =>
        Value is IReadOnlyDictionary<string, object?> m
            ? m.Select(kv => KeyValuePair.Create(kv.Key, new StepArg(kv.Value))).ToList()
            : [];

    /// <summary>JavaScript-style truthiness: null, false, 0 and "" are false.</summary>
    public bool Truthy => Value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        long n => n != 0,
        double d => d != 0 && !double.IsNaN(d),
        _ => true,
    };

    /// <summary>The value as text: "90000", "true", the string itself; JSON for lists and maps.</summary>
    public string Text => Value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        long n => n.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => Describe(),
    };

    /// <summary>A number, or null. Numeric strings count ("5000").</summary>
    public double? Number => Value switch
    {
        long n => n,
        double d => d,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    /// <summary>The value as an int, or <paramref name="fallback"/> when it is missing or not a number.</summary>
    public int IntOr(int fallback) => Number is double d ? (int)d : fallback;

    /// <summary>For logs: JSON for lists and maps, the text otherwise.</summary>
    public string Describe() => IsMap || IsList ? JsonSerializer.Serialize(Value, JsonOptions) : Text;

    public override string ToString() => Describe();
}
