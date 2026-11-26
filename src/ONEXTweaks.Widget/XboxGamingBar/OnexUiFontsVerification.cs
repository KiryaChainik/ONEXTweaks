using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private IEnumerable<DependencyObject> OnexFontPublicNodes(DependencyObject node)
  {
   if(node==null)return Enumerable.Empty<DependencyObject>();
   if(node is FrameworkElement e&&e.Name.IndexOf("OverlayPreview",StringComparison.OrdinalIgnoreCase)>=0)return Enumerable.Empty<DependencyObject>();
   return new[]{node}.Concat(ThemeContentChildren(node).SelectMany(OnexFontPublicNodes));
  }
  private async Task VerifyOnexUiFontsAsync(JsonObject report)
  {
   int checks=0,pageVisits=0,textSamples=0,controlSamples=0,warmVisits=0;double maxFontPassMs=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;
   bool hadFont=values.TryGetValue(AppFontSettingKey,out var raw);string savedFont=raw as string??DefaultAppFont;
   string savedTheme=values.TryGetValue("WidgetTheme",out var themeRaw)?themeRaw as string:OnexPalette.Name;
   bool fontReady=isAppFontInitialized,themeReady=isThemeInitialized;string lastOwnFont=null;
   var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   var previousAppearance=ThemeContent.Visibility;
   bool diagnostics=App.OnexTileRevisionStage==176||App.OnexTileRevisionStage==178;var mismatches=new JsonArray();
   var metrics=new JsonArray();var hashes=new HashSet<string>();var widths=new Dictionary<string,string>();
   var iconFonts=OnexFontPublicNodes(this).OfType<FontIcon>().ToDictionary(x=>x,x=>x.FontFamily.Source);
   void InspectFont(DependencyObject node,Control control,TextBlock text,RadioButton tab,FrameworkElement root)
   {
    string actual=text?.FontFamily.Source??control?.FontFamily.Source;
    if(actual==FontFamily.Source)return;
    var path=new JsonArray();DependencyObject current=node;bool hidden=false;
    for(int depth=0;current!=null&&depth<14;depth++)
    {
     var element=current as FrameworkElement;hidden|=element?.Visibility==Visibility.Collapsed;
     path.Add(new JsonObject{{"type",JsonValue.CreateStringValue(current.GetType().Name)},{"name",JsonValue.CreateStringValue(element?.Name??"")},
      {"font",JsonValue.CreateStringValue(current is TextBlock t?t.FontFamily.Source:current is Control c?c.FontFamily.Source:"")},
      {"visibility",JsonValue.CreateStringValue(element?.Visibility.ToString()??"")}});
     current=element?.Parent;
    }
    var property=text!=null?TextBlock.FontFamilyProperty:Control.FontFamilyProperty;
    object local=node.ReadLocalValue(property);
    var item=new JsonObject{{"tab",JsonValue.CreateStringValue(tab.Tag as string??"")},{"actual",JsonValue.CreateStringValue(actual??"")},
     {"path",path},{"hiddenAncestor",JsonValue.CreateBooleanValue(hidden)},{"localValueType",JsonValue.CreateStringValue(ReferenceEquals(local,DependencyProperty.UnsetValue)?"Unset":local?.GetType().Name??"null")},
     {"rootType",JsonValue.CreateStringValue(root?.GetType().Name??"")},{"rootName",JsonValue.CreateStringValue(root?.Name??"")},
     {"homeFont",JsonValue.CreateStringValue(_releaseHome?.FontFamily.Source??"")}};
    ApplyOnexUiFonts(root);
    item["afterExplicitRootPaint"]=JsonValue.CreateStringValue(text?.FontFamily.Source??control?.FontFamily.Source??"");
    mismatches.Add(item);
   }

   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect pending owner edits");
    Check(AppFontComboBox.Items.OfType<ComboBoxItem>().Count(x=>(x.Tag as string)?.StartsWith("Bahnschrift")==true)<=3,"Bahnschrift catalogue not reduced");
    Check(OnexUiTypefaces.Count(x=>x.Name.StartsWith("Bahnschrift"))==2,"Expected two distinct normal Bahnschrift choices");
    foreach(var font in OnexUiTypefaces.Where(x=>x.Asset!=null))
    {
     var file=await StorageFile.GetFileFromApplicationUriAsync(new Uri(font.Asset));var info=await file.GetBasicPropertiesAsync();
     Check(info.Size>100000,"Packaged font missing or truncated: "+font.Name);
     var license=await StorageFile.GetFileFromApplicationUriAsync(new Uri(font.Asset.Substring(0,font.Asset.LastIndexOf('/')+1)+"OFL.txt"));
     Check((await FileIO.ReadTextAsync(license)).Contains("OPEN FONT LICENSE"),"Bundled font licence missing");
    }
    isThemeInitialized=false;isAppFontInitialized=true;
    int index=0;
    foreach(var face in OnexUiTypefaces)
    {
     var choice=AppFontComboBox.Items.OfType<ComboBoxItem>().Single(x=>x.Tag as string==face.Name);
     // Exercise the real selection handler and the real local setting. Restore
     // only our own last value in finally; never overwrite an owner's new choice.
     if(ReferenceEquals(AppFontComboBox.SelectedItem,choice))AppFontComboBox.SelectedItem=null;
     lastOwnFont=face.Name;AppFontComboBox.SelectedItem=choice;
     await _onexUiFontApplyTask;
     if(_onexUiFontLastError!=null)throw new InvalidOperationException("Actual font apply exception",_onexUiFontLastError);
     Check((values[AppFontSettingKey] as string)==face.Name,"Font choice not persisted");
     Check(FontFamily.Source==ResolveOnexUiFont(face.Name).Source,"Selected font not applied");
     var row=new JsonObject{{"name",JsonValue.CreateStringValue(face.Name)}};
     foreach(var sample in new[]{"Hamburgefontsiv 0123456789","Съешь ещё этих мягких булок. Ёё"})
     {
      var probe=new TextBlock{Text=sample,FontFamily=ResolveOnexUiFont(face.Name),FontSize=24};probe.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
      Check(probe.DesiredSize.Width>150,"Font probe did not lay out");row[sample[0]=='H'?"latinWidth":"russianWidth"]=JsonValue.CreateNumberValue(probe.DesiredSize.Width);
     }
     widths[face.Name]=row["latinWidth"].Stringify()+"/"+row["russianWidth"].Stringify();
     foreach(var tab in GetVisibleNavigationItems())
     {
      tab.IsChecked=true;UpdateLayout();await Task.Delay(60);var root=OnexNavigationRoot();
      foreach(var node in OnexFontPublicNodes(root))
      {
       if(node is TextBlock text&&(diagnostics||text.IsLoaded&&text.Visibility==Visibility.Visible)&&!OnexIsUiIconOrMonospace(text.FontFamily))
       {if(diagnostics)InspectFont(node,null,text,tab,root);else Check(text.FontFamily.Source==FontFamily.Source,"Public text retained old font: "+text.Name+"; tab="+tab.Tag+"; actual="+text.FontFamily.Source+"; expected="+FontFamily.Source+"; parent="+(node as FrameworkElement)?.Parent?.GetType().Name);textSamples++;}
       else if(node is Control control&&(diagnostics||control.IsLoaded&&control.Visibility==Visibility.Visible)&&!(control is ComboBoxItem)&&!OnexIsUiIconOrMonospace(control.FontFamily)&&
        (control is TextBox||control is ComboBox||control is MenuFlyoutItem||
         control is ToggleSwitch toggle&&toggle.Header is string||
         control is Slider slider&&slider.Header is string||
         control is ContentControl content&&content.Content is string))
       {if(diagnostics)InspectFont(node,control,null,tab,root);else Check(control.FontFamily.Source==FontFamily.Source,"Public control retained old font: "+control.GetType().Name+"/"+control.Name+"; tab="+tab.Tag+"; actual="+control.FontFamily.Source+"; expected="+FontFamily.Source+"; parent="+(node as FrameworkElement)?.Parent?.GetType().Name);controlSamples++;}
      }
      maxFontPassMs=Math.Max(maxFontPassMs,_onexLastFontPassMs);pageVisits++;
     }
     if(!diagnostics)
     {
      int coldPasses=_onexFontScopePasses;
      for(int lap=0;lap<2;lap++)foreach(var tab in GetVisibleNavigationItems())
      {tab.IsChecked=true;UpdateLayout();Check(_onexFontScopePasses==coldPasses,"Warm tab repeats whole font walk");warmVisits++;}
     }
     foreach(var theme in new[]{OnexPalette.Name,"Windows 11 Light"})
     {
      ApplyTheme(theme);SystemNavItem.IsChecked=true;ThemeContent.Visibility=Visibility.Visible;UpdateLayout();
      AppFontSampleCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();await Task.Delay(100);
      Check(AppFontSampleCard.Background is SolidColorBrush bg&&bg.Color.A==255,"Font capture must include an opaque actual card");
      Check(AppFontSample.Foreground is SolidColorBrush ink&&OnexContrast(ink.Color,((SolidColorBrush)AppFontSampleCard.Background).Color)>=4.5,"Font sample lacks light/dark contrast");
      Check(AppFontSample.FontFamily.Source==FontFamily.Source,"Sample did not adopt chosen typeface");
      if(theme==OnexPalette.Name)
      {
       var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(AppFontSample);var pixels=(await bitmap.GetPixelsAsync()).ToArray();
       ulong hash=14695981039346656037UL;foreach(byte b in pixels){hash^=b;hash=unchecked(hash*1099511628211UL);}
       string fingerprint=hash.ToString("X16");hashes.Add(fingerprint);row["actualGlyphRaster"]=JsonValue.CreateStringValue(fingerprint);
      }
      await SaveOnexImage("onex-font404-"+index+"-"+(theme==OnexPalette.Name?"dark":"light")+".png",AppFontSampleCard,30);
     }
     metrics.Add(row);index++;
     if(diagnostics)break;report["completedFonts"]=JsonValue.CreateNumberValue(index);
     var progress=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-font-progress.json",CreationCollisionOption.ReplaceExisting);await FileIO.WriteTextAsync(progress,report.Stringify());
    }
    if(!diagnostics)foreach(var face in OnexUiTypefaces.Where(x=>x.Asset!=null))Check(widths[face.Name]!=widths["Segoe UI"],"Packaged font fell back to Segoe UI: "+face.Name);
    if(!diagnostics)Check(hashes.Count==OnexUiTypefaces.Length,"Typeface samples collapse to the same fallback raster");
    foreach(var icon in iconFonts)Check(icon.Key.FontFamily.Source==icon.Value,"Icon font was replaced");
    report["diagnosticOnly"]=JsonValue.CreateBooleanValue(diagnostics);report["fontMismatches"]=mismatches;
    report["checks"]=JsonValue.CreateNumberValue(checks);report["fonts"]=metrics;report["pageVisits"]=JsonValue.CreateNumberValue(pageVisits);report["warmTabVisitsWithoutFontWalk"]=JsonValue.CreateNumberValue(warmVisits);report["maxActiveFontPassMs"]=JsonValue.CreateNumberValue(maxFontPassMs);
    report["textSamples"]=JsonValue.CreateNumberValue(textSamples);report["controlSamples"]=JsonValue.CreateNumberValue(controlSamples);report["uniqueActualGlyphRasters"]=JsonValue.CreateNumberValue(hashes.Count);
    report["realSelectionAndPersistence"]=JsonValue.CreateBooleanValue(true);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    _onexUiFontRequestEpoch++;
    isAppFontInitialized=false;isThemeInitialized=false;
    if((values.TryGetValue(AppFontSettingKey,out var fresh)?fresh as string:null)==lastOwnFont)
    {if(hadFont)values[AppFontSettingKey]=raw;else values.Remove(AppFontSettingKey);}
    string restore=values.TryGetValue(AppFontSettingKey,out var now)?now as string:DefaultAppFont;
    ApplyAppFont(restore);AppFontComboBox.SelectedItem=AppFontComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>x.Tag as string==restore);
    ApplyTheme(values.TryGetValue("WidgetTheme",out var freshTheme)?freshTheme as string:savedTheme);
    ThemeContent.Visibility=previousAppearance;isAppFontInitialized=fontReady;isThemeInitialized=themeReady;
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
