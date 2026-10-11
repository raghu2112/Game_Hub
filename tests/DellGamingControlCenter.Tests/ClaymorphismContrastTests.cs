namespace DellGamingControlCenter.Tests;

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Markup;
using Xunit;

/// <summary>
/// Objective accessibility guard for the Claymorphism palette.
///
/// Human aesthetic review was not available for this pass, so the parts of visual quality
/// that CAN be measured are measured here instead of being asserted in prose: WCAG 2.1
/// contrast for every foreground/background pair the views actually use, and the luminance
/// ordering that the clay depth effect depends on.
///
/// The colour values are parsed from DarkGamingTheme.xaml rather than duplicated, so these
/// thresholds fail the suite if a token is retuned away from legibility.
/// </summary>
public class ClaymorphismContrastTests
{
    private static readonly ResourceDictionary Theme = LoadTheme();

    private static ResourceDictionary LoadTheme()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DellGamingControlCenter.sln")))
        {
            dir = dir.Parent;
        }
        if (dir == null)
        {
            throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
        }

        var path = Path.Combine(dir.FullName, "src", "DellGamingControlCenter.App",
            "Themes", "DarkGamingTheme.xaml");
        return (ResourceDictionary)XamlReader.Parse(File.ReadAllText(path));
    }

    /// <summary>Resolves a theme colour by either its semantic Brush name or its *Color name,
    /// so the pair list below reads the way the views reference it.</summary>
    private static Color C(string key) => Theme[key] switch
    {
        Color color => color,
        SolidColorBrush brush => brush.Color,
        _ => throw new InvalidOperationException(
            $"resource '{key}' is neither a Color nor a SolidColorBrush")
    };

    // WCAG 2.1 relative luminance + contrast ratio.
    private static double Luminance(Color c)
    {
        static double Channel(byte value)
        {
            var s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        if (la < lb)
        {
            (la, lb) = (lb, la);
        }
        return (la + 0.05) / (lb + 0.05);
    }

    /// <summary>Every pair the views really render, with the WCAG AA minimum that applies to it.
    /// All of these set normal-size text (10-17px), so the 4.5:1 threshold applies.</summary>
    public static TheoryData<string, string, string> ReadingPairs => new()
    {
        { "TextPrimaryBrush",   "AppBackgroundBrush",       "body and title text on the app background" },
        { "TextPrimaryBrush",   "SurfaceBrush",             "body and title text on cards" },
        { "TextPrimaryBrush",   "RaisedSurfaceBrush",       "body and title text on raised/selected cards" },
        { "TextPrimaryBrush",   "AppAccentDim",             "selected list and segmented-control items" },
        { "TextSecondaryBrush", "AppBackgroundBrush",       "secondary text on the app background" },
        { "TextSecondaryBrush", "SurfaceBrush",             "secondary text on cards" },
        { "TextSecondaryBrush", "RaisedSurfaceBrush",       "secondary text on raised cards" },
        { "TextMutedBrush",     "AppBackgroundBrush",       "metadata and footnotes on the app background" },
        { "TextMutedBrush",     "SurfaceBrush",             "metadata and footnotes on cards" },
        { "TextMutedBrush",     "RaisedSurfaceBrush",       "metadata on raised/selected mode cards" },
        { "AppBackgroundBrush", "AppAccentColor",           "primary button label on the violet face" },
        { "AppBackgroundBrush", "AppAccentHoverColor",      "primary button label on hover" },
        { "SecondaryAccentBrush", "AppBackgroundBrush",     "highlighted values and subheaders" },
        { "SuccessBrush",       "SurfaceBrush",             "success and recommendation text" },
        { "WarningBrush",       "SurfaceBrush",             "warnings and unsupported text" },
        { "ErrorBrush",         "SurfaceBrush",             "error text" },
        { "TelemetryAccentBrush", "InsetSurfaceBrush",      "telemetry readouts inside recessed meters" },
        { "TelemetryAccentBrush", "AppBackgroundBrush",     "telemetry labels and focus ring" }
    };

    [Theory]
    [MemberData(nameof(ReadingPairs))]
    public void ReadingText_MeetsWcagAa(string foreground, string background, string usage)
    {
        var ratio = Contrast(C(foreground), C(background));

        Assert.True(ratio >= 4.5,
            $"{foreground} on {background} measures {ratio:F2}:1 but needs 4.5:1 (WCAG AA) "
            + $"for {usage}. Token {C(foreground)} on {C(background)}.");
    }

    /// <summary>
    /// The claymorphism depth effect is carried entirely by small luminance steps between
    /// surface tones. If a retune inverts or flattens that ordering, raised cards stop reading
    /// as raised and recessed wells stop reading as recessed — a silent, whole-UI regression.
    /// </summary>
    [Fact]
    public void SurfaceTones_KeepTheClayDepthOrdering()
    {
        var background = Luminance(C("AppBackgroundPrimaryColor")); // app base      #171521
        var card       = Luminance(C("AppBackgroundSecondaryColor")); // primary cards #211E30
        var raised     = Luminance(C("AppBackgroundTertiaryColor"));  // extruded cards #29243B
        var inset      = Luminance(C("AppInsetSurfaceColor"));        // recessed wells  #191725

        // Extrusion: cards rise off the app background, and raised cards rise off cards.
        Assert.True(background < card, "cards must be lighter than the app background");
        Assert.True(card < raised, "raised/extruded cards must be lighter than standard cards");

        // Recession: a well is dark because it sits inside a card, so the comparison that
        // matters is inset-versus-its-surrounding-card. (The inset tone is a hair lighter than
        // the app background; that is not a defect, since wells are never drawn on the base.)
        Assert.True(inset < card, "recessed wells must be darker than the cards that contain them");

        // Steps stay restrained: an over-wide gap would read as harsh banding, not clay.
        Assert.True(Contrast(C("AppBackgroundPrimaryColor"), C("AppBackgroundSecondaryColor")) < 2.0,
            "the card-to-background step should stay subtle");
        Assert.True(Contrast(C("AppBackgroundSecondaryColor"), C("AppBackgroundTertiaryColor")) < 2.0,
            "the raised-to-card step should stay subtle");
    }

    /// <summary>Status colours must stay distinguishable from the surfaces they sit on, and from
    /// each other, so severity is never carried by hue alone against a near-matching backdrop.</summary>
    [Fact]
    public void StatusColors_StaysDistinguishable_OnCards()
    {
        Assert.True(Contrast(C("SuccessBrush"), C("SurfaceBrush")) >= 4.5);
        Assert.True(Contrast(C("WarningBrush"), C("SurfaceBrush")) >= 4.5);
        Assert.True(Contrast(C("ErrorBrush"), C("SurfaceBrush")) >= 4.5);

        // Separation between the three status hues themselves.
        Assert.True(Luminance(C("SuccessBrush")) != Luminance(C("WarningBrush")));
        Assert.True(Luminance(C("WarningBrush")) != Luminance(C("ErrorBrush")));
    }
}
