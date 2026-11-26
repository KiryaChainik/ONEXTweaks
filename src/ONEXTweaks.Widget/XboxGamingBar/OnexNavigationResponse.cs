using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Json;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private int _onexTabFocusRequest;
  private readonly HashSet<RadioButton> _onexTabPointerHooks=new HashSet<RadioButton>();
  private RadioButton _onexTabPointer;private DateTimeOffset _onexTabPointerUtc;
  private double _onexNavFontMs,_onexNavSurfaceMs,_onexNavAccentMs;
  private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject,object> _onexUnusedLegacyContent=new System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject,object>();
  private void HideOnexUnusedLegacy(FrameworkElement item){item.Visibility=Visibility.Collapsed;if(!_onexUnusedLegacyContent.TryGetValue(item,out var prior))_onexUnusedLegacyContent.Add(item,new object());}
  private void RememberOnexTabPointer(RadioButton tab){_onexTabPointer=tab;_onexTabPointerUtc=DateTimeOffset.UtcNow;}
  private void TrackOnexTabPointer(RadioButton tab){if(_onexTabPointerHooks.Add(tab))tab.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((s,e)=>RememberOnexTabPointer(tab)),true);}
  private sealed class OnexDeferredAppearance
  {internal bool Font,Surface;internal ThemeColors Previous;}
  private readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement,OnexDeferredAppearance> _onexDeferredAppearance=new System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement,OnexDeferredAppearance>();
  private readonly List<WeakReference<FrameworkElement>> _onexDeferredAppearanceRoots=new List<WeakReference<FrameworkElement>>();
  private void FlushOnexDeferredAppearance(FrameworkElement element,OnexDeferredAppearance state)
  {
   if(element.Visibility!=Visibility.Visible)return;
   for(var parent=VisualTreeHelper.GetParent(element);parent!=null;parent=VisualTreeHelper.GetParent(parent))if(parent is UIElement hidden&&hidden.Visibility==Visibility.Collapsed)return;
   bool font=state.Font,surface=state.Surface;var previous=state.Previous;state.Font=state.Surface=false;
   if(font)ApplyOnexUiFonts(element);
   if(surface&&_onexAppliedTheme!=null)ApplyOnexCatalogSurfaces(element,_onexAppliedTheme,previous,true);
  }
  private void FlushOnexVisibleDeferredAppearance(FrameworkElement root)
  {
   for(int index=_onexDeferredAppearanceRoots.Count-1;index>=0;index--){
    if(!_onexDeferredAppearanceRoots[index].TryGetTarget(out var element)){_onexDeferredAppearanceRoots.RemoveAt(index);continue;}
    if(!_onexDeferredAppearance.TryGetValue(element,out var state)||!state.Font&&!state.Surface)continue;
    bool belongs=false;for(DependencyObject node=element;node!=null;node=VisualTreeHelper.GetParent(node))if(ReferenceEquals(node,root)){belongs=true;break;}
    if(belongs)FlushOnexDeferredAppearance(element,state);
   }
  }
  private bool DeferOnexCollapsedAppearance(DependencyObject node,bool font,ThemeColors previous=null)
  {
   if(_onexReplayPreviousThemeWork||!(node is FrameworkElement element))return false;
   if(element.Visibility!=Visibility.Collapsed){if(_onexDeferredAppearance.TryGetValue(element,out var pending)){if(font)pending.Font=false;else pending.Surface=false;}return false;}
   if(!_onexDeferredAppearance.TryGetValue(element,out var state)){
    state=new OnexDeferredAppearance();_onexDeferredAppearance.Add(element,state);_onexDeferredAppearanceRoots.Add(new WeakReference<FrameworkElement>(element));
    // Hidden pages are flushed at their next entry even when their palette
    // epoch is unchanged; visible expansions are prepared before layout.
    element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty,(sender,property)=>FlushOnexDeferredAppearance(element,state));
   }
   if(font)state.Font=true;else{if(!state.Surface)state.Previous=previous;state.Surface=true;}
   return true;
  }
  private sealed class OnexNavigationWorkSample{internal string Phase,ElementType;internal double Milliseconds;}
  private readonly List<OnexNavigationWorkSample> _onexNavigationHotspots=new List<OnexNavigationWorkSample>();
  private void RecordOnexNavigationWork(string phase,DependencyObject node,long started)
  {
   if(!_onexNavigationPerfCapture||started==0)return;
   double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency;if(ms<5)return;
   RecordOnexNavigationPoint("phase",phase,started);
   _onexNavigationHotspots.Add(new OnexNavigationWorkSample{Phase=phase,ElementType=node?.GetType().Name??"none",Milliseconds=ms});
   if(_onexNavigationHotspots.Count>80)_onexNavigationHotspots.Remove(_onexNavigationHotspots.OrderBy(item=>item.Milliseconds).First());
  }
  private void MeasureOnexNavigationWork(string phase,DependencyObject node,Action action)
  {long start=System.Diagnostics.Stopwatch.GetTimestamp();try{action();}finally{RecordOnexNavigationWork(phase,node,start);}}
  private JsonArray OnexNavigationHotspotReport(){var result=new JsonArray();foreach(var item in _onexNavigationHotspots)result.Add(new JsonObject{{"phase",JsonValue.CreateStringValue(item.Phase)},{"elementType",JsonValue.CreateStringValue(item.ElementType)},{"milliseconds",JsonValue.CreateNumberValue(item.Milliseconds)}});return result;}
  private bool _onexNavigationPerfCapture;
  private double _onexPushLongestMs;private string _onexPushLongestKey;
  private void ScheduleOnexTabEntryFocus(RadioButton selected)
  {
   int request=++_onexTabFocusRequest;
   // Pointer navigation keeps focus on the tab. Keyboard/controller entry can
   // focus content after the incoming page is allowed to paint.
   if(selected.FocusState==FocusState.Pointer||ReferenceEquals(selected,_onexTabPointer)&&(DateTimeOffset.UtcNow-_onexTabPointerUtc).TotalMilliseconds<1000){_onexTabPointer=null;return;}
   _=Dispatcher.RunAsync(CoreDispatcherPriority.Low,()=>{
    if(request!=_onexTabFocusRequest||selected.IsChecked!=true)return;
    var viewer=GetActiveScrollViewer();var current=FocusManager.GetFocusedElement() as DependencyObject;
    for(var node=current;node!=null;node=VisualTreeHelper.GetParent(node))if(ReferenceEquals(node,viewer))return;
    FocusFirstTabContentElement();
   });
  }
 }
}
