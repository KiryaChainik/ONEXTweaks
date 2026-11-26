// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    internal static class OnexBatteryCapIndicator
    {
        internal static Color ColorFor(double? battery,bool capKnown,int? cap,bool saver,Color? normal=null,bool light=false)
        {
            switch(OnexBatteryCapPolicy.Select(battery,capKnown,cap,saver))
            {
                case OnexBatteryTone.AtCapGreen:return light?Color.FromArgb(255,24,125,42):Color.FromArgb(255,92,196,106);
                case OnexBatteryTone.EnergySaverYellow:return light?Color.FromArgb(255,128,88,0):Color.FromArgb(255,255,211,64);
                default:return normal??Colors.White;
            }
        }
        internal static void Apply(TextBlock value,double? battery,bool capKnown,int? cap,bool saver,Color? normal=null,bool light=false)
        {if(value!=null)value.Foreground=new SolidColorBrush(ColorFor(battery,capKnown,cap,saver,normal,light));}

        // A narrowly armed check creates real XAML controls without constructing a
        // widget, activating a window or connecting to the helper. It can therefore
        // run while the owner's independent colour-calibration window stays active.
        internal static void VerifyHeadless()
        {
            var report=new JsonObject();var cases=new JsonArray();bool passed=true;
            void Check(string name,double? battery,bool known,int? cap,bool saver,string expected)
            {
                var control=new TextBlock{Text=battery.HasValue?battery.Value.ToString():"—"};Apply(control,battery,known,cap,saver);
                string actual=((SolidColorBrush)control.Foreground).Color.ToString();bool ok=actual==expected;passed&=ok;
                cases.Add(new JsonObject{{"case",JsonValue.CreateStringValue(name)},{"expected",JsonValue.CreateStringValue(expected)},{"actual",JsonValue.CreateStringValue(actual)},{"passed",JsonValue.CreateBooleanValue(ok)}});
            }
            try
            {
                Check("below90",89,true,90,false,"#FFFFFFFF");Check("at90",90,true,90,false,"#FF5CC46A");Check("above90",91,true,90,false,"#FF5CC46A");
                Check("saverBelowCap",89,true,90,true,"#FFFFD340");Check("greenOverridesSaver",90,true,90,true,"#FF5CC46A");
                Check("disabledLimitBelow100",99,true,100,false,"#FFFFFFFF");Check("disabledLimitAt100",100,true,100,true,"#FF5CC46A");
                Check("newCapImmediatelyHigher",90,true,95,true,"#FFFFD340");Check("newCapImmediatelyLower",90,true,85,true,"#FF5CC46A");
                Check("unknownCap",100,false,90,false,"#FFFFFFFF");Check("unknownCapSaver",100,false,null,true,"#FFFFD340");
                Check("missingBattery",null,true,90,true,"#FFFFFFFF");Check("invalidBattery",101,true,90,true,"#FFFFFFFF");Check("negativeBattery",-1,true,90,true,"#FFFFFFFF");
                Check("notFiniteBattery",double.NaN,true,90,true,"#FFFFFFFF");Check("invalidCap",95,true,0,false,"#FFFFFFFF");
                report["cases"]=cases;
            }
            catch(Exception error){passed=false;report["error"]=JsonValue.CreateStringValue(error.Message);}
            report["passed"]=JsonValue.CreateBooleanValue(passed);report["nativeXamlControls"]=JsonValue.CreateBooleanValue(true);
            report["syntheticTelemetry"]=JsonValue.CreateBooleanValue(true);report["physicalChargingTested"]=JsonValue.CreateBooleanValue(false);
            report["windowActivated"]=JsonValue.CreateBooleanValue(false);report["helperConnected"]=JsonValue.CreateBooleanValue(false);report["hardwareWrites"]=JsonValue.CreateNumberValue(0);
            File.WriteAllText(Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-battery-cap-native.json"),report.Stringify());
        }
        internal static bool QuietUpdateArmed()
        {
            try{string path=Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-quiet-update-until.txt");return File.Exists(path)&&DateTime.TryParse(File.ReadAllText(path),out var until)&&until.ToUniversalTime()>DateTime.UtcNow&&until.ToUniversalTime()<DateTime.UtcNow.AddMinutes(10);}catch{return false;}
        }
    }
}
