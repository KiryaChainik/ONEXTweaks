// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexGyroEditorSection {internal string Id;internal ToggleButton Header;internal Button Help;internal StackPanel Body;internal TextBlock Summary;internal FontIcon Chevron;}
  private readonly List<OnexGyroEditorSection> _onexGyroSections=new List<OnexGyroEditorSection>();
  private string _onexGyroOpenSection,_onexGyroLastTarget;
  private StackPanel GyroSectionBody(StackPanel parent,string id,string title,string summary,string help)
  {
   var section=new OnexGyroEditorSection{Id=id};
   var host=new StackPanel{Spacing=6};var line=new Grid{ColumnSpacing=8};line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var text=new StackPanel{Spacing=3};text.Children.Add(new TextBlock{Text=OnexStrings.Get(title),FontSize=16,FontWeight=Windows.UI.Text.FontWeights.SemiBold});
   section.Summary=ControllerHelp(OnexStrings.Get(summary));text.Children.Add(section.Summary);
   var content=new Grid{ColumnSpacing=10};content.ColumnDefinitions.Add(new ColumnDefinition());content.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});content.Children.Add(text);section.Chevron=new FontIcon{Glyph="\uE70D",FontSize=12,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(section.Chevron,1);content.Children.Add(section.Chevron);
   section.Header=new ToggleButton{Name="OnexGyroSection_"+id,Content=content,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12,9,12,9),MinHeight=48};OnexInputGuard.Wire(section.Header);line.Children.Add(section.Header);
   section.Help=new Button{Name="OnexGyroSectionHelp_"+id,Content="?",Width=36,Height=36,MinWidth=0,Padding=new Thickness(0),VerticalAlignment=VerticalAlignment.Center};OnexInputGuard.Wire(section.Help);Grid.SetColumn(section.Help,1);line.Children.Add(section.Help);
   var flyout=TrackOnexMenu(new Flyout{Content=new TextBlock{Text=OnexStrings.Get(help),TextWrapping=TextWrapping.Wrap,MaxWidth=420,FontSize=14}});section.Help.Click+=(s,e)=>flyout.ShowAt(section.Help);
   section.Body=new StackPanel{Name="OnexGyroSectionBody_"+id,Spacing=10,Margin=new Thickness(10,4,10,6),Visibility=Visibility.Collapsed};host.Children.Add(line);host.Children.Add(section.Body);parent.Children.Add(host);_onexGyroSections.Add(section);
   section.Header.Click+=(s,e)=>OpenOnexGyroSection(_onexGyroOpenSection==id?null:id);
   return section.Body;
  }
  private void OpenOnexGyroSection(string id)
  {
   _onexGyroOpenSection=id;
   foreach(var item in _onexGyroSections){bool open=item.Id==id;item.Header.IsChecked=open;item.Chevron.Glyph=open?"\uE70E":"\uE70D";item.Body.Visibility=open?Visibility.Visible:Visibility.Collapsed;}
   WireOnexControllerNavigation();ApplyOnexPageFocus();
  }
  private void SetOnexGyroSectionSummary(string id,string text){var section=_onexGyroSections.FirstOrDefault(s=>s.Id==id);if(section!=null)section.Summary.Text=text;}
  private bool OnexGyroControlVisible(Control control)
  {
   if(control==null)return false;
   for(DependencyObject node=control;node!=null;node=VisualTreeHelper.GetParent(node))if(node is FrameworkElement element&&element.Visibility!=Visibility.Visible)return false;
   return true;
  }
  private IEnumerable<Control[]> OnexGyroSectionRows(string id,IEnumerable<Control[]> children)
  {
   var section=_onexGyroSections.FirstOrDefault(s=>s.Id==id);if(section==null)yield break;
   yield return new Control[]{section.Header,section.Help};
   if(section.Body.Visibility==Visibility.Visible)foreach(var row in children){var visible=row.Where(OnexGyroControlVisible).ToArray();if(visible.Length>0)yield return visible;}
  }
  private void RefreshOnexGyroSectionSummaries()
  {
   if(_onexControllerGyroTarget==null)return;
   string target=GyroChoice(_onexControllerGyroTarget);string engine=GyroChoice(_onexControllerGyroEngine)??"Linear";
   SetOnexGyroSectionSummary("aim",OnexStrings.Get(target=="Disabled"?"Settings only · aiming is Off":"Activation and sensitivity"));
   SetOnexGyroSectionSummary("processing",OnexStrings.Get(engine)+(engine=="Linear"?"":OnexStrings.Format(" · smoothing {0}%",Math.Round(_onexControllerGyroSmoothing.Value))));
   SetOnexGyroSectionSummary("thresholds",OnexStrings.Format("Gate {0}°/s · anti-deadzone {1}%",(_onexControllerGyroGate.Value/10).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),Math.Round(_onexControllerGyroFloor.Value)));
   SetOnexGyroSectionSummary("axes",OnexStrings.Get(_onexControllerGyroWorld.IsChecked==true?"Gravity-relative axes":"Console axes")+" · "+(GyroChoice(_onexControllerGyroRotation)??"0")+"°");
   SetOnexGyroSectionSummary("curve",OnexStrings.Get(_onexControllerGyroCurveEnable.IsChecked==true?"On · custom stick response":"Off · standard stick response"));
   SetOnexGyroSectionSummary("calibration",OnexStrings.Get(OnexMotionObject(OnexMotionObject(_onexControllerState,"motion"),"calibration")!=null?"Saved idle correction":"No saved idle correction"));
   if(_onexGyroLastTarget!=target){if(target!="Disabled"&&_onexGyroLastTarget=="Disabled")OpenOnexGyroSection("aim");_onexGyroLastTarget=target;}
  }
 }
}
