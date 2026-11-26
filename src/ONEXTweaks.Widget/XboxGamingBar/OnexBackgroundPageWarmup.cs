using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  // Imported device editors and legacy hotkeys have separate ONEX replacements. Keep their small
  // page hosts, but never construct the unused native/managed control trees.
  public bool LoadImportedDeviceUi => !OnexFrontend.UsesOwnBackend;
  private Task _onexBackgroundPageWarmup;
  private readonly HashSet<string> _onexWarmedPages=new HashSet<string>();
  private readonly Dictionary<string,double> _onexPageWarmupMilliseconds=new Dictionary<string,double>();
  private Task EnsureOnexBackgroundPageWarmupAsync()
  {
   if(_onexBackgroundPageWarmup!=null&&!_onexBackgroundPageWarmup.IsCompleted)return _onexBackgroundPageWarmup;
   if(_onexWarmedPages.Count==2)return Task.CompletedTask;
   return _onexBackgroundPageWarmup=PrepareOnexBackgroundPagesAsync();
  }
  private async Task PrepareOnexBackgroundPagesAsync()
  {
   foreach(string id in new[]{"KeyboardMouse","Overlay"})
   {
    // A separate low-priority turn for each page. Never cover an interactive
    // foreground page, navigate, read/set a profile, or run this on tab entry.
    await Dispatcher.RunAsync(CoreDispatcherPriority.Low,()=>
    {
     if(_onexWarmedPages.Contains(id)||App.GetActiveGamingWidget()!=this||!App.IsConnected)return;
     if(widget==null||widget.Visible&&OnexStartupCover.Visibility==Visibility.Collapsed)return;
     FrameworkElement page=null;Visibility oldVisibility=Visibility.Collapsed,oldEditorVisibility=Visibility.Collapsed;
     double oldOpacity=1;bool restorePage=false,oldHitTest=true,oldKbmSync=_kbmSync,oldOsdLoading=_isLoadingOSDConfig;
     var elapsed=Stopwatch.StartNew();
     try
     {
      _kbmSync=true;isLoadingOSDConfig=true;
      if(id=="KeyboardMouse"){EnsureOnexKeyboardMouseFrontend();page=_onexKeyboardMousePage;}
      else{EnsureOnexOverlayEditor();page=_releaseOsd;}
      if(page==null||page.Visibility==Visibility.Visible)return;
      oldVisibility=page.Visibility;oldOpacity=page.Opacity;oldHitTest=page.IsHitTestVisible;restorePage=true;
      oldEditorVisibility=OSDCustomizeContent.Visibility;
      page.Opacity=0;page.IsHitTestVisible=false;page.Visibility=Visibility.Visible;
      if(id=="Overlay")OSDCustomizeContent.Visibility=Visibility.Visible;
      var content=(page as Windows.UI.Xaml.Controls.ScrollViewer)?.Content as DependencyObject??page;
      ApplyOnexUiFonts(content);if(_onexAppliedTheme!=null)ApplyOnexCatalogSurfaces(content,_onexAppliedTheme);
      double width=Math.Max(320,ActualWidth),height=Math.Max(400,ActualHeight);
      page.Measure(new Windows.Foundation.Size(width,height));
      page.Arrange(new Windows.Foundation.Rect(0,0,width,height));page.UpdateLayout();
      _onexWarmedPages.Add(id);_onexPageWarmupMilliseconds[id]=elapsed.Elapsed.TotalMilliseconds;
     }
     catch(Exception error){Logger.Warn("ONEX background page preparation: "+id+" "+error.GetType().Name);}
     finally
     {
      if(page!=null&&restorePage){if(id=="Overlay")OSDCustomizeContent.Visibility=oldEditorVisibility;page.Visibility=oldVisibility;page.Opacity=oldOpacity;page.IsHitTestVisible=oldHitTest;}
      _kbmSync=oldKbmSync;isLoadingOSDConfig=oldOsdLoading;
     }
    });
   }
  }
 }
}
