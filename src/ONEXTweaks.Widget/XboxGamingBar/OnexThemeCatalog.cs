using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // Palette sources and the explicitly derived Latte variant are recorded in
        // work/references/themes363-sources.json. Console/glass themes are adaptations.
        private static readonly string[] OnexCatalogNames = {
            "Dracula", "Catppuccin Mocha", "Catppuccin Macchiato", "Catppuccin Frappé", "Catppuccin Latte",
            "Catppuccin Latte Soft (adapted)", "Cobalt2", "Miasma", "Kanagawa Wave", "Kanagawa Dragon",
            "Kanagawa Lotus", "Shades of Purple", "Xbox 360", "PS4", "PS5", "Liquid Glass",
            "Windows 11 Light", "Windows 11 Dark"
        };
        internal static bool HasOnexCatalogPalette(string name)=>name=="OLED"||OnexCatalogNames.Contains(name);
        private static string[] OnexAllowedThemeNames=>new[]{OnexPalette.Name,"OLED"}.Concat(OnexCatalogNames).ToArray();
        private static string NormalizeOnexThemeName(string name)
        {
            if(name=="Kanagawa Dark")name="Kanagawa Wave";
            if(name=="Kanagawa Light")name="Kanagawa Lotus";
            if(name=="Catppuccin Frappe")name="Catppuccin Frappé";
            return OnexAllowedThemeNames.Contains(name)?name:name=="Catppuccin"?"Catppuccin Mocha":name=="Windows"?"Windows 11 Dark":OnexPalette.Name;
        }
        private bool OnexCatalogTheme => HasOnexCatalogPalette(currentThemeName);
        private static Color OnexHex(string value) => Color.FromArgb(255,
            Convert.ToByte(value.Substring(0, 2), 16), Convert.ToByte(value.Substring(2, 2), 16), Convert.ToByte(value.Substring(4, 2), 16));
        private void EnsureOnexThemeCatalog()
        {
            if (!OnexFrontend.UsesOwnBackend) return;
            ThemeComboBox.Width=280;ThemeComboBox.MinWidth=280;ThemeComboBox.MaxDropDownHeight=360;
            foreach (var name in OnexAllowedThemeNames)
                WidgetThemes[name] = OnexApprovedTheme.Find(name).Create(name);
            var glass=WidgetThemes["Liquid Glass"];glass.PageBackground2=OnexHex("29465D");
            glass.TileOff2=OnexHex("334D63");glass.TileOn2=OnexHex("3B596F");glass.ShimmerEnabled=true;
            var allowed=OnexAllowedThemeNames;
            foreach(string key in WidgetThemes.Keys.Where(x=>!allowed.Contains(x)).ToArray())WidgetThemes.Remove(key);
            string selected=NormalizeOnexThemeName((ThemeComboBox.SelectedItem as ComboBoxItem)?.Content as string??currentThemeName);
            if(!ThemeComboBox.Items.OfType<ComboBoxItem>().Select(x=>x.Content as string).SequenceEqual(allowed))
            {
                bool initialized=isThemeInitialized;isThemeInitialized=false;
                try{ThemeComboBox.Items.Clear();foreach(string name in allowed)ThemeComboBox.Items.Add(new ComboBoxItem{Content=name});ThemeComboBox.SelectedItem=ThemeComboBox.Items.OfType<ComboBoxItem>().First(x=>x.Content as string==selected);}
                finally{isThemeInitialized=initialized;}
            }
        }
        private static bool OnexLight(ThemeColors theme) =>
            OnexApprovedTheme.Find(theme.Name)?.Light ??
            (theme.PageBackground.R * .2126 + theme.PageBackground.G * .7152 + theme.PageBackground.B * .0722 > 160);
        private bool _onexCatalogPainted;
        private bool _onexThemeQaStore;
        private sealed class OnexSurfaceRole { public int Text; public bool Card, Stroke, AccentStroke; public Color? OriginalAccent,OriginalStrokeAccent; }
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, OnexSurfaceRole> _onexSurfaceRoles =
            new System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, OnexSurfaceRole>();
        private static bool OnexNeutral(Color c) => Math.Max(c.R, Math.Max(c.G,c.B)) - Math.Min(c.R,Math.Min(c.G,c.B)) < 32;
        private static bool OnexSame(Color a, Color b) => a.A==b.A && a.R==b.R && a.G==b.G && a.B==b.B;
        private static bool OnexAccentLike(Color c, ThemeColors previous, ThemeColors theme) =>
            OnexSame(c, theme.EffectiveAccent()) || (previous != null && OnexSame(c, previous.EffectiveAccent())) ||
            (c.R==0 && c.G==200 && c.B==255) ||
            OnexSame(c, OnexPalette.Orange) || OnexSame(c, OnexPalette.Cyan) || OnexSame(c, OnexPalette.Violet) || OnexSame(c, OnexPalette.Magenta);
        private int OnexTextRole(Brush brush, ThemeColors previous, ThemeColors theme)
        {
            if (!(brush is SolidColorBrush solid)) return 0;
            Color c=solid.Color;
            if(c.A==0)return 0;
            if (OnexSame(c,theme.TextSecondary) || (previous!=null && OnexSame(c,previous.TextSecondary))) return 2;
            if (OnexSame(c,theme.TextPrimary) || (previous!=null && OnexSame(c,previous.TextPrimary))) return 1;
            if (OnexNeutral(c)) return (c.R>=205 || c.R<70) ? 1:2;
            return OnexAccentLike(c,previous,theme)?3:0;
        }
        private static void SetOnexThemeBrush(DependencyObject target,DependencyProperty property,Color colour)
        {
            // Property values are immutable colour values. Unlike the private
            // mutable resource roles, equal values can share one UI-thread brush.
            var brush=OnexSolidBrush(colour);
            if(!ReferenceEquals(target.GetValue(property),brush))target.SetValue(property,brush);
        }
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,Dictionary<string,SolidColorBrush>> _onexMutableThemeResources =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control,Dictionary<string,SolidColorBrush>>();
        private void SetOnexThemeResource(Control target,string key,Color colour)
        {
            target.Resources.TryGetValue(key,out var value);
            if(value is SolidColorBrush current&&OnexSame(current.Color,colour))return;
            var owned=_onexMutableThemeResources.GetValue(target,_=>new Dictionary<string,SolidColorBrush>());
            if(!_onexReplayPreviousThemeWork && value is SolidColorBrush mutable && owned.TryGetValue(key,out var prior) && ReferenceEquals(mutable,prior))
            { mutable.Color=colour;return; }
            // Never mutate a shared colour-cache brush or a caller's resource.
            var brush=new SolidColorBrush(colour);target.Resources[key]=brush;owned[key]=brush;
        }
        private int _onexWholeThemePasses;
        private bool IsOnexInactiveThemePage(DependencyObject root)
        {
            if(_onexReplayPreviousThemeWork || !(root is UIElement page) || page.Visibility!=Visibility.Collapsed)return false;
            // Defer only whole navigation pages, never individual collapsed
            // editors. A page receives its palette before navigation can render it.
            return ReferenceEquals(root,QuickSettingsScrollViewer)||ReferenceEquals(root,PerformanceScrollViewer)||
                ReferenceEquals(root,DisplayScrollViewer)||ReferenceEquals(root,FanScrollViewer)||ReferenceEquals(root,LegionScrollViewer)||
                ReferenceEquals(root,SystemScrollViewer)||ReferenceEquals(root,GameScrollViewer)||ReferenceEquals(root,AMDScrollViewer)||
                ReferenceEquals(root,ScalingScrollViewer)||ReferenceEquals(root,GPDScrollViewer)||ReferenceEquals(root,DriverScrollViewer)||
                ReferenceEquals(root,TinyCenterMScrollViewer)||ReferenceEquals(root,OnboardingScrollViewer)||
                ReferenceEquals(root,_onexKeyboardMousePage)||ReferenceEquals(root,_onexLightingPage)||ReferenceEquals(root,OnexOverlayScrollViewer)||
                ReferenceEquals(root,_releaseHome)||ReferenceEquals(root,_releaseOsd);
        }
        private void ApplyOnexCatalogSurfaces(DependencyObject root, ThemeColors theme, ThemeColors previous = null, bool inheritedTheme = false)
        {
            if (root==null || _onexUnusedLegacyContent.TryGetValue(root,out var unused) || (!OnexBrandTheme && !OnexCatalogTheme && !_onexCatalogPainted) || IsOnexInactiveThemePage(root)) return;
            if(DeferOnexCollapsedAppearance(root,false,previous))return;
            long nodeStarted=_onexNavigationPerfCapture?System.Diagnostics.Stopwatch.GetTimestamp():0;
            if(_onexMeasureThemeWork)_onexOwnedThemeNodes++;
            _onexCatalogPainted=true;
            if (root==this)
            {
                _onexWholeThemePasses++;RequestedTheme=OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark;
                SetOnexThemeBrush(this,Control.ForegroundProperty,theme.TextPrimary);
                ((SolidColorBrush)Resources["OnexInactiveSliderTrackBrush"]).Color=OnexInactiveSliderTrack(theme);
            }
            var approved = OnexApprovedTheme.Find(theme.Name);
            if(root is Border glyphPlate&&glyphPlate.Tag as string=="OnexInputGlyphPlate")
            {
                SetOnexThemeBrush(glyphPlate,Border.BackgroundProperty,approved?.Raised??theme.ButtonBackground);
                return;
            }
            var element=root as FrameworkElement;
            string name=element?.Name??"";
            if(_onexStatsValues.TryGetValue("batteryLevel",out var batteryValue)&&ReferenceEquals(root,batteryValue))return;
            if(root is TextBlock tileCaption&&qsTileDefinitions.Any(tile=>ReferenceEquals(tile.NameText,tileCaption)||ReferenceEquals(tile.StateText,tileCaption)))return;
            // The lighting picture and its frame own their visual palette.
            // Its LED shapes are live device values and its hit targets are transparent.
            if (element != null && OnexApprovedTheme.OwnsArtwork(element))
            {
                OnexApprovedTheme.Repaint(element, approved);
                return;
            }
            if (root is Control artTarget && OnexApprovedTheme.OwnsTarget(artTarget)) return;
            if(root is Button swatch && ((swatch.Tag as string)?.StartsWith("#")==true ||
                (swatch.Content==null && swatch.Width<=36 && swatch.Height<=36 && swatch.Background!=null)))return;
            // Colour references, graphs and live previews carry data, not chrome.
            if (root.GetType().Name=="ColorSpectrum" || root.GetType().Name=="ColorPicker" || name.IndexOf("ColorSwatch",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("OverlayPreview",StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("FanCurveChart",StringComparison.OrdinalIgnoreCase)>=0) return;
            ApplyOnexUiFontNode(root);
            var requested=OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark;
            // Set light/dark once at a painted scope. Descendants inherit it,
            // rather than each invalidating a nested resource/theme subtree.
            var desiredTheme=inheritedTheme&&!_onexReplayPreviousThemeWork?ElementTheme.Default:requested;
            if(element!=null&&element.RequestedTheme!=desiredTheme)element.RequestedTheme=desiredTheme;
            var role=_onexSurfaceRoles.GetValue(root, _=>new OnexSurfaceRole());
            if(!role.OriginalAccent.HasValue)
            {
                var colour=root is TextBlock caption?(caption.Foreground as SolidColorBrush)?.Color:
                    root is IconElement glyph?(glyph.Foreground as SolidColorBrush)?.Color:
                    root is Control tint?(tint.Foreground as SolidColorBrush)?.Color:null;
                if(colour.HasValue&&(OnexSame(colour.Value,OnexPalette.Orange)||OnexSame(colour.Value,OnexPalette.Cyan)||OnexSame(colour.Value,OnexPalette.Violet)||OnexSame(colour.Value,OnexPalette.Magenta)))role.OriginalAccent=colour;
            }
            Color semanticAccent=role.OriginalAccent.HasValue?OnexMappedAccent(role.OriginalAccent.Value):theme.EffectiveAccent();
            if (root is TextBlock text && !OnexPowerReadout.GetUsesTrackAccent(text))
            {
                if(OnexPowerReadout.GetIsNeutral(text)){role.Text=1;role.OriginalAccent=null;}
                if(role.Text==0)role.Text=OnexTextRole(text.Foreground,previous,theme);
                if(role.Text!=0)SetOnexThemeBrush(text,TextBlock.ForegroundProperty,role.Text==2?theme.TextSecondary:role.Text==3?(role.OriginalAccent.HasValue?OnexMappedAccent(role.OriginalAccent.Value,true):semanticAccent):theme.TextPrimary);
            }
            if(root is IconElement icon)
            {if(role.Text==0)role.Text=OnexTextRole(icon.Foreground,previous,theme);if(role.Text!=0)SetOnexThemeBrush(icon,IconElement.ForegroundProperty,role.Text==2?theme.TextSecondary:role.Text==3?semanticAccent:theme.TextPrimary);}
            if (root is Border border)
            {
                if (!role.Card && border.CornerRadius.TopLeft>=8 && border.Padding.Left>=8 && border.Background is SolidColorBrush fill)
                    role.Card=OnexNeutral(fill.Color) || OnexSame(fill.Color,theme.CardBackground) ||
                        (previous!=null && (OnexSame(fill.Color,previous.CardBackground)||OnexSame(fill.Color,previous.ButtonBackground)));
                if(role.Card)border.Background=OnexSurface(theme.CardBackground,theme);
                if(border.BorderBrush is SolidColorBrush stroke)
                { if(stroke.Color.A>0 && !role.Stroke && !role.AccentStroke){role.Stroke=OnexNeutral(stroke.Color)||OnexSame(stroke.Color,theme.CardBorder)||OnexSame(stroke.Color,theme.ButtonBorder)||(previous!=null&&(OnexSame(stroke.Color,previous.CardBorder)||OnexSame(stroke.Color,previous.ButtonBorder)));role.AccentStroke=OnexAccentLike(stroke.Color,previous,theme);if(role.AccentStroke&&(OnexSame(stroke.Color,OnexPalette.Orange)||OnexSame(stroke.Color,OnexPalette.Cyan)||OnexSame(stroke.Color,OnexPalette.Violet)||OnexSame(stroke.Color,OnexPalette.Magenta)))role.OriginalStrokeAccent=stroke.Color;} }
                if(role.Stroke||role.AccentStroke)SetOnexThemeBrush(border,Border.BorderBrushProperty,role.AccentStroke?(role.OriginalStrokeAccent.HasValue?OnexMappedAccent(role.OriginalStrokeAccent.Value):theme.EffectiveAccent()):theme.CardBorder);
            }
            if (root is Control control)
            {
                if (control is Button || control is ComboBox || control is ComboBoxItem || control is TextBox)
                { if(!(control is Button) || (control.Background is SolidColorBrush oldFill && oldFill.Color.A>0)||control.Background is LinearGradientBrush)control.Background=OnexSurface(theme.ButtonBackground,theme);
                    if(control.BorderBrush is SolidColorBrush oldStroke && oldStroke.Color.A>0)SetOnexThemeBrush(control,Control.BorderBrushProperty,theme.ButtonBorder);SetOnexThemeBrush(control,Control.ForegroundProperty,theme.TextPrimary); }
                if (control is Slider && !OnexPowerReadout.GetUsesTrackAccent(control))
                {
                    int point = name.LastIndexOf("Point", StringComparison.Ordinal);
                    var track = name.StartsWith("OnexFan", StringComparison.Ordinal) && point >= 0 &&
                        int.TryParse(name.Substring(point + 5), out int degrees)
                        ? OnexFanPointColor(degrees) : theme.EffectiveAccent();
                    SetOnexThemeBrush(control, Control.ForegroundProperty, track);
                }
                if (control is ToggleSwitch || control is CheckBox){SetOnexThemeBrush(control,Control.ForegroundProperty,theme.TextPrimary);if(control is ToggleSwitch)SetOnexThemeBrush(control,Control.BackgroundProperty,theme.EffectiveAccent());}
                if(control is MenuFlyoutItemBase)SetOnexThemeBrush(control,Control.ForegroundProperty,theme.TextPrimary);
                ConfigureOnexFocus(control);
                if(control is ComboBox||control is ToggleSwitch||control is CheckBox||(_onexReplayPreviousThemeWork&&control is ComboBoxItem))
                {
                    SetOnexThemeResourceGroup(control,"accent",OnexControlAccentKeys,theme.EffectiveAccent());
                    SetOnexThemeResourceGroup(control,"accent",new[]{"ToggleSwitchFillOn","ToggleSwitchFillOnPointerOver","ToggleSwitchFillOnPressed","ToggleSwitchStrokeOn","ToggleSwitchStrokeOnPointerOver","ToggleSwitchStrokeOnPressed","CheckBoxCheckBackgroundFillChecked","CheckBoxCheckBackgroundFillCheckedPointerOver","CheckBoxCheckBackgroundFillCheckedPressed","CheckBoxCheckBackgroundStrokeChecked","CheckBoxCheckBackgroundStrokeCheckedPointerOver","CheckBoxCheckBackgroundStrokeCheckedPressed","ComboBoxSelectionIndicatorBrush","ListViewItemSelectionIndicatorBrush"},theme.EffectiveAccent());
                    if(control is ToggleSwitch)SetOnexThemeBrush(control,Control.BackgroundProperty,theme.EffectiveAccent());
                    if(control is CheckBox ownCheck)UpdateOnexOwnedCheck(ownCheck,theme.EffectiveAccent());
                }
            }
            // Closed dropdown rows are styled by their owned container style
            // and realised-item opening hook. Preparing hundreds here blocks a
            // first tab visit even though those rows cannot be seen yet.
            if(element!=null)OnexApprovedTheme.Repaint(element,approved);
            RecordOnexNavigationWork("theme-node",root,nodeStarted);
            if(root is ComboBox closed&&!closed.IsDropDownOpen)return;
            foreach(var child in ThemeContentChildren(root))ApplyOnexCatalogSurfaces(child,theme,previous,true);
            if(root is MenuFlyoutSubItem submenu)foreach(var item in submenu.Items)ApplyOnexCatalogSurfaces(item,theme,previous,true);
        }
        private void ConfigureOnexCatalogFlyout(Windows.UI.Xaml.Controls.Primitives.FlyoutBase flyout)
        {
            if(!OnexCatalogTheme||_onexAppliedTheme==null)return;
            var theme=_onexAppliedTheme;
            if(flyout is MenuFlyout menu)
            {
                var style=new Style(typeof(MenuFlyoutPresenter));
                style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(theme.CardBackground)));
                style.Setters.Add(new Setter(Control.ForegroundProperty,new SolidColorBrush(theme.TextPrimary)));
                style.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(theme.CardBorder)));
                style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty,OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark));
                menu.MenuFlyoutPresenterStyle=style;
                foreach(var item in menu.Items)ApplyOnexCatalogSurfaces(item,theme);
            }
            else if(flyout is Flyout content && content.Content!=null)ApplyOnexCatalogSurfaces(content.Content,theme);
        }
    }
}
