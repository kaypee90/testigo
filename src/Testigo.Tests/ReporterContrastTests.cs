using System.Globalization;
using System.Text.RegularExpressions;

namespace Testigo.Tests;

/// <summary>NFR-UX-1: verify AAA text contrast for the reporter's light/dark theme pairs.</summary>
public sealed class ReporterContrastTests
{
    [Fact]
    public void Reporter_text_pairs_meet_seven_to_one_contrast()
    {
        var css = File.ReadAllText(Path.Combine(RutTests.RepoRoot(), "src/Testigo.Reporter/wwwroot/css/site.css"));
        var themes = Regex.Matches(css, @":root\s*\{([^}]+)\}").Take(2).ToList();
        Assert.Equal(2, themes.Count);
        foreach (var theme in themes)
        {
            var colors = Regex.Matches(theme.Groups[1].Value, @"--([\w-]+):\s*(#[\da-f]{6})")
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            colors["white"] = "#ffffff";
            (string Foreground, string Background)[] pairs =
            [
                ("ink", "bg"), ("ink", "surface"), ("ink", "err-bg"),
                ("muted", "bg"), ("muted", "surface"), ("muted", "exit-bg"),
                ("accent", "bg"), ("accent", "surface"), ("accent", "err-bg"),
                ("accent", "warn-bg"), ("accent", "accent-soft"),
                ("accent-ink", "accent"), ("accent-ink", "accent-strong"),
                ("banner-ink", "banner-bg"), ("warn-ink", "warn-bg"),
                ("err-ink", "surface"), ("white", "err"),
            ];
            foreach (var (fg, bg) in pairs)
            {
                var a = Luminance(colors[fg]);
                var b = Luminance(colors[bg]);
                var ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
                Assert.True(ratio >= 7, $"{fg} {colors[fg]} on {bg} {colors[bg]}: {ratio:F2}:1");
            }
        }
    }

    private static double Luminance(string color)
    {
        var channels = new[] { 1, 3, 5 }.Select(i => int.Parse(color.AsSpan(i, 2), NumberStyles.HexNumber) / 255.0)
            .Select(v => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)).ToArray();
        return channels[0] * .2126 + channels[1] * .7152 + channels[2] * .0722;
    }
}
