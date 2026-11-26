using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private void ApplyOnexHintContrast(ThemeColors theme)
  {
   foreach(var badge in new[]{OnexLtHint,OnexRtHint})
   {
    if(badge==null)continue;
    SetOnexThemeBrush(badge,Border.BackgroundProperty,theme.ButtonBackground);
    SetOnexThemeBrush(badge,Border.BorderBrushProperty,theme.CardBorder);
    badge.BorderThickness=new Thickness(1);badge.CornerRadius=new CornerRadius(4);badge.Opacity=1;
    if(badge.Child is TextBlock text){SetOnexThemeBrush(text,TextBlock.ForegroundProperty,theme.TextPrimary);text.Opacity=1;}
   }
  }
 }
}
