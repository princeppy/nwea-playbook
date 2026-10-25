namespace PlaybookRunner.Tests;

public class TextVarsTests
{
    [Fact]
    public void Normalize_collapses_whitespace_and_nbsp() =>
        Assert.Equal("Spring 2026 - Spring 2027", TextUtil.Normalize(" Spring 2026 -\n Spring 2027  "));

    [Fact]
    public void MatchOptionIndex_prefers_an_exact_match() =>
        Assert.Equal(1, TextUtil.MatchOptionIndex(["Fall 2026-2027 (Most Recent)", "Fall 2026-2027"], "Fall 2026-2027"));

    [Fact]
    public void MatchOptionIndex_accepts_a_bracketed_suffix_such_as_Most_Recent()
    {
        string[] opts = ["Spring 2025-2026", "Fall 2026-2027 (Most Recent)"];
        Assert.Equal(1, TextUtil.MatchOptionIndex(opts, "Fall 2026-2027"));
        Assert.Equal(0, TextUtil.MatchOptionIndex(opts, "Spring 2025-2026"));
    }

    [Fact]
    public void MatchOptionIndex_does_not_match_a_mere_prefix()
    {
        Assert.Equal(-1, TextUtil.MatchOptionIndex(["Fall 2026-20270"], "Fall 2026-2027"));
        Assert.Equal(-1, TextUtil.MatchOptionIndex([], "x"));
    }

    [Fact]
    public void SanitizeForPath_matches_the_existing_file_naming()
    {
        Assert.Equal("Abbasi Nazneen", Vars.SanitizeForPath("Abbasi, Nazneen"));
        Assert.Equal("Math- K-12-", Vars.SanitizeForPath("Math: K/12?"));
    }

    [Fact]
    public void Substitute_fills_vars_and_cleans_only_path_vars()
    {
        var vars = new Dictionary<string, string> { ["term"] = "Fall 2026-2027", ["teacher"] = "Abbasi, Nazneen" };
        Assert.Equal("Fall 2026-2027/Abbasi, Nazneen", Vars.Substitute("${term}/${teacher}", vars));
        Assert.Equal("Fall 2026-2027/Abbasi Nazneen.csv", Vars.Substitute("${term}/${teacher}.csv", vars, new HashSet<string> { "teacher" }));
    }

    [Fact]
    public void Substitute_throws_on_an_unknown_variable() =>
        Assert.Contains("Unknown variable ${nope}",
            Assert.Throws<PlaybookException>(() => Vars.Substitute("${nope}", new Dictionary<string, string>())).Message);

    [Fact]
    public void NthIndexOf_finds_the_nth_occurrence_of_a_name()
    {
        string[] names = ["a", "b", "a"];
        Assert.Equal(0, Engine.NthIndexOf(names, "a", 0));
        Assert.Equal(2, Engine.NthIndexOf(names, "a", 1));
        Assert.Equal(-1, Engine.NthIndexOf(names, "a", 2));
    }

    [Fact]
    public void SelectItems_drops_blanks_skip_names_and_untested_seasons_numbering_duplicates()
    {
        Assert.Equal(
            [new LoopItem("x", 0), new LoopItem("y", 0), new LoopItem("x", 1)],
            Engine.SelectItems(["x", "", "y", "x", "z"], new HashSet<string> { "z" }));
        Assert.Equal(
            [new LoopItem("Fall 2026 - Spring 2027 Report", 0)],
            Engine.SelectItems(["Fall 2026 - Winter 2027 Report", "Fall 2026 - Spring 2027 Report"], new HashSet<string>(), ["Fall", "Spring"]));
    }
}
