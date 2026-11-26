// SPDX-License-Identifier: AGPL-3.0-only
// Exact owner-supplied .itermcolors inputs; UI surfaces/contrast adjustments are derived.
using System;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexOwnerPaletteRecord
  {
   internal readonly string Name,File,Sha,Page,Text,Selection,SelectedText,Focus,Secondary;
   internal readonly string[] Roles;
   internal OnexOwnerPaletteRecord(string name,string file,string sha,string page,string text,string selection,string selectedText,string focus,string secondary,params string[] roles)
   {Name=name;File=file;Sha=sha;Page=page;Text=text;Selection=selection;SelectedText=selectedText;Focus=focus;Secondary=secondary;Roles=roles;}
  }
  private static readonly OnexOwnerPaletteRecord[] OnexOwnerPaletteRecords={
            new OnexOwnerPaletteRecord("Catppuccin Frappé", "catppuccin-frappe.itermcolors", "1e4391c2f4b28ed0ad3cd9d9d9fab2d8531b8890cfbd911d9b5f4d67bd77a405", "303446", "C6D0F5", "44495D", "C6D0F5", "8CAAEE", "9CA4C4", "E5C890", "81C8BE", "8CAAEE", "F4B8E4"),
            new OnexOwnerPaletteRecord("Catppuccin Latte", "catppuccin-latte.itermcolors", "f8dbbb5be91ea09a4103f967db26eb94e37c363ac59dfb9cd57349637a89130f", "EFF1F5", "4C4F69", "D8DAE1", "4C4F69", "1E66F5", "66697F", "DF8E1D", "179299", "1E66F5", "EA76CB"),
            new OnexOwnerPaletteRecord("Catppuccin Macchiato", "catppuccin-macchiato.itermcolors", "05704815610bb33ac43c46a59e93e8e094e514ce657488ae822f6c4881545640", "24273A", "CAD3F5", "3A3E53", "CAD3F5", "8AADF4", "9CA3C1", "EED49F", "8BD5CA", "8AADF4", "F5BDE6"),
            new OnexOwnerPaletteRecord("Catppuccin Mocha", "catppuccin-mocha.itermcolors", "9b53d175937345f754a89a3ba97ac6afe3324be81be4c5f7626653f9d4b05ea3", "1E1E2E", "CDD6F4", "353748", "CDD6F4", "89B4FA", "9CA2BD", "F9E2AF", "94E2D5", "89B4FA", "F5C2E7"),
            new OnexOwnerPaletteRecord("Cobalt2", "cobalt2-default.itermcolors", "82363b1453121aae1ab097ffaa570b14c520911a5937ce31f3d62d0d3c21d38d", "122738", "FFFFFF", "0050A4", "FFFFFF", "0088FF", "BDC3C7", "FFC600", "80FCFF", "0088FF", "FB94FF"),
            new OnexOwnerPaletteRecord("Dracula", "dracula-default.itermcolors", "2f8eb1f9d4e14ce51897b9c828d4abcc568dc70a72070b5da7cabb5d61bfd424", "282A36", "F8F8F2", "44475A", "F8F8F2", "BD93F9", "BEBEBD", "F1FA8C", "8BE9FD", "BD93F9", "FF79C6"),
            new OnexOwnerPaletteRecord("Kanagawa Dragon", "kanagawa-dragon.itermcolors", "7147bf50c25f062debb22e64cc9578f1a68e642a761efe464e657b5f61df1adb", "181616", "C5C9C5", "2D4F67", "C8C093", "8BA4B0", "959794", "C4B28A", "8EA4A2", "8BA4B0", "A292A3"),
            new OnexOwnerPaletteRecord("Kanagawa Lotus", "kanagawa-lotus.itermcolors", "0df370bcdffd2180820b346585bdfb0c17b54246f6d85fab64c71a5d75be1811", "F2ECBC", "545464", "C9CBD1", "43436C", "4D699B", "66656E", "77713F", "597B75", "4D699B", "B35B79"),
            new OnexOwnerPaletteRecord("Kanagawa Wave", "kanagawa-wave.itermcolors", "4b5afa738639f012e74aba4547b68ab38e7645ed5aa047db9b1643674b5e9476", "1F1F28", "DCD7BA", "2D4F67", "C8C093", "7E9CD8", "A7A391", "C0A36E", "6A9589", "7E9CD8", "957FB8"),
            new OnexOwnerPaletteRecord("Miasma", "miasma-default.itermcolors", "35e13e669b9e7c4c838a25b70edc92bffd0c75473d5dc4c45e1b1bb8e383cae8", "222222", "C2C2B0", "E5C47B", "000000", "78824B", "959588", "B36D43", "C9A554", "78824B", "BB7744"),
            new OnexOwnerPaletteRecord("Shades of Purple", "shades-of-purple-default.itermcolors", "348b5ce74fc5689fb8f6e222353f6edec9a68b96def6520b064b1d44ec58c803", "1E1E3F", "FFFFFF", "6D42A5", "FFFFFF", "7857FE", "C0C0C9", "FAD000", "80FCFF", "7857FE", "FF2C70"),
  };
  private static ThemeColors MakeOnexOwnerPalette(OnexOwnerPaletteRecord source)
  {
   var page=OnexHex(source.Page);var text=OnexHex(source.Text);
   var theme=new ThemeColors{Name=source.Name,PageBackground=page,TextPrimary=text,
    CardBackground=OnexOver(text,page,.04),ButtonBackground=OnexOver(text,page,.08),
    CardBorder=OnexOver(text,page,.25),ButtonBorder=OnexOver(text,page,.25),
    SelectionBackground=OnexHex(source.Selection),SelectionText=OnexHex(source.SelectedText),
    TextSecondary=OnexHex(source.Secondary)};
   // Caption colours are UI derivatives, not a terminal ANSI slot. Leave room
   // for the static glass finish without dimming enabled text via opacity.
   for(int step=0;step<=100;step++)
   {
    var colour=OnexOver(text,OnexHex(source.Secondary),step/100d);
    if(new[]{theme.PageBackground,theme.CardBackground,theme.ButtonBackground}.All(bg=>OnexContrast(colour,bg)>=5))
    {theme.TextSecondary=colour;break;}
    if(step==100)theme.TextSecondary=text;
   }
   var focus=source.Name=="Cobalt2"||source.Name=="Shades of Purple"?source.Roles[0]:source.Focus;
   theme.AccentColor=OnexReadableAccent(theme,OnexHex(focus),true);
   theme.TileOff=theme.TileOn=theme.CardBackground;theme.TileIcon=text;
   theme.MetricsBackground=theme.CardBackground;theme.MetricsBorder=theme.CardBorder;theme.GlowColor=theme.AccentColor;
   return theme;
  }
  private static Color[] OnexThemeContrastSurfaces(ThemeColors theme)
  {
   var colours=new System.Collections.Generic.List<Color>{theme.PageBackground,theme.CardBackground,theme.ButtonBackground};
   if(theme.PageBackground2.HasValue)colours.Add(theme.PageBackground2.Value);
   foreach(var fill in new[]{theme.CardBackground,theme.ButtonBackground})
   {
    var stops=OnexGlassSurfaces.GlassColours(fill,theme);
    colours.Add(stops.Edge);colours.Add(stops.Shoulder);
   }
   return colours.Distinct().ToArray();
  }
  private sealed class OnexChoicePaletteState { internal bool Painting; }
  private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBoxItem,OnexChoicePaletteState> _onexChoicePaletteStates=new System.Runtime.CompilerServices.ConditionalWeakTable<ComboBoxItem,OnexChoicePaletteState>();
  private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox,object> _onexChoicePaletteParents=new System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox,object>();
  private void ConfigureOnexChoiceColours(ComboBoxItem item)
  {
   if(!_onexChoicePaletteStates.TryGetValue(item,out var state))
   {
    state=new OnexChoicePaletteState();_onexChoicePaletteStates.Add(item,state);
    item.RegisterPropertyChangedCallback(SelectorItem.IsSelectedProperty,(sender,property)=>ConfigureOnexChoiceColours((ComboBoxItem)sender));
    item.Loaded+=(sender,args)=>ConfigureOnexChoiceColours((ComboBoxItem)sender);
   }
   if(state.Painting)return;state.Painting=true;
   try
   {
    var theme=_onexAppliedTheme??OnexPalette.CreateTheme();
    SetOnexThemeBrush(item,Control.ForegroundProperty,item.IsSelected?(theme.SelectionText??theme.TextPrimary):theme.TextPrimary);
    item.Background=item.IsSelected&&theme.SelectionBackground.HasValue?OnexSolidBrush(theme.SelectionBackground.Value):OnexSurface(theme.ButtonBackground,theme);
    ConfigureOnexInteractionSurfaces(item);
   }
   finally{state.Painting=false;}
  }
  private void ConfigureOnexInteractionSurfaces(Control control)
  {
   if(!(control is Button)&&!(control is ComboBoxItem)&&!(control is MenuFlyoutItemBase)&&!(control is Windows.UI.Xaml.Controls.Primitives.ToggleButton toggle&&IsOnexSettingsSectionHeader(toggle)))return;
   var theme=_onexAppliedTheme??OnexPalette.CreateTheme();
   var background=control.Background is SolidColorBrush solid&&solid.Color.A>0?solid.Color:
    control.Background is LinearGradientBrush gradient&&gradient.GradientStops.Count>0?gradient.GradientStops.Last().Color:
    control is MenuFlyoutItemBase||control is Windows.UI.Xaml.Controls.Primitives.ToggleButton?theme.CardBackground:theme.ButtonBackground;
   var ink=(control.Foreground as SolidColorBrush)?.Color??theme.TextPrimary;
   if(control is ComboBoxItem)
   {
    // A row can change selection while its CommonStates setter is active.
    // Keep interaction ink and surface independent of IsSelected; selection
    // remains visible through the existing check and indicator bindings.
    background=theme.ButtonBackground;ink=theme.TextPrimary;
    SetOnexThemeResourceGroup(control,"choice-interaction-ink",new[]{"SystemControlForegroundBaseHighBrush"},ink);
   }
   // Move the hover/pressed surface away from its text luminance. This adds
   // feedback while increasing contrast, instead of dimming the entire row.
   double direction=OnexLuminance(ink)>=OnexLuminance(background)?-1:1;
   if(control is ComboBoxItem)
   {
    // Own these references from their first assignment. State setters retain
    // the same brushes through palette changes instead of an inherited alias.
    SetOnexThemeResourceGroup(control,"choice-interaction-hover",new[]{"SystemControlHighlightListLowBrush"},ThemeColors.Shade(background,direction*.06));
    SetOnexThemeResourceGroup(control,"choice-interaction-pressed",new[]{"SystemControlHighlightListMediumBrush"},ThemeColors.Shade(background,direction*.12));
    return;
   }
   SetOnexThemeResource(control,"SystemControlHighlightListLowBrush",ThemeColors.Shade(background,direction*.06));
   SetOnexThemeResource(control,"SystemControlHighlightListMediumBrush",ThemeColors.Shade(background,direction*.12));
  }
  private void ConfigureOnexChoicePicker(ComboBox choice)
  {
   if(_onexChoicePaletteParents.TryGetValue(choice,out var existing))return;
   _onexChoicePaletteParents.Add(choice,new object());
   void Paint()
   {
    if(!choice.IsDropDownOpen)return;
    for(int n=0;n<choice.Items.Count;n++)if(choice.ContainerFromIndex(n) is ComboBoxItem row){ConfigureOnexFocus(row);ConfigureOnexChoiceColours(row);}
   }
   choice.DropDownOpened+=(sender,args)=>
   {Paint();_=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,Paint);};
   choice.SelectionChanged+=(sender,args)=>Paint();
  }
 }
}
