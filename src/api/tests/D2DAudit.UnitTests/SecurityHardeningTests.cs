using D2DAudit.Domain;

namespace D2DAudit.UnitTests;

public sealed class SecurityHardeningTests
{
    [Theory]
    [InlineData("https://checkr.example/reports/123", true)]
    [InlineData("http://localhost:5173/integrations", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JAVASCRIPT:alert(document.cookie)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("vbscript:msgbox(1)", false)]
    [InlineData("//evil.example/phish", false)]
    [InlineData("https://example.com/\r\nSet-Cookie:a=b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SafeUrl_accepts_only_http_and_https_without_control_characters(string? url, bool expected)
    {
        Assert.Equal(expected, SafeUrl.IsHttpOrHttps(url));
    }

    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "\"'=cmd|' /C calc'!A0\"")]
    [InlineData("+SUM(A1:A2)", "\"'+SUM(A1:A2)\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@SUM(1+1)", "\"'@SUM(1+1)\"")]
    [InlineData("\tformula", "\"'\tformula\"")]
    [InlineData("normal \"quoted\" text", "\"normal \"\"quoted\"\" text\"")]
    [InlineData("AC-07", "\"AC-07\"")]
    [InlineData("", "\"\"")]
    public void CsvSafety_neutralizes_spreadsheet_formulas_and_escapes_quotes(string input, string expected)
    {
        Assert.Equal(expected, CsvSafety.EscapeCell(input));
    }

    [Fact]
    public void TaskRecurrence_calculates_next_due_dates()
    {
        var start = new DateOnly(2026, 1, 15);
        Assert.Equal(new DateOnly(2026, 1, 22), TaskRecurrence.NextDue(start, "Weekly"));
        Assert.Equal(new DateOnly(2026, 2, 15), TaskRecurrence.NextDue(start, "Monthly"));
        Assert.Equal(new DateOnly(2026, 4, 15), TaskRecurrence.NextDue(start, "Quarterly"));
        Assert.Equal(new DateOnly(2027, 1, 15), TaskRecurrence.NextDue(start, "Annual"));
        Assert.Null(TaskRecurrence.NextDue(start, "None"));
        Assert.Null(TaskRecurrence.NextDue(start, null));
    }

    [Fact]
    public void RedirectPolicy_blocks_unlisted_origins_and_javascript_schemes()
    {
        var allowed = new[] { "http://localhost:5173", "https://app.d2daudit.example/auth/callback" };
        Assert.True(RedirectPolicy.IsAllowed("http://localhost:5173/integrations", allowed));
        Assert.True(RedirectPolicy.IsAllowed("https://app.d2daudit.example/auth/callback", allowed));
        Assert.True(RedirectPolicy.IsAllowed("", allowed));
        Assert.False(RedirectPolicy.IsAllowed("https://evil.example/steal", allowed));
        Assert.False(RedirectPolicy.IsAllowed("https://app.d2daudit.example/other", allowed));
        Assert.False(RedirectPolicy.IsAllowed("javascript:alert(1)", allowed));
    }
}
