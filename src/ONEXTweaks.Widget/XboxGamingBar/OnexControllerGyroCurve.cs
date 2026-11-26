// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private StackPanel _onexControllerGyroCurvePanel,_onexControllerGyroCurveDetails;
  private CheckBox _onexControllerGyroCurveEnable;private Button _onexControllerGyroCurveReset;
  private readonly Dictionary<string,Slider> _onexControllerGyroCurveSliders=new Dictionary<string,Slider>();
  private readonly List<Control[]> _onexControllerGyroCurveRows=new List<Control[]>();
  private static readonly Dictionary<string,int> OnexGyroCurveDefaults=new Dictionary<string,int>{{"sensitivity",100},{"deadzone",2},{"minInput",0},{"maxInput",220},{"minOutput",0},{"maxOutput",100},{"power",100},{"precisionSpeed",0},{"outputMix",0}};
  private void BuildOnexControllerGyroCurve(StackPanel parent){
   var panel=_onexControllerGyroCurvePanel=GyroSectionBody(parent,"curve","Stick response curve","Off · standard stick response","Enable custom response to reveal curve controls. Curve deadzone and minimum input reject small motions; Min/Max output and power shape stick contribution. Available for stick targets, not Mouse.");
   _onexControllerGyroCurveEnable=new CheckBox{Name="OnexControllerGyroCurveEnable",Content=global::XboxGamingBar.OnexStrings.Get("Use custom gyro-to-stick response")};OnexInputGuard.Wire(_onexControllerGyroCurveEnable);OnexAccentResources(_onexControllerGyroCurveEnable,OnexPalette.Magenta);panel.Children.Add(_onexControllerGyroCurveEnable);
   _onexControllerGyroCurveEnable.Click+=(s,e)=>{if(!_onexControllerSync)QueueOnexControllerGyroCurve("enabled",JsonValue.CreateBooleanValue(_onexControllerGyroCurveEnable.IsChecked==true));};
   var details=_onexControllerGyroCurveDetails=new StackPanel{Name="OnexControllerGyroCurveDetails",Spacing=10,Visibility=Visibility.Collapsed};panel.Children.Add(details);
   StackPanel Make(string key,string caption,int min,int max,int gamepadStep,string unit){
    var column=new StackPanel{Spacing=4};var text=ControllerHelp("");text.HorizontalAlignment=HorizontalAlignment.Right;column.Children.Add(OnexSettingsColumns(ControllerHelp(caption),text));
    var slider=new Slider{Name="OnexControllerGyroCurve_"+key,Minimum=min,Maximum=max,StepFrequency=1,SmallChange=gamepadStep,LargeChange=gamepadStep,Style=(Style)Resources["OnexPreciseSliderStyle"]};column.Children.Add(slider);_onexControllerGyroCurveSliders[key]=slider;_onexControllerControls.Add(slider);
    void Label(){text.Text=unit=="x"?(slider.Value/100).ToString("0.00",CultureInfo.InvariantCulture)+"×":key=="outputMix"?slider.Value==0?global::XboxGamingBar.OnexStrings.Get("Balanced"):(slider.Value>0?global::XboxGamingBar.OnexStrings.Get("Horizontal +"):global::XboxGamingBar.OnexStrings.Get("Vertical +"))+Math.Abs(slider.Value).ToString("0")+"%":slider.Value==0&&(key=="deadzone"||key=="minInput"||key=="precisionSpeed")?global::XboxGamingBar.OnexStrings.Get("Off"):slider.Value.ToString("0")+unit;}
    Action commit=()=>{if(!_onexControllerSync)QueueOnexControllerGyroCurve(key,JsonValue.CreateNumberValue(slider.Value));};OnexInputGuard.Wire(slider,commit);if(unit=="%"||unit=="x")OnexInputGuard.ConfigurePercentage(slider);
    slider.ValueChanged+=(s,e)=>{Label();if(!_onexControllerSync&&OnexInputGuard.IsActive(slider))commit();};Label();return column;
   }
   void Pair(string a,string ac,int amin,int amax,int astep,string au,string b,string bc,int bmin,int bmax,int bstep,string bu){details.Children.Add(OnexSettingsColumns(Make(a,ac,amin,amax,astep,au),Make(b,bc,bmin,bmax,bstep,bu)));_onexControllerGyroCurveRows.Add(new Control[]{_onexControllerGyroCurveSliders[a],_onexControllerGyroCurveSliders[b]});}
   Pair("sensitivity",OnexStrings.Get("Curve sensitivity"),1,400,5,"x","deadzone",OnexStrings.Get("Curve deadzone"),0,50,1,"°/s");
   Pair("minInput",OnexStrings.Get("Min input speed"),0,100,1,"°/s","maxInput",OnexStrings.Get("Full-output speed"),50,720,5,"°/s");
   Pair("minOutput",OnexStrings.Get("Min stick output"),0,100,5,"%","maxOutput",OnexStrings.Get("Max stick output"),1,100,5,"%");
   Pair("power","Power curve",10,400,5,"x","precisionSpeed",OnexStrings.Get("Precision range"),0,100,1,"°/s");
   var mix=Make("outputMix","Output mix",-100,100,5,"%");_onexControllerGyroCurveReset=new Button{Name="OnexControllerGyroCurveReset",Content=global::XboxGamingBar.OnexStrings.Get("Restore Curve Defaults"),Style=(Style)Resources["StandaloneButtonStyle"],MinHeight=38,Padding=new Thickness(10,6,10,6)};_onexControllerControls.Add(_onexControllerGyroCurveReset);_onexControllerGyroCurveReset.Click+=(s,e)=>{if(!_onexControllerSync){var c=new JsonObject{{"enabled",JsonValue.CreateBooleanValue(_onexControllerGyroCurveEnable.IsChecked==true)}};foreach(var item in OnexGyroCurveDefaults)c[item.Key]=JsonValue.CreateNumberValue(item.Value);QueueOnexControllerGyroProcessing("stickCurve",c);}};
   details.Children.Add(OnexSettingsColumns(mix,_onexControllerGyroCurveReset));_onexControllerGyroCurveRows.Add(new Control[]{_onexControllerGyroCurveSliders["outputMix"],_onexControllerGyroCurveReset});
   details.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Input speed is measured after axis settings, sensitivity and filtering. The larger of Deadzone / Min input speed is ignored. Example: 220°/s reaches Max output with sensitivity 1.00×. Raise sensitivity for quicker turns.")));
   details.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Power 1.00× is linear; 2.00× makes small movements gentler; 0.50× makes them stronger. Precision range slows movements near the threshold. Example: 30°/s adds fine control during slow tilts.")));
   details.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Min output helps cross a game's stick deadzone; Max output caps the gyro contribution. Example: 20% / 70% stays between those radii while moving. Physical stick input is added afterwards. Output mix +50% halves vertical motion; −50% halves horizontal motion.")));
  }
  private void QueueOnexControllerGyroCurve(string key,IJsonValue value){
   var p=OnexMotionObject(OnexMotionObject(_onexControllerPending??ControllerEditorPolicy,"gyro"),"processing");var old=OnexMotionObject(p,"stickCurve");var c=old==null?new JsonObject():JsonObject.Parse(old.Stringify());foreach(var item in OnexGyroCurveDefaults)if(!c.ContainsKey(item.Key))c[item.Key]=JsonValue.CreateNumberValue(item.Value);if(!c.ContainsKey("enabled"))c["enabled"]=JsonValue.CreateBooleanValue(false);c[key]=value;
   double N(string name)=>c.GetNamedNumber(name);void Set(string name,double v){c[name]=JsonValue.CreateNumberValue(v);}
   if(key=="minOutput"&&N("minOutput")>N("maxOutput"))Set("maxOutput",N("minOutput"));if(key=="maxOutput"&&N("minOutput")>N("maxOutput"))Set("minOutput",N("maxOutput"));
   if(key=="minInput"||key=="deadzone"){double lower=Math.Max(N("minInput"),N("deadzone"));if(N("maxInput")<=lower)Set("maxInput",lower+1);}
   if(key=="maxInput"){if(N("minInput")>=N("maxInput"))Set("minInput",N("maxInput")-1);if(N("deadzone")>=N("maxInput"))Set("deadzone",N("maxInput")-1);}
   QueueOnexControllerGyroProcessing("stickCurve",c);
  }
  private void ApplyOnexControllerGyroCurve(JsonObject processing){
   if(_onexControllerGyroCurveEnable==null)return;var c=OnexMotionObject(processing,"stickCurve");bool enabled=c?.GetNamedBoolean("enabled",false)==true;_onexControllerGyroCurveEnable.IsChecked=enabled;_onexControllerGyroCurveDetails.Visibility=enabled?Visibility.Visible:Visibility.Collapsed;
   foreach(var item in OnexGyroCurveDefaults){var slider=_onexControllerGyroCurveSliders[item.Key];if(!OnexInputGuard.IsEditing(slider))slider.Value=c?.GetNamedNumber(item.Key,item.Value)??item.Value;}
  }
  private void RefreshOnexControllerGyroCurve(bool enabled){
   if(_onexControllerGyroCurveEnable==null)return;bool stick=enabled&&GyroChoice(_onexControllerGyroTarget)!="Mouse";_onexControllerGyroCurveEnable.IsEnabled=stick;foreach(var s in _onexControllerGyroCurveSliders.Values)s.IsEnabled=stick;_onexControllerGyroCurveReset.IsEnabled=stick;
   _onexControllerGyroFloor.IsEnabled=stick&&_onexControllerGyroCurveEnable.IsChecked!=true;
  }
  private IEnumerable<Control[]> OnexControllerGyroCurveRows(){yield return new Control[]{_onexControllerGyroCurveEnable};if(_onexControllerGyroCurveDetails?.Visibility==Visibility.Visible)foreach(var row in _onexControllerGyroCurveRows)yield return row;}
 }
}
