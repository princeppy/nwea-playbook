namespace PlaybookRunner.Tests;

public class TermsTests
{
    private static readonly string[] FallSpring = ["Fall", "Spring"];

    [Fact]
    public void ParseSeasons_accepts_comma_or_space_separated_seasons()
    {
        Assert.Equal(["Fall", "Spring"], Terms.ParseSeasons("Fall, Spring"));
        Assert.Equal(["Fall", "Winter", "Spring", "Summer"], Terms.ParseSeasons("Fall Winter Spring Summer"));
        Assert.Equal(["Fall"], Terms.ParseSeasons("Fall, Fall"));
    }

    [Fact]
    public void ParseSeasons_rejects_unknown_or_empty_input()
    {
        Assert.Contains("Unknown season", Assert.Throws<PlaybookException>(() => Terms.ParseSeasons("Autumn")).Message);
        Assert.Contains("empty", Assert.Throws<PlaybookException>(() => Terms.ParseSeasons(" ")).Message);
    }

    [Fact]
    public void ParseTerm_accepts_NWEA_term_labels_for_tested_seasons()
    {
        Assert.Equal(new Term("Fall 2026-2027", "Fall", 2026, 2027), Terms.ParseTerm("Fall 2026-2027", FallSpring));
        Assert.Equal("Spring", Terms.ParseTerm(" Spring 2025-2026 ", FallSpring).Season);
    }

    [Theory]
    [InlineData("Fall 2026")]
    [InlineData("fall 2026-2027")]
    [InlineData("Fall 2026-2027 (Most Recent)")]
    public void ParseTerm_rejects_malformed_labels(string label) =>
        Assert.Contains("not valid", Assert.Throws<PlaybookException>(() => Terms.ParseTerm(label, FallSpring)).Message);

    [Fact]
    public void ParseTerm_rejects_non_consecutive_years() =>
        Assert.Contains("consecutive", Assert.Throws<PlaybookException>(() => Terms.ParseTerm("Fall 2026-2028", FallSpring)).Message);

    [Fact]
    public void ParseTerm_rejects_seasons_the_school_does_not_test()
    {
        var e = Assert.Throws<PlaybookException>(() => Terms.ParseTerm("Winter 2026-2027", FallSpring));
        Assert.Contains("only tests in Fall and Spring", e.Message);
        Assert.Equal("Winter", Terms.ParseTerm("Winter 2026-2027", ["Fall", "Winter", "Spring"]).Season);
    }

    [Fact]
    public void SeasonsIn_lists_every_season_named_in_a_period_label()
    {
        Assert.Equal(["Fall", "Winter"], Terms.SeasonsIn("Fall 2026 - Winter 2027 Growth Projection Report"));
        Assert.Empty(Terms.SeasonsIn("No seasons here"));
    }

    [Fact]
    public void PeriodAllowed_keeps_only_periods_made_of_tested_seasons()
    {
        string[] fall =
        [
            "Fall 2026 - Winter 2027 Growth Projection Report",
            "Fall 2026 - Spring 2027 Growth Projection Report",
            "Fall 2026 - Fall 2027 Growth Projection Report",
            "Fall 2025 - Fall 2026 Growth Summary Report",
        ];
        string[] spring =
        [
            "Spring 2026 - Spring 2027 Growth Projection Report",
            "Fall 2025 - Spring 2026 Growth Summary Report",
            "Winter 2026 - Spring 2026 Growth Summary Report",
            "Spring 2025 - Spring 2026 Growth Summary Report",
        ];
        Assert.Equal(fall[1..], fall.Where(p => Terms.PeriodAllowed(p, FallSpring)));
        Assert.Equal([spring[0], spring[1], spring[3]], spring.Where(p => Terms.PeriodAllowed(p, FallSpring)));
        Assert.Equal(4, fall.Count(p => Terms.PeriodAllowed(p, Terms.Seasons)));
    }
}
