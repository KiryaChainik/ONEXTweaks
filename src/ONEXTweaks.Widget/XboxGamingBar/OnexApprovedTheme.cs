// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace XboxGamingBar
{
    internal enum OnexThemeRole { Power, Network, Controller, Display, Cooling, Lighting, Success, Warning, Danger }

    internal sealed partial class OnexApprovedTheme
    {
        private readonly Color[] colors;
        private readonly string assetFolder;
        internal bool Light { get; }
        internal double Wash { get; }
        internal double ChipWash { get; }
        internal Color Page => colors[0];
        internal Color Surface => colors[1];
        internal Color Inset => colors[2];
        internal Color Raised => colors[3];
        internal Color Line => colors[4];
        internal Color Text => colors[5];
        internal Color Muted => colors[6];
        internal Color Focus => colors[7];
        internal Color FocusSoft => colors[8];
        internal Color Selected => colors[9];
        internal Color SelectedText => colors[10];
        internal Color SelectedLine => colors[11];
        internal Color DiagramInk => colors[12];
        internal Color DiagramOutline => colors[13];
        internal Color DiagramFrame => colors[14];

        private OnexApprovedTheme(string assetFolder, bool light, int wash, int chipWash, string tokens)
        {
            this.assetFolder = assetFolder;
            Light = light;
            Wash = wash / 100d;
            ChipWash = chipWash / 100d;
            colors = tokens.Split(' ').Select(hex => Color.FromArgb(255,
                Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16))).ToArray();
        }

        internal static OnexApprovedTheme Find(string name) =>
            name != null && Themes.TryGetValue(name, out var value) ? value : null;

        internal static OnexApprovedTheme Current =>
            Find(ApplicationData.Current.LocalSettings.Values["WidgetTheme"] as string) ?? Themes[OnexPalette.Name];

        internal Color Accent(OnexThemeRole role, bool text = false) => colors[(text ? 24 : 15) + (int)role];

        internal ThemeColors Create(string name) => new ThemeColors
        {
            Name = name, PageBackground = Page, CardBackground = Surface, CardBorder = Line,
            AccentColor = Focus, TextPrimary = Text, TextSecondary = Muted,
            SelectionBackground = Selected, SelectionText = SelectedText, ButtonBackground = Raised,
            ButtonBorder = Line, TileOff = Surface, TileOn = Selected, TileIcon = Text,
            MetricsBackground = Inset, MetricsBorder = Line, GlowColor = Focus
        };

        internal BitmapImage Artwork(string file) =>
            new BitmapImage(new Uri("ms-appx:///Assets/OnexThemes/" + assetFolder + "/" + file + ".png"));

        // Use opacity when the background is the tile's actual card surface.
        // Blend explicitly where the target (a control or a plot) owns its backdrop.
        internal static Color Blend(Color surface, Color mark, double amount) => Color.FromArgb(255,
            (byte)Math.Round(surface.R + (mark.R - surface.R) * amount),
            (byte)Math.Round(surface.G + (mark.G - surface.G) * amount),
            (byte)Math.Round(surface.B + (mark.B - surface.B) * amount));

        private sealed class ArtBinding
        {
            internal Action<OnexApprovedTheme> Paint;
            internal OnexApprovedTheme Last;
        }
        private static readonly ConditionalWeakTable<FrameworkElement, ArtBinding> Bindings =
            new ConditionalWeakTable<FrameworkElement, ArtBinding>();
        private static readonly ConditionalWeakTable<Control, object> ArtTargets =
            new ConditionalWeakTable<Control, object>();

        internal static bool OwnsArtwork(FrameworkElement scope) => Bindings.TryGetValue(scope, out _);
        internal static bool OwnsTarget(Control target) => ArtTargets.TryGetValue(target, out _);
        internal static void OwnTarget(Control target) => ArtTargets.Add(target, new object());

        internal static void Bind(FrameworkElement scope, OnexApprovedTheme theme, Action<OnexApprovedTheme> paint)
        {
            Bindings.Add(scope, new ArtBinding { Paint = paint });
            Repaint(scope, theme);
        }

        internal static void Repaint(FrameworkElement scope, OnexApprovedTheme theme)
        {
            if (theme == null || !Bindings.TryGetValue(scope, out var binding) || ReferenceEquals(binding.Last, theme)) return;
            binding.Paint(theme);
            binding.Last = theme;
        }
    }
}
