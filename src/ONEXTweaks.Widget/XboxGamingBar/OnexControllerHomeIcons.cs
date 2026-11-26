// SPDX-License-Identifier: AGPL-3.0-only
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using XboxGamingBar.QuickSettings;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private const string OnexControllerProfileGlyph="F0 M10 1 A5 5 0 1 1 10 11 A5 5 0 1 1 10 1 Z M2 25 V20 C2 15 5 13 10 13 C13 13 16 14 18 17 L14 19 L12 25 Z M18 18 H26 C28 18 29 19 30 22 L31 28 C31.5 31 29 32 27 30 L24 27 H20 L17 30 C15 32 12.5 31 13 28 L14 22 C15 19 16 18 18 18 Z M17 21 V23 H15 V25 H17 V27 H19 V25 H21 V23 H19 V21 Z M26 21 A1 1 0 1 1 26 23 A1 1 0 1 1 26 21 Z M28 24 A1 1 0 1 1 28 26 A1 1 0 1 1 28 24 Z";
  private FrameworkElement CreateOnexControllerHomeIcon(string id,double size,Brush foreground)
  {
   IconElement icon;
   if(id=="OnexControllerProfile")icon=(PathIcon)Windows.UI.Xaml.Markup.XamlReader.Load("<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\""+OnexControllerProfileGlyph+"\"/>");
   else if(OnexHomeKeyboardMouse){string data="F0 "+string.Join(" ",System.Linq.Enumerable.Select(OnexKeyboardMouseGlyphPaths,path=>path.StartsWith("F0 ",StringComparison.Ordinal)?path.Substring(3):path));icon=(PathIcon)Windows.UI.Xaml.Markup.XamlReader.Load("<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\""+data+"\"/>");}
   else icon=new BitmapIcon{UriSource=new Uri("ms-appx:///Assets/ButtonIcons/controller_xboxseries.png"),ShowAsMonochrome=true};
   icon.Foreground=foreground??tileIconBrush??OnexSettingsBrush(255,255,255);
   return new Viewbox{Width=size,Height=size,Stretch=Stretch.Uniform,Child=icon,Tag=id=="OnexInputMode"?(OnexHomeKeyboardMouse?"keyboardMouse":"gamepad"):"profile",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  }
  private void UpdateOnexHomeInputModeIcon(TileDefinition tile)
  {
   if(!(tile.IconElement is Viewbox box))return;string mode=OnexHomeKeyboardMouse?"keyboardMouse":"gamepad";if(box.Tag as string==mode)return;
   var updated=(Viewbox)CreateOnexControllerHomeIcon("OnexInputMode",box.Width,OnexMainTileIcon(tile)?.Foreground);var child=updated.Child;updated.Child=null;box.Child=child;box.Tag=mode;
  }
 }
}
