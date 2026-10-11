namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DellGamingControlCenter.App.Services;
using Xunit;

/// <summary>
/// Regression tests for the clay UI/UX round (Phase 26):
///   1. Game artwork pipeline — fallback chain, caching, never-throw contract.
///   2. Theme resource consistency — every StaticResource reference resolves
///      (the exact class of bug that caused CRASH-1/CRASH-2 in the baseline).
///   3. Universal branding — no user-facing "Dell" strings in any view XAML.
/// </summary>
public class GameArtServiceTests
{
    /// <summary>WPF visuals (RenderTargetBitmap/DrawingVisual) require an STA thread.</summary>
    private static T RunOnSta<T>(Func<T> func)
    {
        Exception? error = null;
        T? result = default;
        var t = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(30));
        if (error != null) throw error;
        return result!;
    }

    [Fact]
    public void GetArt_MissingExecutable_NeverThrows_AndFallsBackToPlaceholder()
    {
        var art = RunOnSta(() => GameArtService.GetArt(
            "Some Totally Unknown Game", @"Z:\does\not\exist\nope.exe"));

        // Fallback chain ends at the generated placeholder — never null, never an exception.
        Assert.NotNull(art);
        Assert.Equal(240, ((BitmapSource)art!).PixelWidth); // generated placeholder is 240x150
    }

    [Fact]
    public void GetArt_EmptyNameAndPath_StillSucceeds()
    {
        var art = RunOnSta(() => GameArtService.GetArt("", ""));
        Assert.NotNull(art);
    }

    [Fact]
    public void GetArt_IsCached_SameInstanceForSameGame()
    {
        var a = RunOnSta(() => GameArtService.GetArt("Cache Probe Game", null));
        var b = RunOnSta(() => GameArtService.GetArt("Cache Probe Game", null));
        Assert.Same(a, b);
    }

    [Fact]
    public void GetArt_DistinctGames_GetDistinctArtInstances()
    {
        var names = new[] { "Alpha Prime", "Beta Protocol", "Gamma Drift", "Delta Vault" };
        var arts = names.Select(n => RunOnSta(() => GameArtService.GetArt(n, null))).ToList();

        // All resolved (never null) and no two games share an instance.
        Assert.All(arts, a => Assert.NotNull(a));
        Assert.Equal(arts.Count, arts.Distinct().Count());
    }

    [Fact]
    public void GetArt_ExeIconPath_NonexistentFile_DegradesQuietly()
    {
        // Icon extraction on a missing file must not throw — placeholder is returned.
        var art = RunOnSta(() => GameArtService.GetArt("Ghost Game", @"C:\Nowhere\ghost.exe"));
        Assert.NotNull(art);
    }
}

public class CircularGaugeRenderTests
{
    private static T RunOnSta<T>(Func<T> func)
    {
        Exception? error = null;
        T? result = default;
        var t = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(30));
        if (error != null) throw error;
        return result!;
    }

    [Fact]
    public void CircularGauge_RendersWithoutException_AcrossFullValueRange()
    {
        // Regression: OnRender previously called ArcTo without BeginFigure, throwing
        // "BeginFigure must be called before this API" on every layout pass —
        // observed as an exception storm in the published-EXE activity log.
        RunOnSta(() =>
        {
            foreach (var v in new[] { -1.0, 0.0, 0.4, 5.0, 50.0, 99.0, 100.0, double.NaN })
            {
                var g = new DellGamingControlCenter.App.Controls.CircularGauge
                {
                    Value = v,
                    Label = "CPU",
                    Width = 150,
                    Height = 150,
                };
                g.Measure(new System.Windows.Size(150, 150));
                g.Arrange(new System.Windows.Rect(0, 0, 150, 150));
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(150, 150, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(g); // throws synchronously if OnRender is broken
            }
            return true;
        });
    }
}

public class ThemeConsistencyTests
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DellGamingControlCenter.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir); // tests run from the repo checkout
        return dir!.FullName;
    }

    private static string ReadApp(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot.Value, "src", "DellGamingControlCenter.App" }.Concat(parts).ToArray()));

    private static readonly string[] GlobalResourceFiles =
    {
        "Themes/DarkGamingTheme.xaml",
        "App.xaml",
    };

    [Fact]
    public void EveryStaticResourceReference_InEveryView_Resolves()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in GlobalResourceFiles)
        {
            foreach (Match m in Regex.Matches(ReadApp(f.Replace('/', Path.DirectorySeparatorChar)), "x:Key=\"([^\"]+)\""))
            {
                defined.Add(m.Groups[1].Value);
            }
        }
        Assert.True(defined.Count > 40, "theme inventory unexpectedly small — file layout changed?");

        var viewsDir = Path.Combine(RepoRoot.Value, "src", "DellGamingControlCenter.App", "Views");
        var viewFiles = Directory.GetFiles(viewsDir, "*.xaml")
            .Append(Path.Combine(RepoRoot.Value, "src", "DellGamingControlCenter.App", "MainWindow.xaml"))
            .ToList();
        Assert.True(viewFiles.Count >= 10, "expected the full set of views");

        var failures = new List<string>();
        foreach (var file in viewFiles)
        {
            var content = File.ReadAllText(file);
            var localKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(content, "x:Key=\"([^\"]+)\""))
            {
                localKeys.Add(m.Groups[1].Value);
            }

            foreach (Match m in Regex.Matches(content, @"\{StaticResource\s+([A-Za-z0-9_]+)\}"))
            {
                var key = m.Groups[1].Value;
                if (!defined.Contains(key) && !localKeys.Contains(key))
                {
                    failures.Add($"{Path.GetFileName(file)}: {{StaticResource {key}}} is not defined");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "missing StaticResource definitions (crash-at-realization class of bug):\n" + string.Join("\n", failures));
    }

    [Fact]
    public void KeyClayThemeResources_ArePresent()
    {
        var theme = ReadApp("Themes", "DarkGamingTheme.xaml");
        foreach (var key in new[] { "ClayCard", "ClayNavButton", "ClayPrimaryButton", "ClaySegmented",
                                    "ClayToggle", "ClayStatTile", "ClayIconButton", "AppAccent", "AppBackgroundPrimary" })
        {
            Assert.Contains($"x:Key=\"{key}\"", theme);
        }

        // App-wide implicit ComboBox style must exist (dropdown readability contract).
        Assert.Matches(new Regex("<Style\\s+TargetType=\"ComboBox\""), theme);
    }
}

public class AccessibleNameTests
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DellGamingControlCenter.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir); // tests run from the repo checkout
        return dir!.FullName;
    }

    [Fact]
    public void EveryButton_HasAnAnnounceableAccessibleName()
    {
        // Regression: the game-card remove button was Content="&#x1F5D1;" (emoji glyph)
        // with no AutomationProperties.Name — screen readers cannot announce a bare glyph,
        // and UIA automation found it only by scanning for code points. Every button must
        // either carry an explicit AutomationProperties.Name or literal text content that
        // survives entity decoding.
        var files = Directory.GetFiles(Path.Combine(RepoRoot.Value, "src", "DellGamingControlCenter.App", "Views"), "*.xaml")
            .Append(Path.Combine(RepoRoot.Value, "src", "DellGamingControlCenter.App", "MainWindow.xaml"))
            .ToList();

        var failures = new List<string>();
        var buttonCount = 0;
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(content, "<Button\\b[^>]*>", RegexOptions.Singleline))
            {
                buttonCount++;

                var named = Regex.Match(m.Value, "AutomationProperties\\.Name=\"([^\\\"]+)\"");
                if (named.Success && named.Groups[1].Value.Trim().Length > 0) continue;

                var contentAttr = Regex.Match(m.Value, "Content=\"([^\\\"]*)\"");
                var value = contentAttr.Success ? contentAttr.Groups[1].Value : string.Empty;
                // Strip markup-entity references (&#x1F5D1;) and binding markup ({Binding ...})
                // so only real, speakable text remains.
                value = Regex.Replace(value, "&#x[0-9A-Fa-f]+;|&#[0-9]+;|\\{[^}]*\\}", string.Empty);
                if (Regex.IsMatch(value, "[A-Za-z0-9]")) continue;

                failures.Add($"{Path.GetFileName(file)}: button without an announceable name -> {m.Value.Trim()}");
            }
        }

        Assert.True(buttonCount >= 30, "expected the full set of buttons — file layout changed?");
        Assert.True(failures.Count == 0,
            "buttons whose accessible name is only a glyph/entity (unreachable to screen readers):\n"
            + string.Join("\n", failures));
    }
}

public class BrandingTests
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DellGamingControlCenter.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void UserFacingXaml_ContainsNoDellBranding()
    {
        // "DellGamingControlCenter.*" namespaces are internal technical identifiers and are allowed;
        // any other occurrence of "dell" in user-facing XAML is a branding violation.
        var dirs = new[]
        {
            Path.Combine(RepoRoot.Value, "src", "DellGamingControlCenter.App", "Views"),
        };
        var files = dirs.SelectMany(d => Directory.GetFiles(d, "*.xaml"))
            .Append(Path.Combine(RepoRoot.Value, "src", "DellGamingControlCenter.App", "MainWindow.xaml"))
            .Append(Path.Combine(RepoRoot.Value, "src", "DellGamingControlCenter.App", "Themes", "DarkGamingTheme.xaml"))
            .ToList();

        var failures = new List<string>();
        foreach (var file in files)
        {
            foreach (var line in File.ReadAllLines(file))
            {
                foreach (Match m in Regex.Matches(line, "dell", RegexOptions.IgnoreCase))
                {
                    var idx = m.Index;
                    var isInternalNs = idx >= line.IndexOf("DellGamingControlCenter", StringComparison.OrdinalIgnoreCase)
                                       && line.Contains("DellGamingControlCenter");
                    if (!isInternalNs)
                    {
                        failures.Add($"{Path.GetFileName(file)}: {line.Trim()}");
                        break;
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, "user-facing Dell branding found:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void MainWindowTitle_IsGamerHub()
    {
        var mw = File.ReadAllText(Path.Combine(RepoRoot.Value, "src", "DellGamingControlCenter.App", "MainWindow.xaml"));
        Assert.Contains("Title=\"GamerHub\"", mw);
        Assert.DoesNotContain("Dell Gaming", mw, StringComparison.OrdinalIgnoreCase);
    }
}
