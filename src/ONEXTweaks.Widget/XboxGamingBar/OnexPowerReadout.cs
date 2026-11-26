// SPDX-License-Identifier: AGPL-3.0-only
using Windows.UI.Xaml;
namespace XboxGamingBar
{
 // Explicit text role: watts are data, independent of the slider accent.
 public sealed class OnexPowerReadout:DependencyObject
 {
  public static readonly DependencyProperty UsesTrackAccentProperty=DependencyProperty.RegisterAttached("UsesTrackAccent",typeof(bool),typeof(OnexPowerReadout),new PropertyMetadata(false));
  public static bool GetUsesTrackAccent(DependencyObject element)=>(bool)element.GetValue(UsesTrackAccentProperty);
  public static void SetUsesTrackAccent(DependencyObject element,bool value)=>element.SetValue(UsesTrackAccentProperty,value);
  public static readonly DependencyProperty IsNeutralProperty=DependencyProperty.RegisterAttached("IsNeutral",typeof(bool),typeof(OnexPowerReadout),new PropertyMetadata(false));
  public static bool GetIsNeutral(DependencyObject value)=>value!=null&&value.GetValue(IsNeutralProperty) is bool neutral&&neutral;
  public static void SetIsNeutral(DependencyObject value,bool neutral)=>value.SetValue(IsNeutralProperty,neutral);
 }
}
