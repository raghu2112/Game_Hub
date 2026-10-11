namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using DellGamingControlCenter.App.Converters;
using Xunit;

/// <summary>
/// Regression tests for two classes of silent UI defect found by the post-Phase-30
/// visual QA pass:
///   1. Dead bindings — DiagnosticsView bound PrimaryBottleneckName / SecondaryBottleneckName /
///      *Confidence / Explanation to properties no type ever declared, so WPF raised a
///      binding error at runtime and the cards rendered their placeholder text over empty
///      values (" LIMITATION"). Nothing in the build caught it.
///   2. Converters whose result the target property cannot use — InverseBooleanConverter
///      returned bool for a Visibility target, the binding failed silently, and the element
///      kept its default Visible state: hardware capabilities showed a red cross next to
///      "Status: Supported", and the editor's warnings never hid.
/// </summary>
public class UiBindingAndConverterTests
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

    /// <summary>
    /// Every root segment of every {Binding} in every view must exist as a property
    /// somewhere in the shipped assemblies. Scope is intentionally broad (any public
    /// property of any type in App/Core/Services) so nested DataTemplate bindings to
    /// model objects do not produce false positives, while bindings to properties that
    /// do not exist at all — the dead-binding class — always fail.
    /// </summary>
    [Fact]
    public void EveryViewBindingPathRoot_ExistsAsAPropertySomewhere()
    {
        var propertyNames = CollectPropertyNames();

        // Roots that are part of binding markup rather than data paths.
        var markupRoots = new HashSet<string>(StringComparer.Ordinal)
        {
            "DataContext", "Tag", "Path", "ElementName", "RelativeSource", "AncestorType",
            "Mode", "Converter", "ConverterParameter", "StringFormat", "UpdateSourceTrigger"
        };

        var viewsDir = Path.Combine(RepoRoot.Value, "src", "DellGamingControlCenter.App", "Views");
        var files = Directory.GetFiles(viewsDir, "*.xaml")
            .Append(Path.Combine(RepoRoot.Value, "src", "DellGamingControlCenter.App", "MainWindow.xaml"))
            .ToList();
        Assert.True(files.Count >= 10, "expected the full set of views — file layout changed?");

        var failures = new List<string>();
        var checkedBindings = 0;

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(content, @"\{Binding\s+([^}]+)\}"))
            {
                var spec = m.Groups[1].Value.Trim();
                var pathPart = spec.Split(',')[0].Trim();   // strip ,Converter=..., StringFormat=...
                if (pathPart.Length == 0) continue;         // {Binding} — whole DataContext
                if (pathPart.Contains('=')) continue;       // ElementName=..., RelativeSource=...
                if (pathPart.StartsWith('[') || pathPart.StartsWith('(')) continue;

                var root = pathPart.Split('.')[0].Trim().Trim('(', ')');
                if (root.Length == 0 || markupRoots.Contains(root)) continue;

                checkedBindings++;
                if (!propertyNames.Contains(root))
                {
                    failures.Add($"{Path.GetFileName(file)}: {{Binding {root}}} matches no property in App/Core/Services");
                }
            }
        }

        Assert.True(checkedBindings > 50, "binding scan found almost nothing — regex vs. file layout changed?");
        Assert.True(failures.Count == 0,
            "bindings whose path root does not exist (render as empty / binding errors in the output log):\n"
            + string.Join("\n", failures.Distinct()));
    }

    private static HashSet<string> CollectPropertyNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var assemblies = new[]
        {
            typeof(DellGamingControlCenter.App.App).Assembly,   // App (views, view models)
            typeof(DellGamingControlCenter.Core.Interfaces.IDiagnosticEngine).Assembly,
            typeof(DellGamingControlCenter.Services.DiagnosticEngine).Assembly,
        };

        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetExportedTypes())
            {
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    names.Add(p.Name);
                }
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    names.Add(f.Name);
                }
            }
        }

        Assert.True(names.Count > 500, "property inventory unexpectedly small");
        return names;
    }

    // ── InverseBooleanConverter ────────────────────────────────────────────────

    [Fact]
    public void InverseBooleanConverter_VisibilityTarget_HidesWhenTrue()
    {
        var c = new InverseBooleanConverter();
        Assert.Equal(Visibility.Collapsed, c.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, c.Convert(false, typeof(Visibility), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void InverseBooleanConverter_BoolTarget_StillInvertsBoolean()
    {
        // IsEnabled="{Binding IsAnalyzing, Converter={StaticResource InverseBooleanConverter}}"
        var c = new InverseBooleanConverter();
        Assert.Equal(false, c.Convert(true, typeof(bool), null!, CultureInfo.InvariantCulture));
        Assert.Equal(true, c.Convert(false, typeof(bool), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void InverseBooleanConverter_NonBoolValue_PassesThrough()
    {
        var c = new InverseBooleanConverter();
        Assert.Equal("unchanged", c.Convert("unchanged", typeof(Visibility), null!, CultureInfo.InvariantCulture));
    }

    // ── CountToVisibilityConverter ─────────────────────────────────────────────

    [Fact]
    public void CountToVisibility_Default_ShowsOnlyWhileEmpty()
    {
        var c = new CountToVisibilityConverter();
        Assert.Equal(Visibility.Visible, c.Convert(0, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, c.Convert(3, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, c.Convert(new List<int>(), typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, c.Convert(new List<int> { 1 }, typeof(Visibility), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CountToVisibility_NonEmptyParameter_ShowsOnlyWhenPopulated()
    {
        var c = new CountToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, c.Convert(0, typeof(Visibility), "NonEmpty", CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, c.Convert(2, typeof(Visibility), "NonEmpty", CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, c.Convert(new List<int>(), typeof(Visibility), "NonEmpty", CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, c.Convert(new List<int> { 1 }, typeof(Visibility), "NonEmpty", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CountToVisibility_UnknownValue_Collapses()
    {
        var c = new CountToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, c.Convert(null!, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, c.Convert("not a count", typeof(Visibility), "NonEmpty", CultureInfo.InvariantCulture));
    }
}
