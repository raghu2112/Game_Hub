namespace DellGamingControlCenter.App.Services;

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// <summary>
/// Offline, deterministic game artwork pipeline. Resolution order:
///   1. Bundled art:   Assets/Games/{slug}.png (pack resource, then loose file next to the EXE)
///   2. Executable icon: the game's own .exe icon via Icon.ExtractAssociatedIcon
///   3. Generated placeholder: hash(game name) → premium gradient + initials (always succeeds)
/// No network access. Results cached per game name for the app's lifetime. Never throws —
/// every failure degrades to the next source, ending at the generated placeholder.
/// </summary>
public static class GameArtService
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Curated clay-compatible gradients (violet / cyan / magenta family) — premium, not random hues.
    private static readonly (Color Top, Color Bottom)[] Gradients =
    {
        (Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x4C, 0x1D, 0x95)), // violet
        (Color.FromRgb(0x22, 0xD3, 0xEE), Color.FromRgb(0x0E, 0x74, 0x90)), // cyan
        (Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0x83, 0x18, 0x43)), // magenta
        (Color.FromRgb(0xA8, 0x55, 0xF7), Color.FromRgb(0x31, 0x2E, 0x81)), // violet→indigo
        (Color.FromRgb(0x38, 0xBD, 0xF8), Color.FromRgb(0x6D, 0x28, 0xD9)), // sky→violet
        (Color.FromRgb(0xF4, 0x72, 0xB6), Color.FromRgb(0x7C, 0x3A, 0xED)), // pink→violet
        (Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0x0F, 0x76, 0x6E)), // emerald→teal
        (Color.FromRgb(0xFB, 0xBF, 0x24), Color.FromRgb(0xB4, 0x53, 0x09)), // amber→bronze
    };

    /// <summary>
    /// Returns cached artwork for a game. Never returns null unless rendering is
    /// completely unavailable (caller should hide the image area in that case).
    /// </summary>
    public static ImageSource? GetArt(string? gameName, string? executablePath)
    {
        var key = string.IsNullOrWhiteSpace(gameName)
            ? (string.IsNullOrWhiteSpace(executablePath) ? "unknown" : Path.GetFileNameWithoutExtension(executablePath))
            : gameName.Trim();

        try
        {
            return Cache.GetOrAdd(key, k => Resolve(k, executablePath));
        }
        catch
        {
            return null; // binding must never crash the UI over artwork
        }
    }

    private static ImageSource Resolve(string key, string? executablePath)
    {
        // 1) Bundled art (pack resource, then loose file for drop-in user art)
        var fromBundle = TryLoadBundled(key);
        if (fromBundle != null) return fromBundle;

        // 2) The game exe's own icon — honest, unique per game, zero external assets
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var fromIcon = TryLoadExeIcon(executablePath);
            if (fromIcon != null) return fromIcon;
        }

        // 3) Deterministic generated placeholder — always succeeds
        return GeneratePlaceholder(key);
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }
        var s = sb.ToString();
        while (s.Contains("--", StringComparison.Ordinal)) s = s.Replace("--", "-", StringComparison.Ordinal);
        return s.Trim('-');
    }

    private static ImageSource? TryLoadBundled(string gameName)
    {
        var slug = Slug(gameName);
        if (slug.Length == 0) return null;

        // Pack resource (build action = Resource) — works in single-file publish.
        try
        {
            var uri = new Uri($"pack://application:,,,/DellGamingControlCenter.App;component/Assets/Games/{slug}.png", UriKind.Absolute);
            var img = new BitmapImage(uri);
            if (img.PixelWidth > 0)
            {
                img.Freeze();
                return img;
            }
        }
        catch { /* not bundled — fall through */ }

        // Loose file next to the EXE for user drop-in art (no rebuild needed).
        try
        {
            var loose = Path.Combine(AppContext.BaseDirectory, "Assets", "Games", slug + ".png");
            if (File.Exists(loose))
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(loose, UriKind.Absolute);
                img.EndInit();
                img.Freeze();
                return img;
            }
        }
        catch { /* unreadable — fall through */ }

        return null;
    }

    private static ImageSource? TryLoadExeIcon(string executablePath)
    {
        try
        {
            if (!File.Exists(executablePath)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon == null) return null;

            var src = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null; // missing file, no icon resource, etc.
        }
    }

    private static ImageSource GeneratePlaceholder(string gameName)
    {
        // Deterministic gradient pick from the game name hash.
        int hash = 0;
        foreach (var ch in gameName) hash = unchecked(hash * 31 + char.ToLowerInvariant(ch));
        hash = Math.Abs(hash == int.MinValue ? int.MaxValue : hash);
        var (top, bottom) = Gradients[hash % Gradients.Length];

        try
        {
            const int W = 240, H = 150;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var brush = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(1, 1));
                brush.Freeze();
                dc.DrawRectangle(brush, null, new Rect(0, 0, W, H));

                // Soft highlight sweep for depth (cheap, drawn once, cached forever)
                var sheen = new LinearGradientBrush(
                    Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255),
                    new Point(0, 0), new Point(0.4, 1));
                sheen.Freeze();
                dc.DrawRectangle(sheen, null, new Rect(0, 0, W, H));

                var initials = Initials(gameName);
                var ft = new FormattedText(initials, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"),
                        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    64, Brushes.White, 1.25);
                dc.DrawText(ft, new Point(W / 2 - ft.Width / 2, H / 2 - ft.Height / 2));
            }

            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
        catch
        {
            // Absolute last resort: flat frozen color block (no initials)
            try
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var flatBrush = new SolidColorBrush(top);
                    flatBrush.Freeze();
                    dc.DrawRectangle(flatBrush, null, new Rect(0, 0, 240, 150));
                }
                var rtb = new RenderTargetBitmap(240, 150, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                return rtb;
            }
            catch
            {
                return null!;
            }
        }
    }

    private static string Initials(string name)
    {
        var words = name.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "??";
        if (words.Length == 1)
        {
            var w = words[0];
            return w.Length <= 2 ? w.ToUpperInvariant() : (w[0].ToString() + w[1]).ToUpperInvariant();
        }
        return new string(words.Take(2).Select(w => char.ToUpperInvariant(w[0])).ToArray());
    }
}
