// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class AssignmentRegion
  {
   internal readonly string Id;
   internal readonly double X,Y,Width,Height;
   internal readonly string Outline;
   internal AssignmentRegion(string id,double x,double y,double width,double height,string outline=null)
   { Id=id;X=x;Y=y;Width=width;Height=height;Outline=outline; }

   internal PathGeometry HitGeometry()
   {
    var vertices=Outline.Split(' ').Select(vertex=>
    {
     var parts=vertex.Split(',');
     return new Point(double.Parse(parts[0],CultureInfo.InvariantCulture),double.Parse(parts[1],CultureInfo.InvariantCulture));
    }).ToArray();
    var figure=new PathFigure{StartPoint=vertices[0],IsClosed=true,IsFilled=true};
    foreach(var point in vertices.Skip(1))figure.Segments.Add(new LineSegment{Point=point});
    var geometry=new PathGeometry();geometry.Figures.Add(figure);
    return geometry;
   }
  }

  private sealed class AssignmentArt
  {
   internal readonly string Id;
   internal readonly double Width,Height;
   internal readonly AssignmentRegion[] Regions;
   internal AssignmentArt(string id,double width,double height,AssignmentRegion[] regions)
   { Id=id;Width=width;Height=height;Regions=regions; }
  }

  private static readonly string[] AssignmentMouseTargets={"left","right","middle","wheelUp","wheelDown","wheelLeft","wheelRight","x1","x2"};
  private static readonly string[] AssignmentMouseLabels={"Left click","Right click","Middle click","Wheel up","Wheel down","Scroll left","Scroll right","Back (X1)","Forward (X2)"};
  private static readonly string[] Ds4TouchpadTargets={"TouchpadClick","TouchpadClickLeft","TouchpadClickRight"};
  private static bool IsSplitDs4TouchpadTarget(string id)=>id=="TouchpadClickLeft"||id=="TouchpadClickRight";
  private string ControllerAssignmentTarget=>ControllerText(_onexControllerPending??ControllerEditorPolicy,"virtualTarget","Xbox360");
  private AssignmentArt ControllerPadAssignmentArt=>ControllerAssignmentTarget=="DualShock4"?Ds4AssignmentArt:
   ControllerAssignmentTarget=="Xbox360"?GamepadAssignmentArt:null;
  private bool OnexControllerWideEditor(int index)=>index==2||index==3||index==4;

  private string AssignmentPadLabel(string id)=>AssignmentPadLabel(id,ControllerAssignmentTarget=="DualShock4");
  private string AssignmentPadLabel(string id,bool ds4)
  {
   string label=null;
   if(ds4)
   {
    switch(id)
    {
     case "A":label="Cross";break;case "B":label="Circle";break;case "X":label="Square";break;case "Y":label="Triangle";break;
     case "LT":label="L2 · Left trigger";break;case "RT":label="R2 · Right trigger";break;
     case "LB":label="L1 · Left bumper";break;case "RB":label="R1 · Right bumper";break;
     case "Select":label="Share";break;case "Start":label="Options";break;
     case "Guide":label="PS button";break;case "TouchpadClick":label="Touchpad click (whole pad)";break;
    }
   }
   return label!=null?OnexStrings.Get(label):id=="Guide"?OnexStrings.Get("ONEXPLAYER / Guide"):OnexControllerSourceName(id);
  }

  private sealed class AssignmentDiagram
  {
   internal const double KeyboardAlphanumericSize=17.5;
   internal const double KeyboardLegendSize=10.6;
   internal const double KeyboardLegendWidthScale=.91;
   private readonly AssignmentArt art;
   private readonly Image selectedArtwork;
   private readonly Image baseArtwork;
   private readonly Border keyboardDeck;
   private readonly TextBlock keyboardWordmark;
   private readonly Dictionary<string,TextBlock> legends=new Dictionary<string,TextBlock>(StringComparer.Ordinal);
   private OnexApprovedTheme theme;
   private string activeTarget;
   private IReadOnlyList<int> pressedKeys = Array.Empty<int>();
   private readonly Dictionary<string,Button> buttons=new Dictionary<string,Button>(StringComparer.Ordinal);
   internal readonly Viewbox View;
   internal AssignmentArt Art=>art;
   internal Uri ArtworkUri => (baseArtwork?.Source as BitmapImage)?.UriSource;
   internal Uri HighlightUri => (selectedArtwork?.Source as BitmapImage)?.UriSource;
   internal IEnumerable<Control> Controls=>View.Visibility==Visibility.Visible?buttons.Values.Cast<Control>():Enumerable.Empty<Control>();

   internal AssignmentDiagram(AssignmentArt art,Func<string,string> label,Action<string> choose,double maxWidth=740)
   {
    this.art=art;
    var canvas=new Canvas{Width=art.Width,Height=art.Height};
    bool keyboard=art==KeyboardAssignmentArt;
    var keyboardTypeface=keyboard?new FontFamily("Segoe UI"):null;
    if(keyboard)
    {
     keyboardDeck=new Border{Width=art.Width,Height=art.Height,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1.5),IsHitTestVisible=false};
     canvas.Children.Add(keyboardDeck);
     keyboardWordmark=new TextBlock{Text="ONEXPLAYER",Width=125,TextAlignment=TextAlignment.Center,FontSize=15,
      CharacterSpacing=90,FontWeight=Windows.UI.Text.FontWeights.Bold,IsHitTestVisible=false};
     Canvas.SetLeft(keyboardWordmark,597);Canvas.SetTop(keyboardWordmark,18);canvas.Children.Add(keyboardWordmark);
    }
    else
    {
     baseArtwork=Artwork();selectedArtwork=Artwork();
     canvas.Children.Add(baseArtwork);canvas.Children.Add(selectedArtwork);
    }
    var template=(ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='4'><ContentPresenter Content='{TemplateBinding Content}' HorizontalAlignment='Center' VerticalAlignment='Bottom'/></Border></ControlTemplate>");
    var keyTemplate=keyboard?(ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='4'><ContentPresenter Content='{TemplateBinding Content}' HorizontalAlignment='Stretch' VerticalAlignment='Stretch'/></Border></ControlTemplate>"):null;
    var centeredTemplate=art==Ds4AssignmentArt?(ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><ContentPresenter Content='{TemplateBinding Content}' HorizontalAlignment='Center' VerticalAlignment='Center'/></ControlTemplate>"):null;
    foreach(var region in art.Regions)
    {
     var button=new Button{Template=template,Background=OnexSolidBrush(Windows.UI.Colors.Transparent),BorderThickness=new Thickness(0),
      Width=region.Width,Height=region.Height,MinWidth=0,MinHeight=0,Tag=region.Id,UseSystemFocusVisuals=true};
     string name=label(region.Id);
     if(keyboard)
     {
      var cap=KeyboardLegend(region.Id);
      bool alphanumeric=IsKeyboardAlphanumeric(region.Id);
      double size=alphanumeric?KeyboardAlphanumericSize:KeyboardLegendSize;
      var caption=new TextBlock
      {
       Text=cap,FontSize=size,FontFamily=keyboardTypeface,Tag=OnexKeyboardLegendTag,
       FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextAlignment=TextAlignment.Center,
       HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
       LineHeight=size*(alphanumeric?1.08:1.05),LineStackingStrategy=LineStackingStrategy.BlockLineHeight,
       CharacterSpacing=alphanumeric?0:-25,IsHitTestVisible=false
      };
      if(!alphanumeric)
      {
       caption.RenderTransformOrigin=new Point(.5,.5);
       caption.RenderTransform=new ScaleTransform{ScaleX=KeyboardLegendWidthScale};
      }
      var content=new Grid{IsHitTestVisible=false};
      content.Children.Add(caption);
      button.Template=keyTemplate;button.Content=content;
      legends.Add(region.Id,caption);
     }
     else if(IsSplitDs4TouchpadTarget(region.Id))
     {
      button.Template=centeredTemplate;
      button.Content=new TextBlock
      {
       Text=OnexStrings.Get(region.Id=="TouchpadClickLeft"?"Left":"Right"),FontSize=17,
       FontWeight=Windows.UI.Text.FontWeights.SemiBold,Foreground=OnexSettingsBrush(213,224,239),
      };
     }
     AutomationProperties.SetName(button,name);
     OnexApprovedTheme.OwnTarget(button);
     ToolTipService.SetToolTip(button,name);
     Canvas.SetLeft(button,region.X);Canvas.SetTop(button,region.Y);
     button.Click+=(s,e)=>choose(region.Id);
     buttons.Add(region.Id,button);
     canvas.Children.Add(button);
     string legend=art==GamepadAssignmentArt?XboxLegend(region.Id):null;
     if(legend!=null)
     {
      bool face=legend.Length==1;
      var caption=new TextBlock
      {
       Text=legend,Width=region.Width,Height=26,LineHeight=26,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,
       FontSize=face?19:15,FontWeight=Windows.UI.Text.FontWeights.SemiBold,
       TextAlignment=TextAlignment.Center,IsHitTestVisible=false,
      };
      Canvas.SetLeft(caption,region.X);Canvas.SetTop(caption,region.Y+region.Height/2-13);
      Canvas.SetZIndex(caption,1);
      canvas.Children.Add(caption);
      legends.Add(region.Id,caption);
     }
     if(region.Outline!=null)
     {
      // Keep the button for gamepad/keyboard and use the printed contour for pointer taps.
      button.IsHitTestVisible=false;
      var surface=new Windows.UI.Xaml.Shapes.Path
      {
       Data=region.HitGeometry(),Fill=OnexSolidBrush(Windows.UI.Colors.Transparent),
      };
      surface.Tapped+=(s,e)=>{if(button.IsEnabled){e.Handled=true;choose(region.Id);}};
      ToolTipService.SetToolTip(surface,name);
      canvas.Children.Add(surface);
     }
    }
    View=new Viewbox{Child=canvas,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,
     MaxWidth=maxWidth,HorizontalAlignment=HorizontalAlignment.Center};
    OnexApprovedTheme.Bind(View, OnexApprovedTheme.Current, ApplyTheme);
   }

   internal static bool IsKeyboardAlphanumeric(string id)
    =>id.Length==1&&(id[0]>='A'&&id[0]<='Z'||id[0]>='0'&&id[0]<='9')||
      id.Length==4&&id.StartsWith("Num",StringComparison.Ordinal)&&char.IsDigit(id[3]);

   private static string KeyboardLegend(string id)
   {
    if(id.StartsWith("Num",StringComparison.Ordinal)&&id.Length==4&&char.IsDigit(id[3]))return id.Substring(3);
    switch(id)
    {
     case "PrintScreen":return "Prt\nSc";case "ScrollLock":return "Scr\nLk";case "Pause":return "Ⅱ";
     case "Backquote":return "`";case "Minus":return "−";case "Equal":return "=";case "Backspace":return "⌫";
     case "BracketLeft":return "[";case "BracketRight":return "]";case "Enter":case "NumEnter":return "↵";
     case "CapsLock":return "Caps";case "Semicolon":return ";";case "Quote":return "'";case "Backslash":case "IntlBackslash":return "\\";
     case "RightShift":return "Shift";case "Comma":return ",";case "Period":case "NumDecimal":return ".";case "Slash":case "NumDivide":return "/";
     case "RightCtrl":return "Ctrl";case "RightAlt":return "Alt";case "ContextMenu":return "☰";
     case "Insert":return "Ins";case "Delete":return "Del";case "PageUp":return "Pg\nUp";case "PageDown":return "Pg\nDn";
     case "ArrowUp":return "↑";case "ArrowLeft":return "←";case "ArrowDown":return "↓";case "ArrowRight":return "→";
     case "NumLock":return "Num\nLk";case "NumMultiply":return "×";case "NumMinus":return "−";case "NumPlus":return "+";
     default:return id;
    }
   }

   private static string XboxLegend(string id)
   {
    switch(id)
    {
     case "A":case "B":case "X":case "Y":case "LT":case "LB":case "RT":case "RB":return id;
     case "LSClick":return "L3";
     case "RSClick":return "R3";
     default:return null;
    }
   }

   internal IEnumerable<TextBlock> Legends=>legends.Values;

   private void ApplyTheme(OnexApprovedTheme palette)
   {
    theme = palette;
    if(keyboardDeck!=null)
    {
     keyboardDeck.Background=OnexSolidBrush(palette.Inset);
     keyboardDeck.BorderBrush=OnexSolidBrush(palette.DiagramFrame);
     keyboardWordmark.Foreground=OnexSolidBrush(palette.Text);
     PaintKeys(pressedKeys);
     return;
    }
    baseArtwork.Source = palette.Artwork(art.Id);
    Select(activeTarget);
    if (art == Ds4AssignmentArt)
     foreach (var button in buttons.Values)
      if (button.Content is TextBlock label) label.Foreground = OnexSolidBrush(palette.Text);
   }

   private Image Artwork() => new Image
   {
    Width = art.Width, Height = art.Height, IsHitTestVisible = false, Stretch = Stretch.Fill
   };

   internal void Select(string selected)
   {
    activeTarget = selected;
    if (selectedArtwork == null) return;
    selectedArtwork.Source = selected != null &&
      (buttons.ContainsKey(selected) || art == Ds4AssignmentArt && selected == "TouchpadClick")
      ? theme.Artwork(art.Id + "-" + selected) : null;
    foreach(var legend in legends)
     legend.Value.Foreground=OnexSolidBrush(legend.Key==selected?theme.SelectedText:theme.Text);
   }

   internal void SetAvailable(string id,bool enabled,string hint)
   {
    if(!buttons.TryGetValue(id,out var button))return;
    button.IsEnabled=enabled;
    AutomationProperties.SetHelpText(button,hint??"");
    ToolTipService.SetToolTip(button,hint??AutomationProperties.GetName(button));
   }

   internal void PaintKeys(IReadOnlyList<int> keys)
   {
    pressedKeys = keys;
    var cap=OnexSolidBrush(OnexApprovedTheme.Blend(theme.Inset,theme.Text,.055));
    var selection=OnexSolidBrush(OnexApprovedTheme.Blend(theme.Inset,theme.SelectedLine,theme.ChipWash));
    var boundary=OnexSolidBrush(theme.DiagramFrame);
    var outline=OnexSolidBrush(theme.SelectedLine);
    var ink=OnexSolidBrush(theme.Text);
    var selectedInk=OnexSolidBrush(theme.SelectedText);
    foreach(var item in buttons)
    {
     int key=AssignmentKeyCode(item.Key), index=-1;
     for(int i=0;i<keys.Count;i++)if(AssignmentDiagramKey(keys[i])==key){index=i;break;}
     item.Value.Background=index<0?cap:selection;
     item.Value.BorderBrush=index<0?boundary:outline;
     item.Value.BorderThickness=new Thickness(index<0?1:2);
     legends[item.Key].Foreground=index<0?ink:selectedInk;
    }
   }
  }

  private static readonly object OnexKeyboardLegendTag=new object();

  private static int AssignmentDiagramKey(int key)
  {
   switch(key){case 160:return 16;case 162:return 17;case 164:return 18;default:return key;}
  }

  private static int AssignmentKeyCode(string id)
  {
   if(id.Length==1)return id[0];
   if(id[0]=='F'&&int.TryParse(id.Substring(1),out var function))return 111+function;
   if(id.StartsWith("Num",StringComparison.Ordinal)&&int.TryParse(id.Substring(3),out var number))return 96+number;
   switch(id)
   {
    case "Esc":return 27;case "PrintScreen":return 44;case "ScrollLock":return 145;case "Pause":return 19;
    case "Backquote":return 192;case "Minus":return 189;case "Equal":return 187;case "Backspace":return 8;
    case "Tab":return 9;case "BracketLeft":return 219;case "BracketRight":return 221;case "Enter":return 13;
    case "CapsLock":return 20;case "Semicolon":return 186;case "Quote":return 222;case "Backslash":return 220;
    case "Shift":return 16;case "RightShift":return 161;case "IntlBackslash":return 226;case "Comma":return 188;
    case "Period":return 190;case "Slash":return 191;case "Ctrl":return 17;case "RightCtrl":return 163;
    case "Alt":return 18;case "RightAlt":return 165;case "Win":return 91;case "Space":return 32;case "ContextMenu":return 93;
    case "Insert":return 45;case "Delete":return 46;case "Home":return 36;case "End":return 35;
    case "PageUp":return 33;case "PageDown":return 34;case "ArrowUp":return 38;case "ArrowDown":return 40;
    case "ArrowLeft":return 37;case "ArrowRight":return 39;case "NumLock":return 144;case "NumDivide":return 111;
    case "NumMultiply":return 106;case "NumMinus":return 109;case "NumPlus":return 107;case "NumEnter":return 269;case "NumDecimal":return 110;
    default:throw new ArgumentException("Unknown key in assignment artwork",nameof(id));
   }
  }

  private sealed class KeyboardAssignmentPicker
  {
   private static readonly int[] DelayPresets={1,2,5,10,20,30,40,50,100,200};
   private readonly Style delaySliderStyle;
   private readonly AssignmentDiagram diagram;
   private readonly ComboBox mode,keyList;
   private readonly StackPanel steps,sequenceStrip;
   private readonly Button apply;
   private readonly Func<int,string> label;
   private readonly Func<JsonObject,Task> commit;
   private readonly List<int> keys=new List<int>();
   private readonly List<TextBox> delays=new List<TextBox>();
   private readonly TextBlock note;
   private bool syncing,dirty;
   private int paintedMode;
   private string loadedBinding;
   private OnexApprovedTheme sequencePalette;
   internal void SetEnabled(bool enabled)
   {
    foreach(var control in Controls)control.IsEnabled=enabled;
    apply.IsEnabled=enabled&&keys.Count>0;
   }
   internal bool HasOpenDropdown=>mode.IsDropDownOpen||keyList.IsDropDownOpen;
   internal readonly StackPanel View=new StackPanel{Spacing=8};
   internal IEnumerable<Control> Controls=>View.Visibility!=Visibility.Visible?Enumerable.Empty<Control>():
    new Control[]{mode,keyList}.Concat(diagram.Controls)
     .Concat(steps.Children.OfType<Grid>().SelectMany(row=>row.Children.OfType<Control>())).Concat(new[]{apply});
   private bool Sequence=>mode.SelectedIndex==0;

   internal KeyboardAssignmentPicker(GamingWidget owner,Func<JsonObject,Task> commit)
   {
    this.commit=commit;
    delaySliderStyle=(Style)owner.Resources["OnexPreciseSliderStyle"];
    label=OnexVirtualKeyDisplayName;
    mode=owner.KbmChoice("OnexAssignmentKeyboardMode",OnexStrings.Get("Keyboard mode"));
    mode.Items.Add(OnexStrings.Get("Sequence"));mode.Items.Add(OnexStrings.Get("Shortcut"));mode.SelectedIndex=0;
    keyList=owner.KbmChoice("OnexAssignmentKeys",OnexStrings.Get("Keys"));
    var codes=KeyboardAssignmentArt.Regions.Select(x=>AssignmentKeyCode(x.Id))
      .Concat(OnexKeyboardChoiceCodes).Where(key=>key!=0).Distinct();
    keyList.Items.Add(new ComboBoxItem{Content=OnexStrings.Get("Choose a key"),Tag=0});
    foreach(int key in codes)keyList.Items.Add(new ComboBoxItem{Content=label(key),Tag=key});
    keyList.SelectedIndex=0;
    View.Children.Add(owner.OnexSettingsColumns(mode,keyList));
    diagram=new AssignmentDiagram(KeyboardAssignmentArt,id=>label(AssignmentKeyCode(id)),id=>
    {
     int key=AssignmentKeyCode(id);
     int selected=keys.Find(value=>AssignmentDiagramKey(value)==key);
     Toggle(selected==0?key:selected);
    });
    View.Children.Add(diagram.View);
    sequenceStrip=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,Visibility=Visibility.Collapsed};
    View.Children.Add(sequenceStrip);
    View.SizeChanged+=(s,e)=>FitSequenceChips(e.NewSize.Width);
    OnexApprovedTheme.Bind(sequenceStrip,OnexApprovedTheme.Current, PaintSequenceChips);
    steps=new StackPanel{Spacing=5};
    View.Children.Add(steps);
    note=owner.ControllerHelp("");
    View.Children.Add(note);
    apply=owner.KbmButton("OnexApplyKeyboardAssignment",OnexStrings.Get("Apply action"));
    View.Children.Add(apply);
    void ChangeMode()
    {
     if(syncing||mode.IsDropDownOpen||paintedMode==mode.SelectedIndex)return;
     if(Sequence&&keys.Count>3)keys.RemoveRange(3,keys.Count-3);
     dirty=true;Rebuild();
    }
    mode.SelectionChanged+=(s,e)=>ChangeMode();
    mode.DropDownClosed+=(s,e)=>ChangeMode();
    void SelectKey()
    {
     if(syncing||keyList.IsDropDownOpen||!(keyList.SelectedItem is ComboBoxItem item)||!(item.Tag is int key)||key==0)return;
     Toggle(key);keyList.SelectedIndex=0;
    }
    keyList.SelectionChanged+=(s,e)=>SelectKey();
    keyList.DropDownClosed+=(s,e)=>SelectKey();
    apply.Click+=async(s,e)=>await Apply();
    Rebuild();
   }

   internal void Load(JsonObject binding,bool force)
   {
    string identity=binding?.Stringify();
    if(!force&&(dirty||identity==loadedBinding))return;
    loadedBinding=identity;
    syncing=true;
    try
    {
     var saved=ReadAssignmentNumbers(binding,"keys");
     keys.Clear();keys.AddRange(saved.Distinct());
     mode.SelectedIndex=binding==null||ControllerText(binding,"kind")=="keySequence"?0:1;
     dirty=false;
     Rebuild(ReadAssignmentNumbers(binding,"delays").Select(value=>value.ToString()).ToArray());
    }
    finally{syncing=false;}
   }

   private void Toggle(int key)
   {
    var before=keys.ToArray();
    var times=delays.Select(box=>box.Text).ToArray();
    if(!keys.Remove(key))
    {
     if(keys.Count>=(Sequence?3:5)){ShowKeyboardNote(OnexStrings.Get(Sequence?"Choose up to three keys":"Choose up to five keys"));return;}
     keys.Add(key);
    }
    var retained=new string[Math.Max(0,keys.Count-1)];
    for(int i=0;i<retained.Length;i++)
    {
     int prior=Array.IndexOf(before,keys[i]);
     if(prior>=0&&prior<times.Length&&before[prior+1]==keys[i+1])retained[i]=times[prior];
    }
    dirty=true;Rebuild(retained);
   }

   private void Rebuild(string[] values=null)
   {
    if(values==null)values=delays.Select(box=>box.Text).ToArray();
    steps.Children.Clear();delays.Clear();
    paintedMode=mode.SelectedIndex;
    diagram.PaintKeys(keys);
    string summary=string.Join(Sequence?" → ":" + ",keys.Select(label));
    RefreshSequenceChips();
    ShowKeyboardNote(Sequence?"":summary);
    foreach(var item in keyList.Items.OfType<ComboBoxItem>())
    {
     int key=(int)item.Tag;
     item.Content=key==0?(keys.Count==0?OnexStrings.Get("Choose a key"):summary):
      keys.Contains(key)?OnexStrings.Format("Remove {0}",label(key)):label(key);
    }
    apply.IsEnabled=keys.Count>0;
    if(!Sequence)return;
    for(int i=0;i<keys.Count-1;i++)AddDelayEditor(keys[i],values.ElementAtOrDefault(i)??"100");
   }

   private void ShowKeyboardNote(string text)
   {
    note.Text=text;
    note.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;
   }

   internal IEnumerable<string> SequenceChips=>sequenceStrip.Children.OfType<Border>().Select(chip=>((TextBlock)chip.Child).Text);

   private void RefreshSequenceChips()
   {
    sequenceStrip.Children.Clear();
    sequenceStrip.Visibility=Sequence&&keys.Count>0?Visibility.Visible:Visibility.Collapsed;
    if(sequenceStrip.Visibility!=Visibility.Visible)return;
    for(int index=0;index<keys.Count;index++)
    {
     string text=(index+1).ToString(CultureInfo.CurrentCulture)+"  "+label(keys[index]);
     var caption=new TextBlock{Text=text,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,
      FontSize=12,FontWeight=Windows.UI.Text.FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
     var chip=new Border{Child=caption,Padding=new Thickness(7,3,7,3),MinHeight=25,
      BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6)};
     ToolTipService.SetToolTip(chip,text);
     sequenceStrip.Children.Add(chip);
    }
    PaintSequenceChips(sequencePalette);
    FitSequenceChips(View.ActualWidth);
   }

   private void FitSequenceChips(double width)
   {
    int count=sequenceStrip.Children.Count;
    if(width<=0||count==0)return;
    double maximum=Math.Max(20,(width-(count-1)*sequenceStrip.Spacing)/count);
    foreach(var chip in sequenceStrip.Children.OfType<Border>())chip.MaxWidth=maximum;
   }

   private void PaintSequenceChips(OnexApprovedTheme palette)
   {
    sequencePalette=palette;
    var background=OnexSolidBrush(palette.Selected);
    var outline=OnexSolidBrush(palette.SelectedLine);
    var ink=OnexSolidBrush(palette.SelectedText);
    foreach(var chip in sequenceStrip.Children.OfType<Border>())
    {
     chip.Background=background;chip.BorderBrush=outline;((TextBlock)chip.Child).Foreground=ink;
    }
   }

   private void AddDelayEditor(int key,string value)
   {
    string caption=OnexStrings.Format("Delay after {0} (ms)",label(key));
    var scope=new InputScope();
    scope.Names.Add(new InputScopeName {NameValue=InputScopeNameValue.Number});
    var box=new TextBox {Text=value,InputScope=scope,TextAlignment=TextAlignment.Right,Margin=new Thickness(10,0,0,0)};
    AutomationProperties.SetName(box,caption);
    var slider=new Slider
    {
     Style=delaySliderStyle,StepFrequency=1,SmallChange=1,LargeChange=1,SnapsTo=SliderSnapsTo.StepValues,
     IsThumbToolTipEnabled=false,VerticalAlignment=VerticalAlignment.Center
    };
    OnexInputGuard.Wire(slider);
    var choices=DelayPresets;
    bool syncingSlider=false;
    int SelectedDelay()=>choices[(int)slider.Value];
    void UpdateSlider(int delay)
    {
     syncingSlider=true;
     try
     {
      OnexInputGuard.CancelInteraction(slider);
      // Keep a saved or typed value exact even if it falls between the presets.
      choices=Array.IndexOf(DelayPresets,delay)>=0?DelayPresets:DelayPresets.Concat(new[]{delay}).OrderBy(n=>n).ToArray();
      slider.Maximum=choices.Length-1;
      slider.Value=Array.IndexOf(choices,delay);
      AutomationProperties.SetName(slider,caption+" · "+delay);
     }
     finally{syncingSlider=false;}
    }
    UpdateSlider(int.TryParse(value,out int initial)&&initial>=0?initial:100);
    slider.ValueChanged+=(s,e)=>
    {
     if(syncingSlider)return;
     int delay=SelectedDelay();
     box.Text=delay.ToString(CultureInfo.CurrentCulture);
     AutomationProperties.SetName(slider,caption+" · "+delay);
    };
    box.TextChanged+=(s,e)=>
    {
     if(!syncing)dirty=true;
     if(!syncingSlider&&int.TryParse(box.Text,out int delay)&&delay>=0&&delay!=SelectedDelay())
      UpdateSlider(delay);
    };

    var row=new Grid();
    row.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
    row.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
    row.ColumnDefinitions.Add(new ColumnDefinition());
    row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(86)});
    var title=new TextBlock {Text=caption,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3)};
    Grid.SetColumnSpan(title,2);
    Grid.SetRow(slider,1);
    Grid.SetRow(box,1);
    Grid.SetColumn(box,1);
    row.Children.Add(title);
    row.Children.Add(slider);
    row.Children.Add(box);
    delays.Add(box);
    steps.Children.Add(row);
   }

   private async Task Apply()
   {
    if(keys.Count==0)return;
    var values=new int[delays.Count];
    for(int i=0;i<values.Length;i++)
     if(!int.TryParse(delays[i].Text,out values[i])||values[i]<0)
     {ShowKeyboardNote(OnexStrings.Get("Enter a nonnegative delay in milliseconds"));delays[i].Focus(FocusState.Programmatic);return;}
    var payload=KbmBinding(Sequence?"keySequence":"keyboard","",keys.ToArray());
    if(Sequence)payload["delays"]=AssignmentNumbers(values);
    dirty=false;
    await commit(payload);
   }
  }

  private async Task VerifyOnexKeyboardLegendsAsync(JsonObject report)
  {
   var root=(Grid)Content;
   JsonObject binding=null;
   var picker=new KeyboardAssignmentPicker(this,value=>{binding=value;return Task.CompletedTask;});
   var host=new Border{Width=700,Height=455,HorizontalAlignment=HorizontalAlignment.Left,
    VerticalAlignment=VerticalAlignment.Top,Child=picker.View,IsHitTestVisible=false};
   Grid.SetRowSpan(host,20);
   Canvas.SetZIndex(host,10000);
   root.Children.Add(host);
   void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
   try
   {
    var selected=KbmBinding("keySequence","",123,145,144);
    selected["delays"]=AssignmentNumbers(new[]{10,20});
    picker.Load(selected,true);
    UpdateLayout();
    await Task.Delay(100);
    var diagram=picker.View.Children.OfType<Viewbox>().Single();
    var keys=((Canvas)diagram.Child).Children.OfType<Button>().ToArray();
    Check(keys.Length==104,"Keyboard target count changed.");
    int large=0;
    foreach(var button in keys)
    {
     string id=(string)button.Tag;
     var content=(Grid)button.Content;
     Check(content.Children.Count==1&&content.Children[0] is TextBlock,"Unexpected text overlay in "+id);
     var caption=(TextBlock)content.Children[0];
     bool alphanumeric=AssignmentDiagram.IsKeyboardAlphanumeric(id);
     if(alphanumeric)large++;
     double target=alphanumeric?AssignmentDiagram.KeyboardAlphanumericSize:AssignmentDiagram.KeyboardLegendSize;
     Check(Math.Abs(caption.FontSize-target)<.02&&string.Equals(caption.FontFamily.Source,"Segoe UI",StringComparison.OrdinalIgnoreCase),
      $"Unexpected keyboard caption style for {id}: {caption.FontSize}, {caption.FontFamily.Source}");
     var rect=caption.TransformToVisual(button).TransformBounds(new Rect(0,0,caption.ActualWidth,caption.ActualHeight));
     Check(caption.ActualWidth>0&&caption.ActualHeight>0&&rect.Left>=.5&&rect.Top>=.5&&
      rect.Right<=button.ActualWidth-.5&&rect.Bottom<=button.ActualHeight-.5,"Legend crossed its key: "+id);
     Check(Math.Abs(rect.Left+rect.Width/2-button.ActualWidth/2)<1.25,"Legend not centered: "+id);
    }
    Check(large==46,"Wrong letter and digit grouping.");
    Check(picker.SequenceChips.SequenceEqual(new[]{123,145,144}.Select((key,index)=>
     (index+1).ToString(CultureInfo.CurrentCulture)+"  "+OnexVirtualKeyDisplayName(key))),"Sequence labels missing or reordered.");
    Check(keys.Count(key=>key.BorderThickness.Left==2)==3,"Key selection lost its outline.");
    var delays=picker.Controls.OfType<TextBox>().ToArray();
    Check(delays.Length==2&&delays[0].Text=="10"&&delays[1].Text=="20","Sequence delay display changed.");
    InvokeLightingButton(picker.Controls.OfType<Button>().Single(button=>button.Name=="OnexApplyKeyboardAssignment"));
    await Task.Delay(80);
    Check(ReadAssignmentNumbers(binding,"keys").SequenceEqual(new[]{123,145,144})&&
     ReadAssignmentNumbers(binding,"delays").SequenceEqual(new[]{10,20}),"Sequence output changed.");
    await SaveOnexImage("onex-keyboard-legends-native.png",diagram,100);
    report["keyboardCaptionsChecked"]=JsonValue.CreateNumberValue(keys.Length);
    report["largeLettersAndDigits"]=JsonValue.CreateNumberValue(large);
    report["orderedChipsPreservedSequence"]=JsonValue.CreateBooleanValue(true);
   }
   finally{root.Children.Remove(host);}
  }

  private async Task VerifyAssignmentPickersAsync(JsonObject report)
  {
   int checks=0;
   void Check(bool value,string text){checks++;if(!value)throw new InvalidOperationException(text);}
   void Invoke(Button button)=>
    ((Windows.UI.Xaml.Automation.Provider.IInvokeProvider)new Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
      .GetPattern(Windows.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();

   foreach(var art in new[]{GamepadAssignmentArt,Ds4AssignmentArt,MouseAssignmentArt,KeyboardAssignmentArt})
   {
    var packaged=await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/OnexRemap/"+art.Id+".png"));
    Check((await packaged.GetBasicPropertiesAsync()).Size>0,"Diagram is not packaged");
   }
   string chosen=null;
   var mouse=new AssignmentDiagram(MouseAssignmentArt,KbmMouseCaption,id=>chosen=id);
   var mouseButtons=mouse.Controls.OfType<Button>().ToArray();
   Check(mouseButtons.Length==9,"Incomplete mouse hotspots");
   foreach(string id in new[]{"x1","x2","wheelDown","middle"})
   {
    Invoke(mouseButtons.Single(button=>Equals(button.Tag,id)));
    await Task.Delay(50);
    Check(chosen==id,"Wrong mouse hotspot: "+id);
    mouse.Select(id);
   }
   var pad=new AssignmentDiagram(GamepadAssignmentArt,AssignmentPadLabel,id=>chosen=id);
   Check(pad.Controls.Count()==25&&!pad.Controls.Any(control=>Equals(control.Tag,"Share")),"Unexpected gamepad hotspot");
   foreach(string id in new[]{"DPadRight","LSUp","RSRight"})
   {
    Invoke((Button)pad.Controls.Single(control=>Equals(control.Tag,id)));
    await Task.Delay(50);
    Check(chosen==id,"Wrong gamepad hotspot: "+id);
   }

   var ds4=new AssignmentDiagram(Ds4AssignmentArt,id=>AssignmentPadLabel(id,true),id=>chosen=id);
   var nativeDs4=ds4.Controls.OfType<Button>().ToArray();
   Check(nativeDs4.Length==27,"The approved DS4 layout requires 27 selectable regions");
   var ds4Canvas=(Canvas)ds4.View.Child;
   var highlight=(Image)ds4Canvas.Children[1];
   Check(ds4Canvas.Children.OfType<Windows.UI.Xaml.Shapes.Path>().Count()==nativeDs4.Length,"DS4 is missing a pointer contour");
   foreach(string id in new[]{"TouchpadClickLeft","TouchpadClickRight","Select","Guide"})
   {
    Invoke(nativeDs4.Single(button=>Equals(button.Tag,id)));
    await Task.Delay(50);
    Check(chosen==id,"Wrong DS4 hotspot: "+id);
    ds4.Select(id);
    Check(((BitmapImage)highlight.Source).UriSource.AbsolutePath.EndsWith("/ds4-"+id+".png",StringComparison.Ordinal),"Wrong DS4 highlight: "+id);
   }
   foreach(string id in Ds4TouchpadTargets)
   {
    var image=await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/OnexRemap/ds4-"+id+".png"));
    Check((await image.GetBasicPropertiesAsync()).Size>0,"DS4 touchpad highlight is missing: "+id);
   }
   ds4.Select("TouchpadClick");
   Check(((BitmapImage)highlight.Source).UriSource.AbsolutePath.EndsWith("/ds4-TouchpadClick.png",StringComparison.Ordinal),"Whole-pad assignment lost its highlight");
   var left=Ds4AssignmentArt.Regions.Single(region=>region.Id=="TouchpadClickLeft").HitGeometry().Bounds;
   var right=Ds4AssignmentArt.Regions.Single(region=>region.Id=="TouchpadClickRight").HitGeometry().Bounds;
   Check(Math.Abs(left.Right-right.Left)<.1&&left.Left<right.Left,"Touchpad regions overlap or are misaligned");

   JsonObject sequence=null;
   var editor=new KeyboardAssignmentPicker(this,value=>{sequence=value;return Task.CompletedTask;});
   var buttons=editor.Controls.OfType<Button>().ToArray();
   foreach(string id in new[]{"A","B","NumEnter","C"})
   {
    Invoke(buttons.Single(button=>Equals(button.Tag,id)));
    await Task.Delay(50);
   }
   var waits=editor.Controls.OfType<TextBox>().ToArray();
   Check(waits.Length==2,"The sequence should contain three keys and two delays");
   waits[0].Text="150";waits[1].Text="0";
   Invoke(buttons.Single(button=>button.Name=="OnexApplyKeyboardAssignment"));
   await Task.Delay(100);
   Check(ControllerText(sequence,"kind")=="keySequence"&&ReadAssignmentNumbers(sequence,"keys").SequenceEqual(new[]{65,66,269})
    &&ReadAssignmentNumbers(sequence,"delays").SequenceEqual(new[]{150,0}),"Native sequence payload differs from the picker");
   Invoke(buttons.Single(button=>Equals(button.Tag,"B")));
   await Task.Delay(50);
   Check(editor.Controls.OfType<TextBox>().Count()==1,"Repeated key selection did not remove it");
   await VerifyControllerKeyboardDraftAsync(Check,Invoke);
   report["assignmentPickersPassed"]=JsonValue.CreateBooleanValue(true);
   report["assignmentPickerChecks"]=JsonValue.CreateNumberValue(checks);
   report["ds4AssignmentPickerPassed"]=JsonValue.CreateBooleanValue(true);
   report["controllerKeyboardDraftPassed"]=JsonValue.CreateBooleanValue(true);
   report["controllerButtonRestorePassed"]=JsonValue.CreateBooleanValue(true);
   report["ownerProfileWrites"]=JsonValue.CreateNumberValue(0);
  }

  private async Task VerifyControllerKeyboardDraftAsync(Action<bool,string> check,Action<Button> invoke)
  {
   var previousTab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   bool previousSync=_onexControllerSync,previousProjection=_onexControllerReadOnlyProjection;
   string original=null;
   try
   {
    LegionNavItem.IsChecked=true;
    original=(await ReadOnexControllerAsync()).Stringify();
    check(_onexControllerPending==null&&!_onexControllerBusy,"Controller editor is already saving.");
    _onexControllerReadOnlyProjection=true;
    var state=JsonObject.Parse(original);
    state["enabled"]=JsonValue.CreateBooleanValue(true);
    var policy=state.GetNamedObject("editor").GetNamedObject("policy");
    policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);
    policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(false);
    var bindings=JsonObject.Parse("{\"X\":{\"kind\":\"gamepad\",\"target\":\"Y\"}}");
    policy["bindings"]=bindings;
    string oldState=state.Stringify();
    ApplyOnexControllerState(oldState);
    if(_onexControllerOpen!="mapping")SetOnexControllerCard("mapping");
    int restores=0;
    EnsureOnexControllerBindingEditor(request=>
    {
     restores++;
     check(ControllerText(request,"operation")=="resetBinding"&&ControllerText(request,"id")==ControllerEditorId&&
      ControllerText(request,"source")=="A"&&!request.ContainsKey("policy"),"The editor sent a reset outside button A.");
     bindings.Remove("A");
     ApplyOnexControllerState(state.Stringify());
     return Task.CompletedTask;
    });
    SelectOnexControllerMappingSource("A",_onexControllerDiagramButtons["A"],true);
    check(_onexInlineRemapWindow!=null&&_onexControllerKind.SelectedIndex==0,"Default action did not open inline.");
    _onexControllerSync=true;
    _onexControllerKind.SelectedIndex=3;
    _onexControllerSync=false;
    ChooseOnexControllerAction();
    ApplyOnexControllerState(oldState);
    for(int attempt=0;attempt<20&&_onexControllerMappingFlyout?.IsOpen!=true;attempt++)await Task.Delay(50);
    check(_onexControllerMappingFlyout?.IsOpen==true&&_onexControllerKind.SelectedIndex==3,
      "The saved Default state prevented the keyboard diagram from opening.");
    var flyout=_onexControllerMappingFlyout;
    var picker=_controllerKeyboardArt;
    foreach(string id in new[]{"A","B","C"})
    {
     invoke(picker.Controls.OfType<Button>().Single(button=>Equals(button.Tag,id)));
     await Task.Delay(50);
     ApplyOnexControllerState(oldState);
    }
    var delays=picker.Controls.OfType<TextBox>().ToArray();
    var sliders=picker.Controls.OfType<Slider>().ToArray();
    check(delays.Length==2&&sliders.Length==2,"Expected a slider and a numeric field for each delay.");
    OnexInputGuard.HandleSliderKey(sliders[0],VirtualKey.GamepadA,false);
    OnexInputGuard.HandleSliderKey(sliders[0],VirtualKey.GamepadDPadLeft,false);
    check(delays[0].Text=="50","One D-pad step from 100 ms did not select 50 ms.");
    OnexInputGuard.HandleSliderKey(sliders[0],VirtualKey.GamepadB,false);
    check(delays[0].Text=="100","Cancel did not restore the previous delay.");
    OnexInputGuard.HandleSliderKey(sliders[0],VirtualKey.GamepadA,false);
    OnexInputGuard.HandleSliderKey(sliders[0],VirtualKey.GamepadDPadRight,false);
    OnexInputGuard.HandleSliderKey(sliders[0],VirtualKey.GamepadA,false);
    check(delays[0].Text=="200","Confirm did not keep the selected preset.");
    delays[0].Text="250";
    delays[1].Text="125";
    ApplyOnexControllerState(oldState);
    invoke(picker.Controls.OfType<Button>().Single(button=>button.Name=="OnexApplyKeyboardAssignment"));
    await Task.Delay(100);
    ApplyOnexControllerState(oldState);
    check(flyout.IsOpen&&_onexControllerKind.SelectedIndex==3&&_controllerKeyboardSubmittedBinding!=null,
     "The editor reverted before the save response.");
    check(ReadAssignmentNumbers(_controllerKeyboardSubmittedBinding,"keys").SequenceEqual(new[]{65,66,67})&&
     ReadAssignmentNumbers(_controllerKeyboardSubmittedBinding,"delays").SequenceEqual(new[]{250,125}),"The submitted keys or delays changed.");
    bindings["A"]=JsonObject.Parse(_controllerKeyboardSubmittedBinding.Stringify());
    ApplyOnexControllerState(state.Stringify());
    check(flyout.IsOpen&&_onexControllerKind.SelectedIndex==3&&_controllerKeyboardDraftContext==null,
     "The acknowledged keyboard assignment did not replace its draft.");
    invoke(_onexControllerStandardReset);
    await Task.Delay(100);
    check(_onexControllerResetMenu==null&&restores==0&&bindings.ContainsKey("A"),"Group reset activated behind the button editor.");
    invoke(_onexControllerBindingReset);
    for(int attempt=0;attempt<20&&_onexInlineRemapWindow==null;attempt++)await Task.Delay(50);
    check(_onexInlineRemapWindow!=null&&_onexControllerKind.SelectedIndex==0&&ControllerSourceId=="A"&&restores==1,
     "Restoring the default did not reset exactly the open button.");
    check(ControllerText(ControllerEditorPolicy.GetNamedObject("bindings").GetNamedObject("X",null),"target")=="Y",
     "The unrelated X assignment did not survive the restore response.");
    CloseOnexRemapEditor(_onexControllerMappingFlyout);
    invoke(_onexControllerStandardReset);
    for(int attempt=0;attempt<20&&_onexControllerResetMenu?.IsOpen!=true;attempt++)await Task.Delay(50);
    check(_onexControllerResetMenu?.IsOpen==true&&_onexControllerResetMenu.Items.Count==1&&restores==1,
     "A group reset must open its own confirmation first.");
   }
   finally
   {
    _onexControllerResetMenu?.Hide();
    CloseOnexRemapEditor(_onexControllerMappingFlyout);
    _onexControllerSync=previousSync;
    if(original!=null)ApplyOnexControllerState(original);
    _onexControllerReadOnlyProjection=previousProjection;
    if(previousTab!=null)previousTab.IsChecked=true;
   }
  }

  private static int[] ReadAssignmentNumbers(JsonObject binding,string name)
  {
   if(binding!=null&&binding.TryGetValue(name,out var value)&&value.ValueType==JsonValueType.Array)
    return value.GetArray().Select(item=>(int)item.GetNumber()).ToArray();
   return Array.Empty<int>();
  }

  private static JsonArray AssignmentNumbers(IEnumerable<int> numbers)
  {
   var items=new JsonArray();
   foreach(int value in numbers)items.Add(JsonValue.CreateNumberValue(value));
   return items;
  }
 }
}
