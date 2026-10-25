namespace PlaybookRunner.Tests;

public class InputsTests
{
    private static readonly Dictionary<string, string> None = [];

    [Fact]
    public void CliArgs_reads_file_flags_and_name_value_pairs()
    {
        var a = CliArgs.Parse(["nwea.yaml", "--headed", "term=Fall 2026-2027", "pass=a=b"]);
        Assert.Equal("nwea.yaml", a.File);
        Assert.True(a.Headed);
        Assert.Equal(200, a.SlowMo);
        Assert.Equal(new Dictionary<string, string> { ["term"] = "Fall 2026-2027", ["pass"] = "a=b" }, a.Vars);
    }

    [Fact]
    public void CliArgs_keep_implies_headed_and_slow_overrides()
    {
        Assert.True(CliArgs.Parse(["x.yml", "--keep"]).Headed);
        Assert.Equal(50, CliArgs.Parse(["x.yml", "--slow=50"]).SlowMo);
        Assert.Null(CliArgs.Parse([]).File);
        Assert.Equal(0, CliArgs.Parse(["x.yml"]).SlowMo);
        Assert.Equal("x.yml", CliArgs.Parse(["pass=secret.yml", "x.yml"]).File);
    }

    [Fact]
    public void Params_apply_defaults_lists_and_CLI_overrides()
    {
        var spec = YamlLoader.Parse("""
            term: { required: true, describe: 'e.g. "Fall 2026-2027"' }
            testedSeasons: [Fall, Spring]
            downloadDir: downloads/nwea
            limit: { default: 3 }
            """);
        var cli = new Dictionary<string, string> { ["term"] = "Fall 2026-2027", ["extra"] = "x" };
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["testedSeasons"] = "Fall, Spring", ["downloadDir"] = "downloads/nwea", ["limit"] = "3",
                ["term"] = "Fall 2026-2027", ["extra"] = "x",
            },
            Params.Resolve(spec, cli));
    }

    [Fact]
    public void Params_read_env_values_and_the_command_line_still_wins()
    {
        var spec = YamlLoader.Parse("term: { required: true, env: NWEA_TERM }");
        var env = new Dictionary<string, string> { ["NWEA_TERM"] = "Fall 2026-2027" };
        Assert.Equal("Fall 2026-2027", Params.Resolve(spec, None, env)["term"]);
        Assert.Equal("Spring 2025-2026", Params.Resolve(spec, new Dictionary<string, string> { ["term"] = "Spring 2025-2026" }, env)["term"]);
        Assert.Contains("term, or NWEA_TERM= in .env", Assert.Throws<PlaybookException>(() => Params.Resolve(spec, None, None)).Message);
    }

    [Fact]
    public void Params_report_missing_required_params_with_their_description()
    {
        var spec = YamlLoader.Parse("term: { required: true, describe: season + year }");
        Assert.Contains("Missing required input(s): term (season + year)",
            Assert.Throws<PlaybookException>(() => Params.Resolve(spec, None)).Message);
        Assert.Empty(Params.Resolve(StepArg.Null, None));
    }

    [Fact]
    public void Secrets_list_and_map_forms()
    {
        Assert.Equal([new SecretSpec("A")], Secrets.NormalizeSpecs(YamlLoader.Parse("[A]")));
        Assert.Equal(
            [new SecretSpec("A", "user"), new SecretSpec("B")],
            Secrets.NormalizeSpecs(YamlLoader.Parse("{ A: { arg: user }, B: null }")));
        Assert.Empty(Secrets.NormalizeSpecs(StepArg.Null));
        Assert.Contains("must be", Assert.Throws<PlaybookException>(() => Secrets.NormalizeSpecs(new StepArg("A"))).Message);
    }

    [Fact]
    public void Secrets_CLI_arg_wins_over_env_and_missing_ones_are_reported()
    {
        var specs = Secrets.NormalizeSpecs(YamlLoader.Parse("{ NWEA_USER: { arg: user }, NWEA_PASS: { arg: pass } }"));
        var r = Secrets.Resolve(specs, new Dictionary<string, string> { ["user"] = "cli@x" }, new Dictionary<string, string> { ["NWEA_USER"] = "env@x" });
        Assert.Equal(new Dictionary<string, string> { ["NWEA_USER"] = "cli@x" }, r.Values);
        Assert.Equal(["NWEA_PASS"], r.Missing);
        Assert.Equal(
            new Dictionary<string, string> { ["term"] = "t" },
            Secrets.WithoutSecretArgs(new Dictionary<string, string> { ["user"] = "u", ["pass"] = "p", ["term"] = "t" }, specs));
    }

    [Fact]
    public void MakeMasker_hides_every_secret_value() =>
        Assert.Equal("pw=*** and ***", Secrets.MakeMasker(["s3cret", ""])("pw=s3cret and s3cret"));

    [Fact]
    public void DotEnv_parses_quotes_comments_and_export()
    {
        var env = DotEnv.Parse("""
            # a comment
            NWEA_USER="me@school.org"
            export NWEA_PASS='p#ss word'
            NWEA_TERM=Fall 2026-2027   # trailing note

            BROKEN LINE
            """);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["NWEA_USER"] = "me@school.org", ["NWEA_PASS"] = "p#ss word", ["NWEA_TERM"] = "Fall 2026-2027",
            },
            env);
    }
}
