// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    // Static, cached glass finish: no recurring animation, backdrop permission or
    // private control-template walk. Keep data visualisations and input tracks flat.
    internal sealed class OnexGlassSurfaces
    {
        internal const string Setting = "GlassEffectEnabled";
        internal static bool Enabled
        {
            get { var values=Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                return !values.TryGetValue(Setting,out var value)||!(value is bool enabled)||enabled; }
        }
        // A cache belongs to a page. Game Bar can destroy and replace a view
        // while keeping the process (and its managed static fields) alive.
        private readonly Dictionary<(Color Fill,Color Primary,Color Secondary,bool Enabled),Brush> cache=
            new Dictionary<(Color,Color,Color,bool),Brush>();
        internal Brush Get(Color colour,ThemeColors theme,bool enabled)
        {
            var key=(colour,theme.TextPrimary,theme.TextSecondary,enabled);
            if(cache.TryGetValue(key,out var cached))return cached;
            if(cache.Count>=256)cache.Clear();
            return cache[key]=Create(colour,theme,enabled);
        }
        internal static Brush Create(Color colour,ThemeColors theme,bool enabled)
        {
            if(!enabled)return new SolidColorBrush(colour);
            var stops=GlassColours(colour,theme);
            var brush=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(0,1)};
            brush.GradientStops.Add(new GradientStop{Offset=0,Color=colour});
            brush.GradientStops.Add(new GradientStop{Offset=.12,Color=stops.Edge});
            brush.GradientStops.Add(new GradientStop{Offset=.35,Color=stops.Shoulder});
            brush.GradientStops.Add(new GradientStop{Offset=.60,Color=colour});
            brush.GradientStops.Add(new GradientStop{Offset=1,Color=colour});
            return brush;
        }
        internal static (Color Edge,Color Shoulder) GlassColours(Color colour,ThemeColors theme)
        {
            bool light=Luminance(colour)>.45;
            double strength=light?-.065:.14;
            Color edge=ThemeColors.Shade(colour,strength);
            // Do not make either ink less readable than its existing surface.
            double primary=Math.Min(4.5,Contrast(theme.TextPrimary,colour));
            double secondary=Math.Min(4.5,Contrast(theme.TextSecondary,colour));
            for(int n=0;n<18&&(Contrast(theme.TextPrimary,edge)<primary||Contrast(theme.TextSecondary,edge)<secondary);n++)
                edge=ThemeColors.Shade(colour,strength*=.8);
            return (edge,ThemeColors.Shade(colour,strength*.32));
        }
        private static double Luminance(Color colour)
        {double C(byte v){double x=v/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}return .2126*C(colour.R)+.7152*C(colour.G)+.0722*C(colour.B);}
        private static double Contrast(Color a,Color b)
        {double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
    }
    public sealed partial class GamingWidget
    {
        private int _onexGlassRefreshes;
        private double _onexGlassRefreshMs;
        private readonly OnexGlassSurfaces _onexGlassSurfaces=new OnexGlassSurfaces();
        private Brush OnexSurface(Color colour,ThemeColors theme=null)=>_onexGlassSurfaces.Get(colour,theme??_onexAppliedTheme??OnexPalette.CreateTheme(),IsGlassEffectEnabled());
        // The palette, fonts, focus templates and live device state have not
        // changed. Updating only fills avoids replaying their expensive painters.
        private void ApplyOnexGlassOnly(DependencyObject root,ThemeColors theme)
        {
            if(root==null||IsOnexInactiveThemePage(root))return;
            var element=root as FrameworkElement;string name=element?.Name??"";
            if(element!=null&&OnexApprovedTheme.OwnsArtwork(element))return;
            if(root is Control artTarget&&OnexApprovedTheme.OwnsTarget(artTarget))return;
            if(root is Border plate&&plate.Tag as string=="OnexInputGlyphPlate")return;
            if(root.GetType().Name=="ColorSpectrum"||root.GetType().Name=="ColorPicker"||
                name.IndexOf("ColorSwatch",StringComparison.OrdinalIgnoreCase)>=0||
                name.IndexOf("OverlayPreview",StringComparison.OrdinalIgnoreCase)>=0||
                name.IndexOf("FanCurveChart",StringComparison.OrdinalIgnoreCase)>=0)return;
            if(root is Button swatch&&((swatch.Tag as string)?.StartsWith("#")==true||
                (swatch.Content==null&&swatch.Width<=36&&swatch.Height<=36&&swatch.Background!=null)))return;
            if(root is Border card&&_onexSurfaceRoles.TryGetValue(card,out var role)&&role.Card)
                card.Background=OnexSurface(theme.CardBackground,theme);
            if(root is Control control&&(control is Button||control is ComboBox||control is ComboBoxItem||control is TextBox||
                control is ToggleButton&&!(control is CheckBox)&&!(control is RadioButton))&&
                (control.Background is LinearGradientBrush||control.Background is SolidColorBrush fill&&fill.Color.A>0))
                control.Background=OnexSurface(theme.ButtonBackground,theme);
            if(root is ComboBoxItem choice)ConfigureOnexChoiceColours(choice);
            foreach(var child in ThemeContentChildren(root))ApplyOnexGlassOnly(child,theme);
        }
        private void RefreshOnexGlassSurfaces()
        {
            var theme=_onexAppliedTheme;if(theme==null)return;
            var watch=System.Diagnostics.Stopwatch.StartNew();
            _onexThemeEpoch++;_onexPageThemeEpochs.Clear();
            ApplyOnexGlassOnly(this,theme);
            _releaseHome?.ApplyPalette(theme,true);
            var tileBrush=OnexSurface(theme.TileOff,theme);
            foreach(var tile in qsTileDefinitions)if(tile.TileButton!=null)tile.TileButton.Background=tileBrush;
            ApplyOnexConnectedCardOutlines();
            var current=OnexNavigationRoot();if(current!=null)_onexPageThemeEpochs[current]=_onexThemeEpoch;
            _onexGlassRefreshes++;_onexGlassRefreshMs=watch.Elapsed.TotalMilliseconds;
        }
    }
}
