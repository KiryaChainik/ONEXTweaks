using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexUiTypeface
  {
   internal string Name, Source, Asset;
   internal OnexUiTypeface(string name,string folder=null,string family=null)
   {
    Name=name;Asset=folder==null?null:"ms-appx:///Assets/Fonts/"+folder+"/"+folder+"Regular.ttf";
    Source=Asset==null?name:Asset+"#"+family;
   }
  }
  private static readonly OnexUiTypeface[] OnexUiTypefaces={
   new OnexUiTypeface("Inter","Inter","Inter"),
   new OnexUiTypeface("Manrope","Manrope","Manrope"),
   new OnexUiTypeface("PT Sans","PTSans","PT Sans"),
   new OnexUiTypeface("IBM Plex Sans","IBMPlexSans","IBM Plex Sans"),
   new OnexUiTypeface("Source Sans 3","SourceSans3","Source Sans 3"),
   new OnexUiTypeface("Noto Sans","NotoSans","Noto Sans"),
   new OnexUiTypeface("Segoe UI"),new OnexUiTypeface("Bahnschrift"),
   new OnexUiTypeface("Bahnschrift SemiLight")};
  private FontFamily _onexUiTypeface;
  private Exception _onexUiFontLastError;
  private int _onexUiFontEpoch;
  private double _onexLastFontPassMs;
  private int _onexFontScopePasses;
  private readonly Dictionary<FrameworkElement,int> _onexPageFontEpochs=new Dictionary<FrameworkElement,int>();
  private bool _onexUiFontsPopulated;
  private bool _onexOriginalTextStyleRead;
  private Style _onexOriginalTextStyle;
  private FontFamily ResolveOnexUiFont(string name)=>new FontFamily(OnexUiTypefaces.FirstOrDefault(x=>x.Name==name)?.Source??name);
  private void EnsureOnexUiFonts(string saved)
  {
   if(AppFontComboBox==null||_onexUiFontsPopulated)return;
   bool ready=isAppFontInitialized;isAppFontInitialized=false;
   try
   {
    AppFontComboBox.Items.Clear();
    foreach(var face in OnexUiTypefaces)
     AppFontComboBox.Items.Add(new ComboBoxItem{Content=face.Name,Tag=face.Name,FontFamily=ResolveOnexUiFont(face.Name)});
    // Keep only a genuinely selected legacy face, without exposing the old
    // twelve nearly identical Bahnschrift variants as the normal catalogue.
    if(!OnexUiTypefaces.Any(x=>x.Name==saved)&&!string.IsNullOrWhiteSpace(saved))
     AppFontComboBox.Items.Add(new ComboBoxItem{Content=saved+global::XboxGamingBar.OnexStrings.Get(" (saved)"),Tag=saved,FontFamily=ResolveOnexUiFont(saved)});
    _onexUiFontsPopulated=true;
   }
   finally{isAppFontInitialized=ready;}
  }
  private static bool OnexIsUiIconOrMonospace(FontFamily font)
  {
   string source=font?.Source??"";
   return source.IndexOf("MDL2",StringComparison.OrdinalIgnoreCase)>=0||
    source.IndexOf("Fluent Icons",StringComparison.OrdinalIgnoreCase)>=0||
    source.IndexOf("Segoe UI Symbol",StringComparison.OrdinalIgnoreCase)>=0||
    source.IndexOf("Consolas",StringComparison.OrdinalIgnoreCase)>=0||
    source.IndexOf("Courier",StringComparison.OrdinalIgnoreCase)>=0;
  }
  private void ApplyOnexUiFontNode(DependencyObject node)
  {
   long fontStarted=_onexNavigationPerfCapture?System.Diagnostics.Stopwatch.GetTimestamp():0;
   try{
   if(_onexUiTypeface==null||node is IconElement||node is ComboBoxItem)return;
   if(node is TextBlock text&&!ReferenceEquals(text.Tag,OnexKeyboardLegendTag)&&!OnexIsUiIconOrMonospace(text.FontFamily))
   {if(text.FontFamily.Source!=_onexUiTypeface.Source)text.FontFamily=_onexUiTypeface;}
   else if(node is Control control&&!OnexIsUiIconOrMonospace(control.FontFamily))
   {
    if(control.FontFamily.Source!=_onexUiTypeface.Source)control.FontFamily=_onexUiTypeface;
    if(control is ComboBox combo)
    {var style=OnexOwnedComboItemStyle();if(!ReferenceEquals(combo.ItemContainerStyle,style))combo.ItemContainerStyle=style;}
   }
   }finally{RecordOnexNavigationWork("font-node",node,fontStarted);}
  }
  private void ApplyOnexUiFonts(DependencyObject node)
  {
   if(node==null||_onexUnusedLegacyContent.TryGetValue(node,out var unused))return;
   // The RTSS preview has its own separately selected typeface. Never replace
   // it, icon fonts or the intentional fixed-width diagnostic text.
   if(node is FrameworkElement element&&element.Name.IndexOf("OverlayPreview",StringComparison.OrdinalIgnoreCase)>=0)return;
   if(IsOnexInactiveThemePage(node)||DeferOnexCollapsedAppearance(node,true))return;
   ApplyOnexUiFontNode(node);
   if(node is ComboBox closed&&!closed.IsDropDownOpen)return;
   foreach(var child in ThemeContentChildren(node))ApplyOnexUiFonts(child);
  }
  private void ApplyOnexCurrentPageFont(FrameworkElement root)
  {
   if(root==null||_onexUiTypeface==null)return;
   if(_onexPageFontEpochs.TryGetValue(root,out int epoch)&&epoch==_onexUiFontEpoch){_onexLastFontPassMs=0;return;}
   // A tab can reparent an existing stats/media block without changing the
   // font choice. Paint its public text on entry, not on every layout/focus.
   var timer=System.Diagnostics.Stopwatch.StartNew();ApplyOnexUiFonts(root);
   _onexLastFontPassMs=timer.Elapsed.TotalMilliseconds;_onexPageFontEpochs[root]=_onexUiFontEpoch;_onexFontScopePasses++;
  }
  private void SetOnexUiTypeface(string name)
  {
   var font=ResolveOnexUiFont(name);
   if(_onexUiTypeface?.Source==font.Source)return;
   _onexUiTypeface=font;FontFamily=font;Resources["ContentControlThemeFontFamily"]=font;
   // WinUI's default styles can resolve their resources at application scope.
   // Newly rebuilt profile/status rows must use the selected UI font too.
   var resources=Application.Current.Resources;
   resources["ContentControlThemeFontFamily"]=font;resources["TextControlThemeFontFamily"]=font;
   if(!_onexOriginalTextStyleRead)
   {resources.TryGetValue(typeof(TextBlock),out var original);_onexOriginalTextStyle=original as Style;_onexOriginalTextStyleRead=true;}
   var textStyle=new Style(typeof(TextBlock)){BasedOn=_onexOriginalTextStyle};
   textStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty,font));resources[typeof(TextBlock)]=textStyle;
   _onexUiFontEpoch++;
   if(_releaseHome!=null)_releaseHome.FontFamily=font;
   ApplyOnexUiFonts(this);
   // A public navigation root may sit below an intentionally unwalked Viewbox.
   // Paint the actual active content directly.
   ApplyOnexCurrentPageFont(OnexNavigationRoot());
  }
 }
}
