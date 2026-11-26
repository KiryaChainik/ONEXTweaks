// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexRemapWindow
  {
   internal Flyout Flyout;
   internal SketchRemapHost Sketch, WideSketch;
   internal Button Anchor, Close;
   internal Control ReturnFocus;
   internal Action Focus,Closed;
   internal StackPanel Editor;
   internal ScrollViewer Scroll;
   internal Grid PopupRoot;
   internal TextBlock InlineTitle, PopupTitle;
   internal bool ReturningToSketch;
   internal Color Accent;
   internal double Width=-1,Height=-1,NaturalHeight=-1;
   internal string ThemeKey;
  }
  private const double OnexRemapHeaderHeight=40;
  private DataTemplate _onexRemapTextTemplate;
  private readonly List<OnexRemapWindow> _onexRemapWindows=new List<OnexRemapWindow>();
  private Size OnexRemapViewport()
  {
   double width=ActualWidth>0?ActualWidth:720,height=ActualHeight>0?ActualHeight:640;
   if(XamlRoot!=null){if(XamlRoot.Size.Width>0)width=Math.Min(width,XamlRoot.Size.Width);if(XamlRoot.Size.Height>0)height=Math.Min(height,XamlRoot.Size.Height);}
   if(widget!=null){if(widget.WindowBounds.Width>0)width=Math.Min(width,widget.WindowBounds.Width);if(widget.WindowBounds.Height>0)height=Math.Min(height,widget.WindowBounds.Height);}
   return new Size(Math.Max(1,width),Math.Max(1,height));
  }
  private Flyout CreateOnexRemappingWindow(StackPanel editor,Color accent,Action focus=null,Action closed=null,TextBlock title=null)
  {
   var view=new OnexRemapWindow{Editor=editor,Accent=accent,Focus=focus,Closed=closed,Scroll=new ScrollViewer{Content=editor,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Disabled,Padding=new Thickness(4,4,12,4),HorizontalContentAlignment=HorizontalAlignment.Stretch}};
   // Full placement provides a root-centred anchor. Centre alignment and auto
   // height keep short editors compact instead of stretching them to the root.
   var flyout=view.Flyout=new Flyout{Content=view.Scroll,Placement=FlyoutPlacementMode.Full,ShouldConstrainToRootBounds=true};_onexRemapWindows.Add(view);
   if(title!=null)
   {
    view.InlineTitle=title;
    view.PopupTitle=new TextBlock{FontSize=title.FontSize,FontWeight=title.FontWeight,TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=1,Margin=new Thickness(4,0,12,0),VerticalAlignment=VerticalAlignment.Center};
    view.PopupTitle.SetBinding(TextBlock.TextProperty,new Binding{Source=title,Path=new PropertyPath(nameof(TextBlock.Text))});
    view.Close=CreateOnexRemapCloseButton(()=>CloseOnexRemapEditor(flyout));
    view.Close.Name="OnexControllerRemapClose";
    view.Close.PreviewKeyDown+=HandleOnexControllerMappingEditorKey;
    var header=new Grid{Height=32,Margin=new Thickness(0,0,0,8)};
    header.ColumnDefinitions.Add(new ColumnDefinition());
    header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
    Grid.SetColumn(view.Close,1);
    header.Children.Add(view.PopupTitle);
    header.Children.Add(view.Close);
    view.PopupRoot=new Grid();
    view.PopupRoot.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
    view.PopupRoot.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
    Grid.SetRow(view.Scroll,1);
    view.PopupRoot.Children.Add(header);
    flyout.Content=null;
    view.PopupRoot.Children.Add(view.Scroll);
    flyout.Content=view.PopupRoot;
   }
   flyout.Opening+=(s,e)=>
   {
    if(view.InlineTitle!=null)view.InlineTitle.Visibility=Visibility.Collapsed;
    ConfigureOnexRemappingWindow(view);
   };
   flyout.Opened+=(s,e)=>{ConfigureOnexRemappingWindow(view);_ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,()=>{if(flyout.IsOpen)view.Focus?.Invoke();});};
   flyout.Closed+=(s,e)=>
   {
    if(!TryReturnOnexEditorToSketch(view))view.Closed?.Invoke();
   };
   SizeChanged+=(s,e)=>{if(flyout.IsOpen)ConfigureOnexRemappingWindow(view);};
   // Opening configures bounds before the presenter is shown. Measuring the
   // closed editor here realises every hidden keyboard dropdown at startup.
   view.Scroll.Content=null;view.Scroll.Content=WrapOnexFocusContent(editor);return flyout;
  }
  private void ConfigureOnexRemappingWindow(OnexRemapWindow view)
  {
   var theme=_onexAppliedTheme??OnexPalette.CreateTheme();var accent=OnexBrandTheme?view.Accent:theme.EffectiveAccent();
   string themeKey=currentThemeName+theme.CardBackground.ToString()+accent.ToString();
   bool wide=OnexWideAssignmentWindow(view);
   var viewport=OnexRemapViewport();double width=Math.Min(wide?610:500,Math.Max(120,viewport.Width-(wide?24:32))),
    height=Math.Min(wide?640:560,Math.Max(120,viewport.Height-40));
   if(view.WideSketch!=null&&view.Flyout.IsOpen&&!OnexControllerWideEditor(_onexControllerKind?.SelectedIndex??-1))return;
   width=Math.Min(width,Math.Max(1,viewport.Width-8));height=Math.Min(height,Math.Max(1,viewport.Height-8));
   view.Scroll.MinWidth=0;view.Scroll.VerticalAlignment=VerticalAlignment.Top;view.Scroll.VerticalContentAlignment=VerticalAlignment.Top;view.Editor.MinWidth=0;view.Editor.HorizontalAlignment=HorizontalAlignment.Stretch;view.Editor.VerticalAlignment=VerticalAlignment.Top;
   view.Scroll.Width=Math.Max(1,width-30);view.Scroll.MaxWidth=view.Scroll.Width;
   if(view.PopupTitle!=null)view.PopupTitle.Foreground=OnexSolidBrush(theme.TextPrimary);
   var headerHeight=view.PopupRoot==null?0:OnexRemapHeaderHeight;
   double contentWidth=Math.Max(60,width-46),dropHeight=Math.Max(72,Math.Min(300,viewport.Height-64));
   ConfigureOnexRemappingControls(view,contentWidth,dropHeight);
   // Measure the public editor content, not UWP private template children.
   // This lets a short action picker keep its natural height in Full placement.
   view.Editor.Measure(new Size(contentWidth,double.PositiveInfinity));double natural=Math.Min(height,Math.Max(80,Math.Ceiling(view.Editor.DesiredSize.Height+40+headerHeight)));view.Scroll.MaxHeight=Math.Max(1,natural-30-headerHeight);
   if(view.Width!=width||view.Height!=height||view.NaturalHeight!=natural||view.ThemeKey!=themeKey)
   {
    view.Width=width;view.Height=height;view.NaturalHeight=natural;view.ThemeKey=themeKey;var style=new Style{TargetType=typeof(FlyoutPresenter)};
    void Set(DependencyProperty key,object value)=>style.Setters.Add(new Setter(key,value));
    Set(FrameworkElement.RequestedThemeProperty,OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark);Set(Control.BackgroundProperty,OnexDialogSurface);Set(Control.BorderBrushProperty,OnexSolidBrush(accent));Set(Control.BorderThicknessProperty,new Thickness(1));Set(Control.CornerRadiusProperty,new CornerRadius(12));Set(Control.PaddingProperty,new Thickness(14));Set(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch);Set(Control.VerticalContentAlignmentProperty,VerticalAlignment.Top);Set(FrameworkElement.WidthProperty,width);Set(FrameworkElement.MaxWidthProperty,width);Set(FrameworkElement.HeightProperty,natural);Set(FrameworkElement.MaxHeightProperty,height);Set(FrameworkElement.MinWidthProperty,0d);Set(FrameworkElement.MinHeightProperty,0d);Set(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);Set(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);view.Flyout.FlyoutPresenterStyle=style;
   }
   var presenter=OnexRemapPresenter(view.Editor);if(presenter!=null){presenter.Width=width;presenter.MaxWidth=width;presenter.Height=natural;presenter.MaxHeight=height;presenter.MinWidth=0;presenter.MinHeight=0;presenter.HorizontalAlignment=HorizontalAlignment.Center;presenter.VerticalAlignment=VerticalAlignment.Center;presenter.HorizontalContentAlignment=HorizontalAlignment.Stretch;presenter.VerticalContentAlignment=VerticalAlignment.Top;}
  }
  private bool OnexWideAssignmentWindow(OnexRemapWindow view)=>
   view.Flyout==_onexControllerMappingFlyout&&(_onexControllerKind?.SelectedIndex==2||_onexControllerKind?.SelectedIndex==3)||
   view.Flyout==_kbmFlyout&&KbmEditorKind=="keyboard";
  private void ConfigureOnexRemappingControls(OnexRemapWindow view,double contentWidth,double dropHeight)
  {
   var theme=_onexAppliedTheme??OnexPalette.CreateTheme();
   void Spacing(StackPanel panel){if(panel==null)return;panel.Spacing=0;bool first=true;foreach(var child in panel.Children.OfType<FrameworkElement>()){bool visible=child.Visibility==Visibility.Visible;child.Margin=new Thickness(0,visible&&!first?10:0,0,0);if(visible)first=false;}}
   Spacing(view.Editor);if(ReferenceEquals(view.Editor,_onexControllerMappingEditor))Spacing(_onexControllerMacroPanel);else Spacing(_kbmCustom);
   if(_onexRemapTextTemplate==null)_onexRemapTextTemplate=(DataTemplate)XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><TextBlock Text=\"{Binding}\" TextWrapping=\"Wrap\" MaxLines=\"2\" FontSize=\"13\" /></DataTemplate>");
   view.Editor.MaxWidth=contentWidth;
   foreach(var control in OnexOwnedContent(view.Editor).OfType<Control>())
   {
    if(OnexApprovedTheme.OwnsTarget(control))continue;
    control.MinWidth=0;ConfigureOnexFocus(control);
    if(control is Button button){button.Background=OnexSolidBrush(theme.ButtonBackground);button.Foreground=OnexSolidBrush(theme.TextPrimary);button.BorderBrush=OnexSolidBrush(theme.ButtonBorder);button.BorderThickness=new Thickness(1);button.CornerRadius=new CornerRadius(8);button.MinHeight=36;button.HorizontalContentAlignment=HorizontalAlignment.Center;button.Resources["ButtonBackgroundDisabled"]=OnexSolidBrush(theme.CardBackground);button.Resources["ButtonForegroundDisabled"]=OnexSolidBrush(theme.TextSecondary);button.Resources["ButtonBorderBrushDisabled"]=OnexSolidBrush(theme.CardBorder);}
    if(control is ComboBox choice)
    {
     choice.MaxDropDownHeight=dropHeight;choice.HorizontalAlignment=HorizontalAlignment.Stretch;choice.MaxWidth=contentWidth;
     foreach(var item in choice.Items.OfType<ComboBoxItem>())
     {
      ConfigureOnexFocus(item);item.MinWidth=0;item.MaxWidth=contentWidth;item.HorizontalContentAlignment=HorizontalAlignment.Stretch;item.FocusVisualMargin=new Thickness(2);item.FocusVisualPrimaryThickness=new Thickness(2);item.FocusVisualSecondaryThickness=new Thickness(0);item.Padding=new Thickness(10,7,10,7);
      if(item.Content is string&&!ReferenceEquals(item.ContentTemplate,_onexRemapTextTemplate))item.ContentTemplate=_onexRemapTextTemplate;
     }
    }
   }
   ApplyOnexCatalogSurfaces(view.Editor,theme);
  }
  private static FlyoutPresenter OnexRemapPresenter(DependencyObject element)
  {for(var parent=VisualTreeHelper.GetParent(element);parent!=null;parent=VisualTreeHelper.GetParent(parent))if(parent is FlyoutPresenter presenter)return presenter;return null;}
  private void RefreshOnexRemappingWindows()
  {foreach(var view in _onexRemapWindows)if(view.Flyout.IsOpen)ConfigureOnexRemappingWindow(view);else if(view.Sketch!=null)RefreshOnexSketchEditor(view.Sketch);}
 }
}
