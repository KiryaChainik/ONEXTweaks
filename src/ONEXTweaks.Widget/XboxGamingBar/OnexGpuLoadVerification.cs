// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexGpuLoadAsync(JsonObject report)
        {
            var oldVisibility=QuickMetricsRow.Visibility;QuickMetricsRow.Visibility=Visibility.Visible;
            try
            {
                for(int i=0;i<60&&(_onexStatsPacket==null||!_onexStatsPacket.ContainsKey("gpuUsage"));i++)await Task.Delay(100);
                if(_onexStatsPacket==null||!_onexStatsPacket.ContainsKey("gpuUsage"))throw new InvalidOperationException("Intel GPU activity unavailable");
                double actual=_onexStatsPacket.GetNamedNumber("gpuUsage");if(double.IsNaN(actual)||double.IsInfinity(actual)||actual<0||actual>100)throw new InvalidOperationException("Invalid GPU activity");
                string source=_onexStatsPacket.GetNamedString("gpuUsageSource","");if(!source.Contains("Intel IGCL"))throw new InvalidOperationException("Unexpected GPU data source");
                if(OnexStats.Length!=8||OnexStats[1][0]!="cpuTemp"||OnexStats[2][0]!="gpuUsage"||Grid.GetColumn(_onexStatsCells["gpuUsage"])!=2)throw new InvalidOperationException("GPU load metric order incorrect");
                QuickMetricsGrid.UpdateLayout();double? top=null;
                foreach(var spec in OnexStats)
                {
                    var cell=_onexStatsCells[spec[0]];var value=_onexStatsValues[spec[0]];double y=cell.TransformToVisual(QuickMetricsGrid).TransformPoint(new Windows.Foundation.Point()).Y;
                    if(Grid.GetRow(cell)!=0||value.FontSize!=16||value.ActualWidth>cell.ActualWidth+.5||cell.ActualWidth<=0||(top.HasValue&&Math.Abs(top.Value-y)>.5))throw new InvalidOperationException("Metrics no longer fit one row at original size");
                    if(((StackPanel)cell).Children.OfType<TextBlock>().Last().FontSize!=11)throw new InvalidOperationException("Metric label font changed");top=y;
                }
                string saved=_onexStatsPacket.Stringify();
                try{var missing=JsonObject.Parse(saved);missing.Remove("gpuUsage");ApplyOnexStatsPacket(missing);if(_onexStatsValues["gpuUsage"].Text!="—")throw new InvalidOperationException("Unavailable GPU sensor displayed a fake zero");}
                finally{ApplyOnexStatsPacket(JsonObject.Parse(saved));}
                await SaveOnexImage("onex-stats-gpu-load.png",QuickMetricsGrid,0);
                report["metricOrder"]=JsonArray.Parse("["+string.Join(",",OnexStats.Select(s=>"\""+s[1]+"\""))+"]");report["gpuPercent"]=JsonValue.CreateNumberValue(actual);report["gpuSource"]=JsonValue.CreateStringValue(source);report["unavailableShowsDash"]=JsonValue.CreateBooleanValue(true);report["singleRowOriginalFontSizes"]=JsonValue.CreateBooleanValue(true);
            }
            finally{QuickMetricsRow.Visibility=oldVisibility;}
        }
    }
}
