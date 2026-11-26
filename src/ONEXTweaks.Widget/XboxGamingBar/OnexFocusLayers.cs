using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Numerics;
using Windows.UI.Composition;
using Windows.UI.Xaml.Hosting;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexTargetFocusInk
  {
   internal ContainerVisual Root;internal ShapeVisual Visual;
   internal CompositionRoundedRectangleGeometry Geometry;internal CompositionColorBrush Brush;
   internal float Width,Height,Radius;internal Rect ChromeBounds;
   internal WeakReference<FrameworkElement> ChromeBody;internal ControlTemplate ChromeTemplate;
   internal object ChromeHeader;internal double BodyWidth,BodyHeight,ChromeControlWidth,ChromeControlHeight;internal FlowDirection ChromeFlow;internal bool ChromeMeasured;
  }
  private sealed class OnexFocusLayer
  {internal Grid Host;internal Control Target;internal OnexTargetFocusInk Ink;internal bool Main;}
  private readonly ConditionalWeakTable<Control,OnexTargetFocusInk> _onexTargetFocusInks=new ConditionalWeakTable<Control,OnexTargetFocusInk>();
  private int _onexFocusGeometryWrites,_onexFocusChromeMeasurements,_onexFocusChromeCacheHits;
  private readonly ConditionalWeakTable<Grid,OnexFocusLayer> _onexFocusHosts=new ConditionalWeakTable<Grid,OnexFocusLayer>();
  private readonly ConditionalWeakTable<Control,OnexFocusLayer> _onexControlLayers=new ConditionalWeakTable<Control,OnexFocusLayer>();
  private readonly List<WeakReference<OnexFocusLayer>> _onexFocusLayers=new List<WeakReference<OnexFocusLayer>>();
  private OnexFocusLayer _onexMainFocusLayer;
  private readonly HashSet<FrameworkElement> _onexFocusLayoutRoots=new HashSet<FrameworkElement>();
  private OnexFocusLayer EnsureOnexFocusLayer(Grid host,bool main=false)
  {
   if(_onexFocusHosts.TryGetValue(host,out var existing))return existing;
   var layer=new OnexFocusLayer{Host=host,Main=main};
   _onexFocusHosts.Add(host,layer);_onexFocusLayers.Add(new WeakReference<OnexFocusLayer>(layer));
   host.SizeChanged+=(s,e)=>RefreshOnexFocusLayer(layer);
   host.GotFocus+=(s,e)=>{var focused=FocusManager.GetFocusedElement() as Control;if(focused==null||focused is ComboBoxItem||focused is MenuFlyoutItemBase)return;if(layer.Main&&_onexControlLayers.TryGetValue(focused,out var inner)&&!ReferenceEquals(inner,layer))return;_onexControlLayers.Remove(focused);_onexControlLayers.Add(focused,layer);ConfigureOnexFocus(focused,!_onexFocusControls.TryGetValue(focused,out var known));FocusOnexOwnedControl(focused);};
   if(main)_onexMainFocusLayer=layer;
   RegisterOnexFocusLayerContent(host,layer);return layer;
  }
  private void RegisterOnexFocusLayerContent(DependencyObject root,OnexFocusLayer layer)
  {
   foreach(var control in OnexOwnedContent(root).OfType<Control>())
   {
    if(control is ComboBoxItem||control is MenuFlyoutItemBase)continue;
    if(_onexControlLayers.TryGetValue(control,out var prior)&&ReferenceEquals(prior,layer))continue;
    _onexControlLayers.Remove(control);_onexControlLayers.Add(control,layer);ConfigureOnexFocus(control);
   }
  }
  private FrameworkElement WrapOnexFocusContent(UIElement content)
  {
   if(content is Grid existing&&_onexFocusHosts.TryGetValue(existing,out var prior)){RegisterOnexFocusLayerContent(existing,prior);return existing;}
   var host=new Grid{HorizontalAlignment=HorizontalAlignment.Stretch};host.Children.Add(content);EnsureOnexFocusLayer(host);return host;
  }
  private void FocusOnexOwnedControl(Control control)
  {
   if(control==null||!ReferenceEquals(FocusManager.GetFocusedElement(),control))return;
   if(OnexApprovedTheme.OwnsTarget(control)){HideOnexAllFocusLayers();return;}
   // A focused scrolling/dialog host must not draw a frame around the whole
   // window. Its actionable children and footer keep their own focus ink.
   if(control is ContentDialog||control is ScrollViewer||control is MenuFlyoutPresenter){HideOnexAllFocusLayers();return;}
   if(!control.IsTabStop&&!(control is ComboBoxItem)&&!(control is MenuFlyoutItemBase)){HideOnexAllFocusLayers();return;}
   if(control is ComboBoxItem||control is MenuFlyoutItemBase){HideOnexAllFocusLayers();return;}
   bool registeredScope=_onexControlLayers.TryGetValue(control,out var scoped);
   // Non-wrapped native dialogs can still have an unregistered inline footer.
   // Wrapped tile dialogs explicitly register footer buttons in their own scope.
   if(!registeredScope&&OnexHasInlineFocus(control)){HideOnexAllFocusLayers();return;}
   var layer=registeredScope?scoped:_onexMainFocusLayer;
   if(layer==null)return;
   if(!_onexControlLayers.TryGetValue(control,out var registered)){_onexControlLayers.Add(control,layer);}
   foreach(var reference in _onexFocusLayers)if(reference.TryGetTarget(out var other)&&!ReferenceEquals(other,layer)){HideOnexFocusInk(other);other.Target=null;}
   if(!ReferenceEquals(layer.Target,control))HideOnexFocusInk(layer);
   layer.Target=control;RefreshOnexFocusLayer(layer);
  }
  private void HideOnexOwnedFocus(Control control)
  {
   var layer=_onexControlLayers.TryGetValue(control,out var scoped)?scoped:_onexMainFocusLayer;
   if(layer!=null&&ReferenceEquals(layer.Target,control)){HideOnexFocusInk(layer);layer.Target=null;}
  }
  private OnexTargetFocusInk EnsureOnexTargetFocusInk(Control target)
  {
   if(_onexTargetFocusInks.TryGetValue(target,out var ink)){if(ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(target),ink.Root))return ink;_onexTargetFocusInks.Remove(target);}
   var compositor=ElementCompositionPreview.GetElementVisual(target).Compositor;
   var root=compositor.CreateContainerVisual();var visual=compositor.CreateShapeVisual();
   var geometry=compositor.CreateRoundedRectangleGeometry();
   var brush=compositor.CreateColorBrush(OnexFocusColour);
   var shape=compositor.CreateSpriteShape(geometry);shape.StrokeBrush=brush;shape.StrokeThickness=2;
   visual.Shapes.Add(shape);visual.Opacity=0;
   // Keep any application-supplied hand-in content. Never replace native
   // template children or mutate the XAML-owned hand-out visual's layout offsets.
   var previous=ElementCompositionPreview.GetElementChildVisual(target);
   if(previous!=null){ElementCompositionPreview.SetElementChildVisual(target,null);root.Children.InsertAtBottom(previous);}
   root.Children.InsertAtTop(visual);ElementCompositionPreview.SetElementChildVisual(target,root);
   ink=new OnexTargetFocusInk{Root=root,Visual=visual,Geometry=geometry,Brush=brush};
   _onexTargetFocusInks.Add(target,ink);return ink;
  }
  private static void HideOnexFocusInk(OnexFocusLayer layer)
  {if(layer?.Ink!=null)layer.Ink.Visual.Opacity=0;layer.Ink=null;}
  private static bool IsOnexFocusInkVisible(OnexFocusLayer layer)=>layer?.Ink!=null&&layer.Target!=null&&layer.Ink.Visual.Opacity>0;
  // Text/choice headers are labels outside editable chrome. Owned toggles
  // include their padded content, so the outline never hugs the track or text. Keep the ink
  // attached to the control (and therefore the compositor scroll transform),
  // but draw only inside its actual template body. Never use window coordinates.
  private Rect OnexFocusChromeBounds(Control control,OnexTargetFocusInk ink)
  {
   var bounds=new Rect(0,0,control.ActualWidth,control.ActualHeight);
   string part=control is ComboBox?"Background":control is TextBox?"BorderElement":control is Slider?"HorizontalTemplate":null;
   if(part==null)return bounds;
   FrameworkElement body=null;
   if(!ReferenceEquals(ink.ChromeTemplate,control.Template)||ink.ChromeBody==null||!ink.ChromeBody.TryGetTarget(out body)||!body.IsLoaded)
   {
    body=FindDescendantByName(control,part);
    if(control is ComboBox&&FindDescendantByName(control,"HighlightBackground") is FrameworkElement halo)halo.Visibility=Visibility.Collapsed;
    ink.ChromeBody=body==null?null:new WeakReference<FrameworkElement>(body);ink.ChromeTemplate=control.Template;ink.ChromeMeasured=false;
   }
   if(body==null||body.Visibility!=Visibility.Visible||body.ActualWidth<=2||body.ActualHeight<=2)return bounds;
   object header=control is ComboBox combo?combo.Header:control is TextBox text?text.Header:control is ToggleSwitch toggle?toggle.Header:(control as Slider)?.Header;
   // GeneralTransform is a WinRT wrapper. Reusing unchanged local geometry
   // avoids allocating one on every focus refresh/page recolour. Size, header,
   // template, flow and body-size changes all invalidate this geometry.
   if(ink.ChromeMeasured&&ink.ChromeControlWidth==bounds.Width&&ink.ChromeControlHeight==bounds.Height&&ink.BodyWidth==body.ActualWidth&&ink.BodyHeight==body.ActualHeight&&ReferenceEquals(ink.ChromeHeader,header)&&ink.ChromeFlow==control.FlowDirection)
   {_onexFocusChromeCacheHits++;return ink.ChromeBounds;}
   var chrome=body.TransformToVisual(control).TransformBounds(new Rect(0,0,body.ActualWidth,body.ActualHeight));_onexFocusChromeMeasurements++;
   if(control is Slider){chrome.X-=12;chrome.Width+=24;}
   chrome.Intersect(bounds);if(chrome.Width<=2||chrome.Height<=2)return bounds;
   ink.ChromeMeasured=true;ink.ChromeControlWidth=bounds.Width;ink.ChromeControlHeight=bounds.Height;ink.ChromeHeader=header;ink.ChromeFlow=control.FlowDirection;ink.BodyWidth=body.ActualWidth;ink.BodyHeight=body.ActualHeight;return chrome;
  }
  private void RefreshOnexFocusLayer(OnexFocusLayer layer)
  {
   if(layer==null)return;var control=layer.Target;
   if(control==null||!control.IsLoaded||!control.IsEnabled||control.Visibility!=Visibility.Visible||control.FocusState==FocusState.Unfocused||control.FocusState==FocusState.Pointer)
   {HideOnexFocusInk(layer);return;}
   try
   {
    float width=(float)control.ActualWidth,height=(float)control.ActualHeight;
    if(width<=2||height<=2){HideOnexFocusInk(layer);return;}
    var ink=EnsureOnexTargetFocusInk(control);layer.Ink=ink;
    var chrome=OnexFocusChromeBounds(control,ink);var corners=control.CornerRadius;float radius=(float)Math.Max(8,Math.Max(Math.Max(corners.TopLeft,corners.TopRight),Math.Max(corners.BottomLeft,corners.BottomRight)));
    radius=(float)Math.Min(radius,Math.Min(chrome.Width,chrome.Height)/2);
    if(ink.Width!=width||ink.Height!=height||ink.Radius!=radius||ink.ChromeBounds!=chrome)
    {
     ink.Root.Size=ink.Visual.Size=new Vector2(width,height);
     ink.Geometry.Offset=new Vector2((float)chrome.X+1,(float)chrome.Y+1);ink.Geometry.Size=new Vector2((float)chrome.Width-2,(float)chrome.Height-2);
     ink.Geometry.CornerRadius=new Vector2(Math.Max(0,radius-1));
     ink.Width=width;ink.Height=height;ink.Radius=radius;ink.ChromeBounds=chrome;_onexFocusGeometryWrites++;
    }
    if(ink.Brush.Color!=OnexFocusColour)ink.Brush.Color=OnexFocusColour;
    // A target-local child shares the target's compositor transforms and
    // ancestor scroll clips. No ViewChanged/Rendering repositioning is needed.
    ink.Visual.Opacity=1;
   }
   catch(ArgumentException){HideOnexFocusInk(layer);}
   catch(InvalidOperationException error){HideOnexFocusInk(layer);Logger.Warn("Owned composition focus unavailable: "+error.GetType().Name);}
   catch(System.Runtime.InteropServices.COMException error){HideOnexFocusInk(layer);Logger.Warn("Owned composition focus rejected: "+error.HResult.ToString("X8"));}
  }
  private void HideOnexAllFocusLayers(){foreach(var reference in _onexFocusLayers)if(reference.TryGetTarget(out var layer)){HideOnexFocusInk(layer);layer.Target=null;}}
  private void RefreshOnexOwnedFocus()
  {foreach(var reference in _onexFocusLayers)if(reference.TryGetTarget(out var layer))RefreshOnexFocusLayer(layer);}
 }
}
