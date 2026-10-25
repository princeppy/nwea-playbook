// Start-up: reading the playbook, .env and inputs before a browser opens; the run log.

namespace PlaybookRunner.Tests;

public sealed class SetupTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("playbook-setup-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    private const string Playbook = """
        name: test
        params:
          term: { required: true, env: PLAYBOOK_TEST_TERM }
          dir: out
        secrets:
          PLAYBOOK_TEST_USER: { arg: user }
        steps:
          - sleep: 1
        """;

    [Fact]
    public void Load_reads_params_and_secrets_from_env_file_and_command_line()
    {
        var env = Write(".env", "PLAYBOOK_TEST_USER=\"me@school.org\"\nPLAYBOOK_TEST_TERM=\"Fall 2026-2027\"\n");
        var s = Program.Load(Write("pb.yaml", Playbook), new Dictionary<string, string>(), env);
        Assert.Equal("Fall 2026-2027", s.Vars["term"]);
        Assert.Equal("out", s.Vars["dir"]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", s.Vars["date"]);
        Assert.Equal("me@school.org", s.Secrets["PLAYBOOK_TEST_USER"]);

        var cli = new Dictionary<string, string> { ["user"] = "cli@school.org", ["term"] = "Spring 2025-2026" };
        var fromCli = Program.Load(Write("pb.yaml", Playbook), cli, env);
        Assert.Equal("cli@school.org", fromCli.Secrets["PLAYBOOK_TEST_USER"]);
        Assert.Equal("Spring 2025-2026", fromCli.Vars["term"]);
        Assert.False(fromCli.Vars.ContainsKey("user"), "secret arguments never become ${vars}");
    }

    [Fact]
    public void Load_fails_fast_on_a_missing_secret_or_steps()
    {
        var noEnv = Path.Combine(_dir, "missing.env");
        var e = Assert.Throws<PlaybookException>(() =>
            Program.Load(Write("pb.yaml", Playbook), new Dictionary<string, string> { ["term"] = "t" }, noEnv));
        Assert.StartsWith("Missing secret(s): PLAYBOOK_TEST_USER.", e.Message);
        var noSteps = Assert.Throws<PlaybookException>(() =>
            Program.Load(Write("empty.yaml", "name: x\n"), new Dictionary<string, string>(), noEnv));
        Assert.EndsWith("has no \"steps:\" list", noSteps.Message);
    }

    [Fact]
    public void DotEnv_Load_lets_the_real_environment_win()
    {
        Environment.SetEnvironmentVariable("PLAYBOOK_TEST_REAL", "real");
        try
        {
            var env = DotEnv.Load(Write(".env", "PLAYBOOK_TEST_REAL=file\nPLAYBOOK_TEST_FILE_ONLY=file\n"));
            Assert.Equal("real", env["PLAYBOOK_TEST_REAL"]);
            Assert.Equal("file", env["PLAYBOOK_TEST_FILE_ONLY"]);
            Assert.False(DotEnv.Load(Path.Combine(_dir, "none.env")).ContainsKey("PLAYBOOK_TEST_FILE_ONLY"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLAYBOOK_TEST_REAL", null);
        }
    }

    [Fact]
    public void RunLog_masks_secrets_indents_by_depth_and_appends_to_the_file()
    {
        var file = Path.Combine(_dir, "run.log");
        var log = new RunLog(file, Secrets.MakeMasker(["s3cret"]));
        log.Info("user s3cret", depth: 2);
        log.Error("FAILED: s3cret");
        Assert.Equal("    user ***\nFAILED: ***\n", File.ReadAllText(file));
        Assert.Equal(file, log.File);
    }

    [Fact]
    public void ErrorLine_keeps_only_the_first_line() =>
        Assert.Equal("Timeout 3000ms exceeded.", RunLog.ErrorLine(new InvalidOperationException("Timeout 3000ms exceeded.\r\nCall log:\n- x")));
}
