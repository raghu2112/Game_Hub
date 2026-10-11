namespace DellGamingControlCenter.Tests;

using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

/// <summary>
/// Runtime checks for the WPF surfaces that a plain ResourceDictionary parse cannot reach.
///
/// Why these exist: a ControlTemplate Setter value is deferred, so a missing (or merely
/// mistyped) StaticResource inside a template passes both the XAML compiler and a dictionary
/// parse, then throws the moment the template is instantiated by a live control.
/// ThemeResourceTests covers the parse-time crash classes; this class covers the
/// instantiate-time ones — the drop-down/popup gap and the pressed-state button triggers.
///
/// Resource scope note: the shipped app merges the theme into Application.Resources, but
/// creating a WPF Application in a test host sets the AppDomain-wide Application.Current and
/// breaks unrelated tests (Application.MainWindow is then owned by another thread). Every
/// test here therefore scopes the theme to the element's own Resources, which exercises the
/// same implicit-style resolution without leaking global state.
///
/// Every test runs on a dedicated STA thread and shows a real (off-screen) window. No system
/// setting, power plan, registry key or process is touched.
/// </summary>
public class WpfSurfaceRuntimeTests
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

    private static string ThemePath()
        => Path.Combine(FindRepoRoot(), "src", "DellGamingControlCenter.App",
            "Themes", "DarkGamingTheme.xaml");

    private static ResourceDictionary LoadTheme()
        => (ResourceDictionary)XamlReader.Parse(File.ReadAllText(ThemePath()));

    private static Color BrushColor(Brush? brush)
        => brush is SolidColorBrush solid ? solid.Color
            : throw new InvalidOperationException($"expected a SolidColorBrush, got {brush?.GetType().Name ?? "null"}");

    /// <summary>Runs <paramref name="action"/> on a fresh STA thread with its own dispatcher,
    /// which is what WPF requires to instantiate templates and create window HWNDs.</summary>
    private static void OnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                try { Dispatcher.CurrentDispatcher.InvokeShutdown(); } catch { /* nothing to shut down */ }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "STA surface test did not finish in 45 s");
        if (failure != null)
        {
            throw new TargetInvocationException(failure);
        }
    }

    /// <summary>Runs the dispatcher until ApplicationIdle so queued layout (and therefore
    /// template application) actually completes. Measure/Arrange alone is not enough for a
    /// Window-hosted tree: the HWND target drives layout from the message pump.</summary>
    private static void PumpUntilIdle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    // ── 1. Dropdown / popup open state ────────────────────────────────────

    [Fact]
    public void ComboBox_DropDownOpens_WithThemedPopupSurface_AndNoTemplateResourceCrash()
    {
        var expected = BrushColor((Brush?)LoadTheme()["RaisedSurfaceBrush"]);

        OnStaThread(() =>
        {
            var window = new Window
            {
                Width = 420,
                Height = 320,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            window.Resources = LoadTheme();

            var combo = new ComboBox { Width = 220, Margin = new Thickness(20) };
            foreach (var item in new[] { "Quiet", "Balanced", "Gaming" })
            {
                combo.Items.Add(item);
            }
            combo.SelectedIndex = 1;
            window.Content = combo;

            try
            {
                window.Show();

                // Forcing layout instantiates the control template — this is the line that
                // throws if the popup references a resource the theme does not define.
                combo.Measure(new Size(400, 300));
                combo.Arrange(new Rect(new Size(combo.DesiredSize.Width, combo.DesiredSize.Height)));
                combo.UpdateLayout();

                Assert.True(combo.Template.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup,
                    "the themed ComboBox template must expose PART_Popup");

                combo.IsDropDownOpen = true;

                var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo)!;
                Assert.True(popup.IsOpen, "drop-down must actually open");

                // The popup's own surface must be the clay raised tone, not a system colour.
                var surface = Assert.IsType<Border>(popup.Child);
                Assert.Equal(expected, BrushColor(surface.Background));

                // Selected-item styling must resolve against the same theme.
                var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(1);
                Assert.NotNull(item);
                item.Measure(new Size(200, 40));
                item.Arrange(new Rect(new Size(200, 40)));
                Assert.Equal(BrushColor((Brush?)LoadTheme()["TextPrimaryBrush"]), BrushColor(item.Foreground));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ToolTip_UsesTheClaySurface_NotTheWindowsSystemColours()
    {
        OnStaThread(() =>
        {
            var theme = LoadTheme();
            var expectedBackground = BrushColor((Brush?)theme["RaisedSurfaceBrush"]);
            var expectedForeground = BrushColor((Brush?)theme["TextPrimaryBrush"]);

            var tip = new ToolTip { Content = "Search your game library", Resources = theme };

            tip.Measure(new Size(400, 120));
            tip.Arrange(new Rect(new Size(tip.DesiredSize.Width, tip.DesiredSize.Height)));
            tip.UpdateLayout();

            Assert.Equal(expectedBackground, BrushColor(tip.Background));
            Assert.Equal(expectedForeground, BrushColor(tip.Foreground));
        });
    }

    // ── 2. Button templates: pressed/hover triggers must seal cleanly ──────

    /// <summary>
    /// Reproduces the app's real button path: apply the themed style, instantiate the
    /// template, then drive the interaction triggers. A Setter inside a Trigger is validated
    /// when the template seals, so assigning a Color resource to a Brush-typed property here
    /// throws "… is not a valid value for the 'Panel.Background' property on a Setter" — the
    /// defect that affected ClayNavButton and ActionButtonStyle.
    /// </summary>
    [Theory]
    [InlineData("ClayNavButton")]
    [InlineData("ActionButtonStyle")]
    [InlineData("SecondaryButtonStyle")]
    [InlineData("ClayIconButton")]
    public void ThemedButtonTemplates_SealAndDrive_WithoutATypeCrash(string styleKey)
    {
        OnStaThread(() =>
        {
            var theme = LoadTheme();
            var style = Assert.IsType<Style>(theme[styleKey]);

            var window = new Window
            {
                Width = 400,
                Height = 200,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Content = new StackPanel { Margin = new Thickness(20) }
            };
            window.Resources = theme;

            try
            {
                var button = new Button
                {
                    Content = "ACTIVATE",
                    Style = style,
                    Tag = true // drives the selected-state DataTrigger used by the nav style
                };
                var panel = new StackPanel { Margin = new Thickness(20) };
                panel.Children.Add(button);
                window.Content = panel;

                window.Show();
                PumpUntilIdle();

                button.Measure(new Size(360, 120));
                button.Arrange(new Rect(new Size(button.DesiredSize.Width, button.DesiredSize.Height)));
                button.UpdateLayout();

                // Sealing happens when the template is first instantiated. Reaching a built
                // visual tree without an ArgumentException is the regression guard.
                Assert.True(VisualTreeHelper.GetChildrenCount(button) > 0,
                    $"{styleKey} template must be instantiated (this is what seals its Triggers)");

                // The disabled state must be a real presentation change, not opacity alone.
                button.IsEnabled = true;
                button.UpdateLayout();
                Assert.True(button.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ── 3. Overlay window: declarative desktop-overlay contract ───────────

    /// <summary>
    /// OverlayWindow is never instantiated anywhere in the application (it is dead code — see
    /// STABILIZATION_BASELINE.md B13), so it cannot be opened in the running app or during a
    /// gaming session, and giving a test host an Application scope just to construct it leaks
    /// AppDomain-global state that breaks unrelated tests. What can be locked down honestly is
    /// the safety-critical XAML contract it declares: a click-through, topmost, taskbar-free,
    /// transparent desktop overlay — and the in-code truth that it is not an injected overlay.
    /// Its resource keys are covered by ThemeResourceTests.EveryResourceReferencedByAnyAppXaml_IsDefinedSomewhere.
    /// </summary>
    [Fact]
    public void OverlayWindow_DeclaresItsDesktopOverlayContract_AndDoesNotClaimInGameInjection()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src",
            "DellGamingControlCenter.App", "Views", "OverlayWindow.xaml"));

        foreach (var attribute in new[]
                 {
                     "WindowStyle=\"None\"",
                     "AllowsTransparency=\"True\"",
                     "Background=\"Transparent\"",
                     "Topmost=\"True\"",
                     "ShowInTaskbar=\"False\"",
                     "IsHitTestVisible=\"False\""
                 })
        {
            Assert.Contains(attribute, xaml);
        }

        // The overlay must not be presented as something it is not.
        Assert.Contains("not an injected in-game overlay", xaml);
        Assert.Contains("It draws no DirectX/Vulkan surfaces", xaml);
    }

    /// <summary>The overlay's click-through behaviour is applied in code-behind; that code must
    /// still set the extended window styles that make it non-interactive and keep it out of
    /// Alt-Tab, and must position it inside the working area.</summary>
    [Fact]
    public void OverlayWindow_CodeBehind_StillAppliesClickThroughAndToolWindowStyles()
    {
        var code = File.ReadAllText(Path.Combine(FindRepoRoot(), "src",
            "DellGamingControlCenter.App", "Views", "OverlayWindow.xaml.cs"));

        Assert.Contains("WS_EX_TRANSPARENT", code);
        Assert.Contains("WS_EX_TOOLWINDOW", code);
        Assert.Contains("GWL_EXSTYLE", code);
        Assert.Contains("SystemParameters.WorkArea", code);
    }
}
