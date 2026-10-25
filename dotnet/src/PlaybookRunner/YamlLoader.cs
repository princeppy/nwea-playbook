// Reads a playbook into StepArg values. Plain (unquoted) scalars are typed as in the
// YAML 1.2 core schema, so `timeout: 90000` is a number and `exact: true` a bool,
// while quoted scalars always stay strings. Anchors and aliases (*openPanel) resolve
// to the anchored value.

using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PlaybookRunner;

public static partial class YamlLoader
{
    [GeneratedRegex(@"^[-+]?[0-9]+$")]
    private static partial Regex IntPattern();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    private static partial Regex FloatPattern();

    /// <summary>Parse YAML text; an empty document gives <see cref="StepArg.Null"/>.</summary>
    public static StepArg Parse(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException e)
        {
            throw new PlaybookException($"The playbook is not valid YAML: {e.Message}");
        }
        return stream.Documents.Count == 0 ? StepArg.Null : new StepArg(Convert(stream.Documents[0].RootNode));
    }

    private static object? Convert(YamlNode node) => node switch
    {
        YamlScalarNode s => Scalar(s),
        YamlSequenceNode seq => seq.Children.Select(Convert).ToList(),
        YamlMappingNode map => ToMap(map),
        _ => null,
    };

    private static OrderedDictionary<string, object?> ToMap(YamlMappingNode map)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in map.Children)
        {
            result[key is YamlScalarNode k ? k.Value ?? "" : key.ToString()] = Convert(value);
        }
        return result;
    }

    private static object? Scalar(YamlScalarNode s)
    {
        var text = s.Value ?? "";
        if (s.Style != ScalarStyle.Plain) return text;
        return text switch
        {
            "" or "~" or "null" or "Null" or "NULL" => null,
            "true" or "True" or "TRUE" => true,
            "false" or "False" or "FALSE" => false,
            _ when IntPattern().IsMatch(text) && long.TryParse(text, CultureInfo.InvariantCulture, out var n) => n,
            _ when FloatPattern().IsMatch(text) => double.Parse(text, CultureInfo.InvariantCulture),
            _ => text,
        };
    }
}

/// <summary>A problem with the playbook or the inputs, reported to the user as is.</summary>
public sealed class PlaybookException(string message) : Exception(message);
