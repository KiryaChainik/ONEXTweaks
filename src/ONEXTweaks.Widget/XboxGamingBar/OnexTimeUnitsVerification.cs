// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexTimeUnitsAsync(JsonObject report)
        {
            if(_onexStatsPacket==null)throw new InvalidOperationException("No real metrics packet to preserve");string saved=_onexStatsPacket.Stringify();var oldVisibility=QuickMetricsRow.Visibility;QuickMetricsRow.Visibility=Windows.UI.Xaml.Visibility.Visible;
            try
            {
                var cases=new[]{Tuple.Create(0d,"~0h"),Tuple.Create(1d/60,"<0.01h"),Tuple.Create(1d,"~0.02h"),Tuple.Create(20d,"~0.33h"),Tuple.Create(30d,"~0.5h"),Tuple.Create(45d,"~0.75h"),Tuple.Create(60d,"~1h"),Tuple.Create(90d,"~1.5h"),Tuple.Create(120d,"~2h"),Tuple.Create(6000d,"~99h+")};
                var results=new JsonArray();
                foreach(var item in cases)
                {
                    var packet=JsonObject.Parse(saved);packet["isCharging"]=JsonValue.CreateBooleanValue(false);packet["timeRemaining"]=JsonValue.CreateNumberValue(item.Item1);ApplyOnexStatsPacket(packet);
                    string actual=_onexStatsValues["timeRemaining"].Text;if(actual!=item.Item2)throw new InvalidOperationException("Time conversion mismatch for minutes="+item.Item1+": "+actual);
                    results.Add(new JsonObject{{"minutes",JsonValue.CreateNumberValue(item.Item1)},{"text",JsonValue.CreateStringValue(actual)}});
                }
                var charging=JsonObject.Parse(saved);charging["isCharging"]=JsonValue.CreateBooleanValue(true);charging["timeRemaining"]=JsonValue.CreateNumberValue(90);ApplyOnexStatsPacket(charging);if(_onexStatsValues["timeRemaining"].Text!=OnexChargingIndicator)throw new InvalidOperationException("Charging runtime indicator changed");
                var missing=JsonObject.Parse(saved);missing["isCharging"]=JsonValue.CreateBooleanValue(false);missing.Remove("timeRemaining");ApplyOnexStatsPacket(missing);if(_onexStatsValues["timeRemaining"].Text!="—")throw new InvalidOperationException("Unknown runtime fabricated");
                bool captured=false;
                for(int attempt=0;attempt<3;attempt++)
                {
                    var example=JsonObject.Parse(saved);example["isCharging"]=JsonValue.CreateBooleanValue(false);example["timeRemaining"]=JsonValue.CreateNumberValue(90);ApplyOnexStatsPacket(example);QuickMetricsGrid.UpdateLayout();
                    var value=_onexStatsValues["timeRemaining"];var cell=_onexStatsCells["timeRemaining"];if(value.FontSize!=16||value.ActualWidth>cell.ActualWidth+.5)throw new InvalidOperationException("Hours suffix clipped or font changed");
                    await SaveOnexImage("onex-time-left-hours-example.png",cell,0);if(value.Text=="~1.5h"){captured=true;break;}
                }
                if(!captured)throw new InvalidOperationException("Live updates prevented formatting capture");
                report["sourceUnits"]=JsonValue.CreateStringValue("minutes = Windows remaining seconds / 60");report["displayUnits"]=JsonValue.CreateStringValue("decimal hours with explicit h suffix");report["cases"]=results;report["chargingAndUnavailablePreserved"]=JsonValue.CreateBooleanValue(true);report["exampleImageUsesSynthetic90MinuteFixture"]=JsonValue.CreateBooleanValue(true);report["physicalDischargeTested"]=JsonValue.CreateBooleanValue(false);report["fontSizePreserved"]=JsonValue.CreateBooleanValue(true);
            }
            finally{ApplyOnexStatsPacket(JsonObject.Parse(saved));QuickMetricsRow.Visibility=oldVisibility;}
        }
    }
}
