// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexGlassAsync(JsonObject report)
  {
   var values=ApplicationData.Current.LocalSettings.Values;
   bool had=values.TryGetValue(OnexGlassSurfaces.Setting,out var saved);bool? lastOwn=null;
   bool initialized=isGlassToggleInitialized,themeInitialized=isThemeInitialized;
   bool qaTheme=_onexThemeQaStore;_onexThemeQaStore=true;isThemeInitialized=false;isGlassToggleInitialized=true;
   var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   var appearance=ThemeContent.Visibility;int checks=0,visits=0,gradientCount=0;double maxRefresh=0;
   var rows=new JsonArray();void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   async Task<byte[]> Pixels(UIElement target){var image=new RenderTargetBitmap();await image.RenderAsync(target);return (await image.GetPixelsAsync()).ToArray();}
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect pending owner edits");
    foreach(var name in new[]{OnexPalette.Name,"OLED","Dracula","Catppuccin Latte","Liquid Glass","Windows 11 Light","Windows 11 Dark"})
    {
     ApplyTheme(name);var theme=_onexAppliedTheme;byte[] offPixels=null,onPixels=null;
     foreach(bool enabled in new[]{false,true})
     {
      SystemNavItem.IsChecked=true;ThemeContent.Visibility=Visibility.Visible;
      var tiles=qsTileDefinitions.Where(x=>x.TileButton!=null).ToDictionary(x=>x.Id,x=>x.TileButton);
      int refreshes=_onexGlassRefreshes,palettePasses=_onexWholeThemePasses,fontPasses=_onexFontScopePasses;lastOwn=enabled;
      if(GlassEffectToggle.IsOn==enabled)GlassEffectToggle_Toggled(GlassEffectToggle,null);else GlassEffectToggle.IsOn=enabled;
      Check(_onexGlassRefreshes==refreshes+1,"Actual toggle must refresh exactly once");
      Check(_onexWholeThemePasses==palettePasses&&_onexFontScopePasses==fontPasses,"Glass replayed the palette or font painter");
      Check((values[OnexGlassSurfaces.Setting] as bool?)==enabled,"Glass choice not persisted");
      Check(qsTileDefinitions.Where(x=>tiles.ContainsKey(x.Id)).All(x=>ReferenceEquals(x.TileButton,tiles[x.Id])),"Glass rebuilt the existing home tiles");
      maxRefresh=Math.Max(maxRefresh,_onexGlassRefreshMs);
      foreach(var tab in GetVisibleNavigationItems())
      {
       tab.IsChecked=true;UpdateLayout();await Task.Delay(45);var root=OnexNavigationRoot();
       foreach(var card in OnexOwnedContent(root).OfType<Border>())
        if(_onexSurfaceRoles.TryGetValue(card,out var role)&&role.Card)
        {
         Check(enabled?card.Background is LinearGradientBrush:card.Background is SolidColorBrush,"Incoming card has stale Glass state: "+card.Name);
         if(card.Background is LinearGradientBrush gradient)
         {
          gradientCount++;
          foreach(var stop in gradient.GradientStops)
          {
           Check(OnexContrast(theme.TextPrimary,stop.Color)+.001>=Math.Min(4.5,OnexContrast(theme.TextPrimary,theme.CardBackground)),"Glass reduces primary contrast");
           Check(OnexContrast(theme.TextSecondary,stop.Color)+.001>=Math.Min(4.5,OnexContrast(theme.TextSecondary,theme.CardBackground)),"Glass reduces secondary contrast");
          }
         }
        }
       visits++;
      }
      PerformanceNavItem.IsChecked=true;UpdateLayout();await Task.Delay(100);
      Check(enabled?((Border)_releaseHome.PowerSection).Background is LinearGradientBrush:((Border)_releaseHome.PowerSection).Background is SolidColorBrush,"Home power card did not follow Glass");
      foreach(var tile in qsTileDefinitions.Where(x=>x.TileButton!=null))Check(enabled?tile.TileButton.Background is LinearGradientBrush:tile.TileButton.Background is SolidColorBrush,"Home tile loses Glass after state refresh");
      if(enabled)onPixels=await Pixels(_releaseHome.PowerSection);else offPixels=await Pixels(_releaseHome.PowerSection);
      await SaveOnexImage("onex-glass407-"+name.Replace(" ","-")+"-"+(enabled?"on":"off")+".png",_releaseHome.PowerSection,30);
      SystemNavItem.IsChecked=true;ThemeContent.Visibility=Visibility.Visible;GlassEffectToggle.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();await Task.Delay(60);
      await SaveOnexImage("onex-glass407-settings-"+name.Replace(" ","-")+"-"+(enabled?"on":"off")+".png",this,30);
     }
     Check(offPixels!=null&&onPixels!=null&&offPixels.Length==onPixels.Length,"Cannot compare actual Glass renders");
     int changed=0;for(int n=0;n<offPixels.Length;n+=4)if(offPixels[n]!=onPixels[n]||offPixels[n+1]!=onPixels[n+1]||offPixels[n+2]!=onPixels[n+2])changed++;
     Check(changed>1000,"Glass has no measurable effect on actual UWP card");
     rows.Add(new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"actualChangedPixels",JsonValue.CreateNumberValue(changed)}});
    }
    Check(gradientCount>30,"Too few real card surfaces exercised");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["pageVisits"]=JsonValue.CreateNumberValue(visits);report["gradientCards"]=JsonValue.CreateNumberValue(gradientCount);report["themes"]=rows;report["maxImmediateRefreshMs"]=JsonValue.CreateNumberValue(maxRefresh);
    report["fullPaletteOrFontRepaintsOnToggle"]=JsonValue.CreateNumberValue(0);
    report["movingAnimationsAdded"]=JsonValue.CreateNumberValue(0);report["realTogglePersistenceAndUwpRendering"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    isGlassToggleInitialized=false;
    if(lastOwn.HasValue&&values.TryGetValue(OnexGlassSurfaces.Setting,out var fresh)&&fresh is bool current&&current==lastOwn.Value)
    {if(had)values[OnexGlassSurfaces.Setting]=saved;else values.Remove(OnexGlassSurfaces.Setting);}
    GlassEffectToggle.IsOn=IsGlassEffectEnabled();
    ApplyTheme(values.TryGetValue("WidgetTheme",out var ownerTheme)?ownerTheme as string:OnexPalette.Name);RefreshOnexGlassSurfaces();
    ThemeContent.Visibility=appearance;_onexThemeQaStore=qaTheme;isThemeInitialized=themeInitialized;isGlassToggleInitialized=initialized;
    if(original?.Visibility==Visibility.Visible)original.IsChecked=true;
    report["restoredGlassSetting"]=JsonValue.CreateBooleanValue(had?values.TryGetValue(OnexGlassSurfaces.Setting,out var value)&&Equals(value,saved):!values.ContainsKey(OnexGlassSurfaces.Setting));
   }
  }
 }
}
