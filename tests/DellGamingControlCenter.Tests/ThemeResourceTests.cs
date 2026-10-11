namespace DellGamingControlCenter.Tests;

using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;

/// <summary>
/// Startup-crash regression guard for the Claymorphism theme.
///
/// The XAML compiler does NOT reject a duplicate x:Key inside one ResourceDictionary
/// at build time; the failure only surfaces at process startup as
/// "ArgumentException: Item has already been added. Key in dictionary: 'X'" inside
/// App.InitializeComponent — i.e. the app compiled and unit-tested green but died
/// before showing a window. Parsing the dictionary here reproduces the exact load
/// path, so a duplicate key (or an unresolvable StaticResource inside the
/// dictionary) fails the suite instead of the shipped binary.
/// </summary>
public class ThemeResourceTests
{
    private static string FindRepoRoot()
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
        return dir.FullName;
    }

    [Fact]
    public void DarkGamingTheme_Loads_WithoutDuplicateKeysOrMissingResources()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "DellGamingControlCenter.App",
            "Themes", "DarkGamingTheme.xaml");
        Assert.True(File.Exists(path), "theme dictionary missing: " + path);

        var xaml = File.ReadAllText(path);

        // Throws ArgumentException on a duplicate x:Key, and XamlParseException on a
        // StaticResource that cannot be resolved while the dictionary loads — the two
        // failure modes that killed the app at startup during the claymorphism redesign.
        var dictionary = (ResourceDictionary)XamlReader.Parse(xaml);

        // Sanity: the palette the views depend on must actually be present.
        Assert.NotNull(dictionary["AppBackgroundBrush"]);
        Assert.NotNull(dictionary["TextPrimaryBrush"]);
        Assert.NotNull(dictionary["ClayCard"]);
        Assert.NotNull(dictionary["ActionButtonStyle"]);
    }

    /// <summary>
    /// Color resources assigned to Brush-typed properties crash at runtime when the
    /// style is applied ('#FF171521' is not a valid value for property 'Foreground'),
    /// but only when a live window applies the style — the XAML compiler and a plain
    /// ResourceDictionary parse both accept it because Setter values are deferred.
    /// This lint rejects that mismatch at build/test time.
    /// </summary>
    [Fact]
    public void ThemeNever_AssignsColorResources_ToBrushProperties()
    {
        var appDir = Path.Combine(FindRepoRoot(), "src", "DellGamingControlCenter.App");
        var xamlFiles = Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToList();
        Assert.True(xamlFiles.Count >= 10, "expected the theme + views + windows");

        var brushProperties = new[]
        {
            "Foreground", "Background", "BorderBrush", "Fill", "Stroke", "CaretBrush"
        };

        var failures = new List<string>();
        foreach (var file in xamlFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var prop in brushProperties)
                {
                    // Property="Foreground" Value="{StaticResource SomeColor}"  and the
                    // attribute form Foreground="{StaticResource SomeColor}"
                    var patterns = new[]
                    {
                        $"Property=\"{prop}\" Value=\"{{StaticResource ",
                        $"{prop}=\"{{StaticResource "
                    };
                    foreach (var pattern in patterns)
                    {
                        var idx = lines[i].IndexOf(pattern, StringComparison.Ordinal);
                        if (idx < 0) continue;
                        var rest = lines[i].Substring(idx + pattern.Length);
                        // The key ends at the markup extension's closing brace, not at the
                        // attribute's closing quote. Stopping at the quote used to swallow the
                        // trailing `}"/>` into the key, so `...Value="{StaticResource XColor}"/>`
                        // compared "XColor}/" against EndsWith("Color") and silently passed —
                        // which is how two real Color-on-Brush crashes survived this lint.
                        var end = rest.IndexOfAny(new[] { '"', '}', ' ', ',' });
                        if (end < 0) continue;
                        var key = rest.Substring(0, end);
                        if (key.EndsWith("Color", StringComparison.Ordinal))
                        {
                            failures.Add($"{Path.GetFileName(file)} line {i + 1}: {prop} assigned Color resource '{key}' (needs the matching Brush)");
                        }
                    }
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Brush-typed properties must use Brush resources, never *Color resources:\n"
            + string.Join("\n", failures));
    }

    [Fact]
    public void ThemeDefines_EveryKey_ReferencedByTheAppXaml()
    {
        // App.xaml merges the theme and references converters/brushes at startup;
        // a dangling reference there is also a startup crash.
        var path = Path.Combine(FindRepoRoot(), "src", "DellGamingControlCenter.App",
            "Themes", "DarkGamingTheme.xaml");
        var xaml = File.ReadAllText(path);
        var dictionary = (ResourceDictionary)XamlReader.Parse(xaml);

        foreach (var key in new[]
                 {
                     "AppBackgroundBrush", "SurfaceBrush", "RaisedSurfaceBrush", "InsetSurfaceBrush",
                     "PrimaryAccentBrush", "SecondaryAccentBrush", "TelemetryAccentBrush",
                     "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush",
                     "SuccessBrush", "WarningBrush", "ErrorBrush", "FocusBrush",
                     "CardOuterShadowBrush", "CardInnerHighlightBrush"
                 })
        {
            Assert.True(dictionary.Contains(key), $"spec-mandated semantic token missing from theme: {key}");
        }
    }

    /// <summary>
    /// Whole-app resource integrity.
    ///
    /// The two tests above only parse the theme dictionary, so a reference that lives in a
    /// view (or inside a deferred ControlTemplate, where StaticResource is resolved on first
    /// use rather than at parse time) could still name a key nothing defines. That failure
    /// mode is silent at build time and at parse time, and only surfaces when the control is
    /// first rendered — which is how the drop-down popup gap stayed hidden. This lint walks
    /// every App XAML file and requires each referenced key to exist at application scope or
    /// locally in the same file.
    /// </summary>
    [Fact]
    public void EveryResourceReferencedByAnyAppXaml_IsDefinedSomewhere()
    {
        var appDir = Path.Combine(FindRepoRoot(), "src", "DellGamingControlCenter.App");

        var xamlFiles = Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToList();
        Assert.True(xamlFiles.Count >= 10, "expected the theme + views + windows");

        // Application scope = everything the theme defines, plus the keys App.xaml declares.
        var appScopeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in CollectKeys(Path.Combine(appDir, "Themes", "DarkGamingTheme.xaml")))
        {
            appScopeKeys.Add(key);
        }
        foreach (var key in CollectKeys(Path.Combine(appDir, "App.xaml")))
        {
            appScopeKeys.Add(key);
        }

        var missing = new List<string>();
        foreach (var file in xamlFiles)
        {
            var localKeys = new HashSet<string>(CollectKeys(file), StringComparer.Ordinal);
            foreach (var (key, line) in CollectReferences(File.ReadAllText(file)))
            {
                if (appScopeKeys.Contains(key) || localKeys.Contains(key))
                {
                    continue;
                }
                missing.Add($"{Path.GetFileName(file)}:{line} -> '{key}'");
            }
        }

        Assert.True(missing.Count == 0,
            "XAML references a resource key that no dictionary defines "
            + "(a crash at startup, or on first render for keys inside a template):\n"
            + string.Join("\n", missing));
    }

    private static IEnumerable<string> CollectKeys(string path)
    {
        foreach (Match match in Regex.Matches(File.ReadAllText(path), "x:Key=\"([^\"]+)\""))
        {
            yield return match.Groups[1].Value;
        }
    }

    private static IEnumerable<(string Key, int Line)> CollectReferences(string xaml)
    {
        var lines = xaml.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match match in Regex.Matches(lines[i], "\\{(Static|Dynamic)Resource\\s+([^}\\s,]+)"))
            {
                yield return (match.Groups[2].Value, i + 1);
            }
        }
    }
}
