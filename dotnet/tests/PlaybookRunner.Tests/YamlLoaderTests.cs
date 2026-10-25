namespace PlaybookRunner.Tests;

public class YamlLoaderTests
{
    [Fact]
    public void Plain_scalars_are_typed_and_quoted_ones_stay_strings()
    {
        var y = YamlLoader.Parse("""
            n: 90000
            f: 1.5
            t: true
            nothing:
            s: "90000"
            regex: '\d+ out of \d+'
            """);
        Assert.Equal(90000L, y["n"].Value);
        Assert.Equal(1.5, y["f"].Value);
        Assert.Equal(true, y["t"].Value);
        Assert.True(y.Has("nothing"));
        Assert.True(y["nothing"].IsNull);
        Assert.Equal("90000", y["s"].Value);
        Assert.Equal(@"\d+ out of \d+", y["regex"].Text);
    }

    [Fact]
    public void Anchors_and_aliases_resolve_to_the_anchored_value()
    {
        var y = YamlLoader.Parse("""
            locators:
              cancel: &cancel { testid: cancel-button }
            steps:
              - click: *cancel
            """);
        Assert.Equal("cancel-button", y["steps"].Items[0]["click"]["testid"].Text);
    }

    [Fact]
    public void Describe_writes_JSON_like_the_Node_runner()
    {
        var y = YamlLoader.Parse("""waitFor: { css: '[data-test-id="x"]', timeout: 90000, exact: true }""");
        Assert.Equal("""{"css":"[data-test-id=\"x\"]","timeout":90000,"exact":true}""", y["waitFor"].Describe());
        Assert.Equal("", StepArg.Null.Describe());
    }

    [Fact]
    public void The_shared_playbook_loads()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "../../../../../../nwea-class-profile.yaml");
        var pb = YamlLoader.Parse(File.ReadAllText(path));
        Assert.Equal("nwea-asg-and-class-profile", pb["name"].Text);
        Assert.True(pb["steps"].Items.Count > 10);
        Assert.Equal("Fall, Spring", Params.Resolve(pb["params"], new Dictionary<string, string> { ["term"] = "x" })["testedSeasons"]);
    }

    [Fact]
    public void Invalid_YAML_gives_a_readable_error() =>
        Assert.Contains("not valid YAML", Assert.Throws<PlaybookException>(() => YamlLoader.Parse("a: [b")).Message);
}
