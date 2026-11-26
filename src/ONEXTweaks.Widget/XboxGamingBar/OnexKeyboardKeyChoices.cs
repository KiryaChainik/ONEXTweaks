// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  // Keep a stable value/index mapping. Hundreds of closed keyboard choices
  // need strings, not a separate native ComboBoxItem for every key/slot.
  private static readonly int[] OnexKeyboardChoiceCodes=new[]{0}
   .Concat(Enumerable.Range(65,26)).Concat(Enumerable.Range(48,10))
   .Concat(Enumerable.Range(112,24)).Concat(Enumerable.Range(96,16))
   .Concat(new[]{19,20,144,145,91,92,93,8,9,13,27,32,33,34,35,36,37,38,39,40,44,45,46,160,161,162,163,164,165,166,167,173,174,175,176,177,178,179,186,187,188,189,190,191,192,219,220,221,222,226,269}).ToArray();

  private static string OnexVirtualKeyDisplayName(int key)
  {
   // ONEX bridge bindings carry Windows virtual keys. The imported controller
   // editor uses USB HID usages, where the same numbers name different keys.
   if(key>=65&&key<=90||key>=48&&key<=57)return ((char)key).ToString();
   if(key>=112&&key<=135)return "F"+(key-111);
   if(key>=96&&key<=105)return OnexStrings.Format("Numpad {0}",key-96);
   switch(key)
   {
    case 8:return OnexStrings.Get("Backspace");
    case 9:return OnexStrings.Get("Tab");
    case 12:return OnexStrings.Get("Clear");
    case 13:return OnexStrings.Get("Enter");
    case 16:return "Shift";
    case 17:return "Ctrl";
    case 18:return "Alt";
    case 19:return OnexStrings.Get("Pause");
    case 20:return OnexStrings.Get("Caps Lock");
    case 27:return OnexStrings.Get("Esc");
    case 32:return OnexStrings.Get("Space");
    case 33:return OnexStrings.Get("Page Up");
    case 34:return OnexStrings.Get("Page Down");
    case 35:return OnexStrings.Get("End");
    case 36:return OnexStrings.Get("Home");
    case 37:return OnexStrings.Get("Left");
    case 38:return OnexStrings.Get("Up");
    case 39:return OnexStrings.Get("Right");
    case 40:return OnexStrings.Get("Down");
    case 44:return OnexStrings.Get("Print Screen");
    case 45:return OnexStrings.Get("Insert");
    case 46:return OnexStrings.Get("Delete");
    case 91:return "Win";
    case 92:return OnexStrings.Get("Right Win");
    case 93:return OnexStrings.Get("Context menu key");
    case 106:return OnexStrings.Format("Numpad {0}","*");
    case 107:return OnexStrings.Format("Numpad {0}","+");
    case 108:return OnexStrings.Get("Numpad separator");
    case 109:return OnexStrings.Format("Numpad {0}","-");
    case 110:return OnexStrings.Format("Numpad {0}",".");
    case 111:return OnexStrings.Format("Numpad {0}","/");
    case 144:return OnexStrings.Get("Num Lock");
    case 145:return OnexStrings.Get("Scroll Lock");
    case 160:return OnexStrings.Get("Left Shift");
    case 161:return OnexStrings.Get("Right Shift");
    case 162:return OnexStrings.Get("Left Ctrl");
    case 163:return OnexStrings.Get("Right Ctrl");
    case 164:return OnexStrings.Get("Left Alt");
    case 165:return OnexStrings.Get("Right Alt");
    case 166:return OnexStrings.Get("Browser back");
    case 167:return OnexStrings.Get("Browser forward");
    case 168:return OnexStrings.Get("Browser refresh");
    case 169:return OnexStrings.Get("Browser stop");
    case 170:return OnexStrings.Get("Browser search");
    case 171:return OnexStrings.Get("Browser favorites");
    case 172:return OnexStrings.Get("Browser home");
    case 173:return OnexStrings.Get("Mute sound");
    case 174:return OnexStrings.Get("Volume down");
    case 175:return OnexStrings.Get("Volume up");
    case 176:return OnexStrings.Get("Next track");
    case 177:return OnexStrings.Get("Previous track");
    case 178:return OnexStrings.Get("Stop media");
    case 179:return OnexStrings.Get("Play / pause");
    case 186:return ";";
    case 187:return "=";
    case 188:return ",";
    case 189:return "-";
    case 190:return ".";
    case 191:return "/";
    case 192:return "`";
    case 219:return "[";
    case 220:case 226:return "\\";
    case 221:return "]";
    case 222:return "'";
    case 269:return OnexStrings.Get("Numpad Enter");
    default:return Enum.IsDefined(typeof(Windows.System.VirtualKey),(Windows.System.VirtualKey)key)
     ? ((Windows.System.VirtualKey)key).ToString()
     : OnexStrings.Format("Key {0}",key);
   }
  }
  private static string OnexKeyboardCaption(IEnumerable<int> keys,bool sequence=false)
   =>string.Join(sequence?" → ":" + ",keys.Select(OnexVirtualKeyDisplayName));

  private void PopulateOnexKeyboardChoices(ComboBox choice)
  {
   choice.ItemsSource=OnexKeyboardChoiceCodes.Select(key=>key==0?OnexStrings.Get("None"):OnexVirtualKeyDisplayName(key)).ToArray();
   choice.SelectedIndex=0;
  }
  private static int SelectedOnexKeyboardChoice(ComboBox choice)
  {
   int index=choice?.SelectedIndex??-1;
   return index>=0&&index<OnexKeyboardChoiceCodes.Length?OnexKeyboardChoiceCodes[index]:0;
  }
  private static void SelectOnexKeyboardChoice(ComboBox choice,int key)
  {
   choice.SelectedIndex=Math.Max(0,Array.IndexOf(OnexKeyboardChoiceCodes,key));
  }
 }
}
