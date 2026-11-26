using System.Linq;
using System;
// SPDX-License-Identifier: AGPL-3.0-only
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml;
using System.Collections.Generic;
namespace XboxGamingBar
{
    internal static class OnexPalette
    {
        internal const string Name="ONEXPLAYER";
        // sRGB colours sampled from the owner's official-site reference (ICC converted).
        internal static readonly Color Orange=Color.FromArgb(255,248,72,9),Cyan=Color.FromArgb(255,0,226,227),Violet=Color.FromArgb(255,165,105,255),Magenta=Color.FromArgb(255,234,15,246);
        internal static readonly Color Page=Color.FromArgb(255,8,10,14),Card=Color.FromArgb(255,24,28,35),Border=Color.FromArgb(255,53,60,71),Button=Color.FromArgb(255,34,39,48),Secondary=Color.FromArgb(255,160,160,160);
        internal static ThemeColors CreateTheme()=>new ThemeColors{Name=Name,PageBackground=Page,CardBackground=Card,CardBorder=Border,AccentColor=Orange,TextPrimary=Colors.White,TextSecondary=Secondary,ButtonBackground=Button,ButtonBorder=Border,TileOff=Card,TileOn=Card,GlowColor=Orange,TileIcon=Colors.White,MetricsBackground=Color.FromArgb(255,18,23,30),MetricsBorder=Border,ShimmerEnabled=false};
    }
    public sealed partial class GamingWidget
    {
  private static double OnexLuminance(Color c)
  {double F(byte v){double x=v/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}return F(c.R)*.2126+F(c.G)*.7152+F(c.B)*.0722;}
  private static double OnexContrast(Color a,Color b)
  {double x=OnexLuminance(a),y=OnexLuminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
        private ThemeColors _onexAppliedTheme;
        private readonly Dictionary<string,object> _onexBaseAccentResources=new Dictionary<string,object>();
        private static readonly string[] OnexControlAccentKeys={"SystemControlHighlightAccentBrush","SystemControlForegroundAccentBrush","AccentFillColorDefaultBrush","AccentFillColorSecondaryBrush","AccentFillColorTertiaryBrush","ComboBoxSelectionIndicatorForeground","ComboBoxSelectionIndicatorForegroundPointerOver","ComboBoxSelectionIndicatorForegroundPressed"};
        private void ApplyOnexControlAccents()
        {
            if(!OnexBrandTheme)
            {foreach(var pair in _onexBaseAccentResources){if(pair.Value==null)Resources.Remove(pair.Key);else Resources[pair.Key]=pair.Value;}_onexBaseAccentResources.Clear();return;}
            foreach(string key in OnexControlAccentKeys)
            {if(!_onexBaseAccentResources.ContainsKey(key))_onexBaseAccentResources[key]=Resources.TryGetValue(key,out var original)?original:null;Resources[key]=new SolidColorBrush(OnexPalette.Orange);}
        }
        private void ApplyOnexDialogAccents(Windows.UI.Xaml.Controls.ContentDialog dialog)
        {
            // Native footer buttons are template parts, outside public Content.
            // Pin the same owned style/focus after their template has loaded.
            dialog.Loaded+=(s,e)=>{
                foreach(string name in new[]{"PrimaryButton","SecondaryButton","CloseButton"}){
                    var button=FindDescendantByName(dialog,name) as Windows.UI.Xaml.Controls.Button;
                    var style=name=="PrimaryButton"?dialog.PrimaryButtonStyle:name=="SecondaryButton"?dialog.SecondaryButtonStyle:dialog.CloseButtonStyle;
                    if(button==null||style==null)continue;
                    button.Style=style;
                    if(dialog.Content is Windows.UI.Xaml.Controls.ScrollViewer view&&view.Content is Windows.UI.Xaml.Controls.Grid host&&_onexFocusHosts.TryGetValue(host,out var scope)){
                        _onexControlLayers.Remove(button);_onexControlLayers.Add(button,scope);
                    }
                    ConfigureOnexFocus(button);button.ApplyTemplate();
                    if(ReferenceEquals(Windows.UI.Xaml.Input.FocusManager.GetFocusedElement(),button))FocusOnexOwnedControl(button);
                }
            };
            if(dialog.Content is Windows.UI.Xaml.Controls.ScrollViewer viewport&&viewport.Content is UIElement content){viewport.Content=null;viewport.Content=WrapOnexFocusContent(content);}
            foreach(var element in OnexOwnedContent(dialog))if(element is Windows.UI.Xaml.Controls.Control focusControl)ConfigureOnexFocus(focusControl);
            if(_onexAppliedTheme!=null)
            {
                var theme=_onexAppliedTheme;
                dialog.RequestedTheme=OnexLight(theme)?Windows.UI.Xaml.ElementTheme.Light:Windows.UI.Xaml.ElementTheme.Dark;
                dialog.Background=new SolidColorBrush(theme.CardBackground);dialog.Foreground=new SolidColorBrush(theme.TextPrimary);
                foreach(string key in OnexControlAccentKeys)dialog.Resources[key]=new SolidColorBrush(theme.EffectiveAccent());
                dialog.Resources["ContentDialogBackground"]=new SolidColorBrush(theme.CardBackground);
                dialog.Resources["ContentDialogForeground"]=new SolidColorBrush(theme.TextPrimary);
                dialog.Loaded+=(s,e)=>ApplyOnexCatalogSurfaces(dialog,theme);
                ApplyOnexCatalogSurfaces(dialog,theme);return;
            }
            if(!OnexBrandTheme)return;
            foreach(string key in OnexControlAccentKeys)dialog.Resources[key]=new SolidColorBrush(OnexPalette.Orange);
            foreach(string suffix in new[]{"","PointerOver","Pressed"})
            {dialog.Resources["AccentButtonBackground"+suffix]=new SolidColorBrush(OnexPalette.Orange);dialog.Resources["AccentButtonForeground"+suffix]=OnexSettingsBrush(12,18,24);dialog.Resources["AccentButtonBorderBrush"+suffix]=new SolidColorBrush(OnexPalette.Orange);}
            dialog.Resources["AccentButtonStyle"]=dialog.PrimaryButtonStyle;
            dialog.Resources["ContentDialogTopOverlay"]=new SolidColorBrush(Colors.Transparent);
        }
        private bool OnexBrandTheme=>currentThemeName==OnexPalette.Name;
        private SolidColorBrush OnexDialogSurface=>OnexCatalogTheme&&_onexAppliedTheme!=null?OnexSolidBrush(_onexAppliedTheme.CardBackground):OnexBrandTheme?OnexSolidBrush(OnexPalette.Card):OnexSettingsBrush(29,33,39);
        private SolidColorBrush OnexDialogBorder=>OnexCatalogTheme&&_onexAppliedTheme!=null?OnexSolidBrush(_onexAppliedTheme.CardBorder):OnexBrandTheme?OnexSolidBrush(OnexPalette.Border):OnexSettingsBrush(67,81,94);
        private SolidColorBrush OnexDialogAccent=>OnexCatalogTheme&&_onexAppliedTheme!=null?OnexSolidBrush(_onexAppliedTheme.EffectiveAccent()):OnexBrandTheme?OnexSolidBrush(OnexPalette.Orange):OnexSettingsBrush(0,200,255);
        private string _onexPalettePhase="",_onexPaletteFailurePhase="",_onexPaletteFailureType="",_onexPaletteFailureMethods="";
        private int _onexPaletteFailureHresult;
        private void ApplyOnexPalette(ThemeColors theme)
        {
            _onexPaletteFailureHresult=0;_onexPaletteFailurePhase="";_onexPaletteFailureType="";_onexPaletteFailureMethods="";_onexRestoreScopeType="";_onexRestoreKey="";_onexRestoreOperation="";_onexRestoreOriginalType="";_onexRestoreKeyLocal=false;_onexRestoreLocalCount=0;
            try
            {
            var previous=_onexAppliedTheme;_onexAppliedTheme=theme;_onexThemeEpoch++;_onexPageThemeEpochs.Clear();
            _onexPalettePhase="restore-scoped-resources";if(!OnexBrandTheme)RestoreOnexScopedResources();
            _onexPalettePhase="catalog-surfaces";ApplyOnexCatalogSurfaces(this,theme,previous);
            _onexPalettePhase="control-accents";ApplyOnexControlAccents();
            _onexPalettePhase="home-palette";_releaseHome?.ApplyPalette(theme);
            if(OnexBrandTheme)
            {
                RailBrightnessSlider.Foreground=new SolidColorBrush(OnexPalette.Cyan);RailVolumeSlider.Foreground=new SolidColorBrush(OnexPalette.Cyan);RailBrightnessIcon.Foreground=new SolidColorBrush(OnexPalette.Cyan);RailMuteButton.Foreground=new SolidColorBrush(OnexPalette.Cyan);
            }
            else
            {
                var accent=new SolidColorBrush(OnexRoleAccent(1));RailBrightnessSlider.Foreground=accent;RailVolumeSlider.Foreground=accent;RailBrightnessIcon.Foreground=accent;RailMuteButton.Foreground=accent;
            }
            if(OnexStartupToggle!=null)SetOnexThemeBrush(OnexStartupToggle,Windows.UI.Xaml.Controls.Control.BackgroundProperty,OnexRoleAccent(1));
            _onexPalettePhase="media-visuals";ApplyOnexBrightnessVisual();
            ApplyMuteVisual(_onexMediaState?.GetNamedBoolean("muted",false)??false);
            ApplyOnexMicrophoneVisual();
            _onexPalettePhase="navigation-pills";foreach(var nav in GetVisibleNavigationItems())ApplyNavPillTheme(nav);
            _onexPalettePhase="tile-availability";ApplyOnexTileAvailability();
            _onexPalettePhase="tab-palette";ApplyOnexTabPalette();
            if(_onexKeyboardMousePage?.Visibility==Visibility.Visible)
            {
                RefreshKbmMapping();
                if(IsOnexRemapOpen(_kbmFlyout))PaintKbmEditorLayers(ControllerText(KbmPolicy,"fnButton")==_kbmSource);
            }
            _onexPalettePhase="home-final";_releaseHome?.ApplyPalette(theme,true);
            _onexPalettePhase="page-focus";ApplyOnexPageFocus();
            _onexPalettePhase="stats";UpdateOnexStatsDisplay();
            _onexPalettePhase="hints";ApplyOnexHintContrast(theme);
            // A support title is semantic primary text even while its About
            // section/page is deferred. Update this one known field here so
            // palette changes cannot leave a hidden caption with stale ink.
            _onexPalettePhase="support-title";if(OnexSupportAuthorCaption!=null)SetOnexThemeBrush(OnexSupportAuthorCaption,Windows.UI.Xaml.Controls.TextBlock.ForegroundProperty,theme.TextPrimary);
            var current=OnexNavigationRoot();
            if(current!=null&&!_onexReplayPreviousThemeWork)_onexPageThemeEpochs[current]=_onexThemeEpoch;
            }
            catch(Exception error)
            {
                _onexPaletteFailurePhase=_onexPalettePhase;_onexPaletteFailureHresult=error.HResult;_onexPaletteFailureType=error.GetType().Name;
                if(App.OnexTileRevisionStage==209)
                {
                    try{var frames=new System.Diagnostics.StackTrace(error,false).GetFrames();_onexPaletteFailureMethods=string.Join(";",frames==null?new string[0]:frames.Take(6).Select(frame=>{var method=frame.GetMethod();return(method?.DeclaringType?.FullName??"")+"."+(method?.Name??"");}));}catch{}
                }
                throw;
            }

        }
    }
}
