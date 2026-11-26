// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Data.Json;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private const string OnexChargingIndicator="\u26A1\uFE0E";
        private static readonly string[][] OnexStats={
            new[]{"cpuUsage","CPU Load","%"},new[]{"cpuTemp","CPU Temp","°"},new[]{"gpuUsage","GPU Load","%"},
            new[]{"memoryUsage","Memory","%"},new[]{"systemPowerWatts","TDP Total"," W"},
            new[]{"apuPowerWatts","APU Power"," W"},new[]{"timeRemaining","Time Left",""},new[]{"batteryLevel","Battery","%"}};
        private readonly Dictionary<string,TextBlock> _onexStatsValues=new Dictionary<string,TextBlock>();
        private readonly Dictionary<string,FrameworkElement> _onexStatsCells=new Dictionary<string,FrameworkElement>();
        private JsonObject _onexStatsPacket;
        private void BuildOnexStatsGrid()
        {
            if(QuickMetricsGrid==null)return;
            QuickMetricsGrid.Children.Clear();QuickMetricsGrid.ColumnDefinitions.Clear();QuickMetricsGrid.RowDefinitions.Clear();
            _onexStatsValues.Clear();_onexStatsCells.Clear();
            for(int c=0;c<OnexStats.Length;c++)QuickMetricsGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            QuickMetricsGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            QuickMetricsGrid.ColumnSpacing=2;QuickMetricsGrid.RowSpacing=0;
            for(int i=0;i<OnexStats.Length;i++)
            {
                var spec=OnexStats[i];
                var value=new TextBlock{FontFamily=_onexUiTypeface??FontFamily,Text="—",FontSize=16,FontWeight=Windows.UI.Text.FontWeights.SemiBold,Foreground=new SolidColorBrush(_onexAppliedTheme?.TextPrimary??Colors.White),HorizontalAlignment=HorizontalAlignment.Center};
                var label=new TextBlock{FontFamily=_onexUiTypeface??FontFamily,Text=OnexStrings.Get(spec[1]),FontSize=11,Foreground=(Brush)Resources["HintTextBrush"],HorizontalAlignment=HorizontalAlignment.Center};
                var cell=new StackPanel{Spacing=3,HorizontalAlignment=HorizontalAlignment.Stretch};cell.Children.Add(value);cell.Children.Add(label);
                FrameworkElement host=cell;
                if(spec[0]=="batteryLevel")
                {
                    var button=new Button{FontFamily=_onexUiTypeface??FontFamily,Name="OnexBatteryStatButton",Content=cell,Padding=new Thickness(0),BorderThickness=new Thickness(0),MinWidth=0,MinHeight=0,Background=OnexSolidBrush(Colors.Transparent),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Stretch,IsTabStop=false};
                    foreach(string key in new[]{"ButtonBackgroundPointerOver","ButtonBackgroundPressed","ButtonBackgroundDisabled","ButtonBorderBrushPointerOver","ButtonBorderBrushPressed"})button.Resources[key]=OnexSolidBrush(Colors.Transparent);
                    button.Click+=async(s,e)=>await ShowOnexBatterySaverConfirmationAsync();_onexBatteryStatButton=button;host=button;
                }
                Grid.SetColumn(host,i);Grid.SetRow(host,0);QuickMetricsGrid.Children.Add(host);
                _onexStatsValues[spec[0]]=value;_onexStatsCells[spec[0]]=cell;
            }
            UpdateOnexStatsDisplay();
        }
        private void ApplyOnexStatsPacket(JsonObject packet)
        {
            _onexStatsPacket=packet;currentMetricsData.Clear();
            foreach(var item in packet)
                if(item.Value.ValueType==JsonValueType.Number)
                {double value=item.Value.GetNumber();if(!double.IsNaN(value)&&!double.IsInfinity(value))currentMetricsData[item.Key]=value;}
            currentMetricsIsCharging=packet.GetNamedBoolean("isCharging",false);
            UpdateOnexStatsDisplay();
        }
        private static string FormatOnexTimeLeft(double minutes)
        {
            if(double.IsNaN(minutes)||double.IsInfinity(minutes)||minutes<0)return "—";
            if(minutes>=6000)return "~99h+";
            double hours=Math.Round(minutes/60.0,2,MidpointRounding.AwayFromZero);
            return minutes>0&&hours==0?"<0.01h":"~"+hours.ToString("0.##",CultureInfo.InvariantCulture)+"h";
        }
        private void UpdateOnexStatsDisplay()
        {
            if(_onexStatsValues.Count!=OnexStats.Length)return;
            foreach(var spec in OnexStats)
            {
                SetOnexThemeBrush(_onexStatsValues[spec[0]],TextBlock.ForegroundProperty,_onexAppliedTheme?.TextPrimary??Colors.White);
                bool available=currentMetricsData.TryGetValue(spec[0],out var value)&&value>=0;
                if(spec[0]=="batteryLevel")available=available&&value<=100;
                if(spec[0]=="timeRemaining")available=available&&!currentMetricsIsCharging&&value<=10080;
                string format=spec[2]==" W"?"0.0":"0";
                bool externalPowerPlaceholder=!available&&currentMetricsIsCharging&&(spec[0]=="timeRemaining"||spec[0]=="systemPowerWatts");
                _onexStatsValues[spec[0]].Text=available?(spec[0]=="timeRemaining"?FormatOnexTimeLeft(value):value.ToString(format,CultureInfo.InvariantCulture)+spec[2]):externalPowerPlaceholder?OnexChargingIndicator:"—";
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexStatsValues[spec[0]],OnexStrings.Get(spec[1])+(externalPowerPlaceholder?global::XboxGamingBar.OnexStrings.Get(" · External power connected"):" · "+_onexStatsValues[spec[0]].Text));
                string source=_onexStatsPacket?.GetNamedString(spec[0]+"Source","")??"";
                if(string.IsNullOrEmpty(source))source=available?OnexStrings.Get(spec[1])+OnexStrings.Get(" · live reading"):OnexStrings.Get(spec[1])+OnexStrings.Get(" · sensor unavailable");
                if(spec[0]=="timeRemaining")source=OnexStrings.Get("Approximate remaining battery time in hours (h), estimated by Windows at the current load. For example, 1.5h means 1 hour 30 minutes. Updates while discharging; unavailable on AC or without an estimate.");
                if(externalPowerPlaceholder)source=spec[0]=="timeRemaining"?OnexStrings.Get("External power connected. Remaining battery runtime is shown while discharging."):OnexStrings.Get("External power connected. Whole-system discharge power is unavailable; this icon is not a power reading.");
                if(spec[0]=="gpuUsage")source=OnexStrings.Get("Actual average GPU activity across engines, measured between Intel driver samples. At idle a value below 0.5% rounds to 0%; missing data is shown as a dash.\n")+source;
                if(spec[0]=="apuPowerWatts")source=OnexStrings.Get("Actual CPU/GPU package power. The TDP slider sets a power limit; idle consumption can be lower.\n")+source;
                ToolTipService.SetToolTip(_onexStatsCells[spec[0]],source);
            }
            UpdateOnexEnergySaverIndicators();
        }
    }
}
