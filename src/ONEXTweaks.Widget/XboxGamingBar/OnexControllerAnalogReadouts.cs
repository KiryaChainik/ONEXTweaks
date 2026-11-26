// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Globalization;
using Windows.Data.Json;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    // The Bridge supplies physical and tuned percentages for this exact frame.
    // Both windows display those measurements directly, including inversion and swap.
    internal sealed class OnexControllerAnalogReadouts
    {
        private static readonly string[] Keys={"lx","ly","rx","ry","lt","rt"};
        private static readonly string[] Labels={"AXIS 0 · LX","AXIS 1 · LY","AXIS 2 · RX","AXIS 3 · RY","LT","RT"};
        private readonly TextBlock[] raw=new TextBlock[6],output=new TextBlock[6];
        private readonly TextBlock travel=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=13};
        private string travelText;

        internal StackPanel Panel {get;}=new StackPanel{Spacing=4};
        internal TextBlock LastCell=>output[5];

        private readonly Grid table=new Grid{ColumnSpacing=8,RowSpacing=1};
        private readonly TextBlock[] inputs=new TextBlock[6];
        private TextBlock inputHeading,rawHeading,outputHeading;
        private bool? wideLayout;

        internal OnexControllerAnalogReadouts()
        {
            inputHeading=AddCell(OnexStrings.Get("Input"),false);
            rawHeading=AddCell(OnexStrings.Get("Raw"),false);
            outputHeading=AddCell(OnexStrings.Get("Output"),false);
            for(int i=0;i<Keys.Length;i++)
            {
                inputs[i]=AddCell(Labels[i],false);raw[i]=AddCell("—",true);output[i]=AddCell("—",true);
            }
            Layout(true);
            table.SizeChanged+=(s,e)=>Layout(e.NewSize.Width>=620);
            Panel.Children.Add(table);Panel.Children.Add(travel);
            Panel.Children.Add(new TextBlock{FontSize=13,TextWrapping=TextWrapping.Wrap,
                Text=OnexStrings.Get("Positive Y is up. Each axis is rescaled outside its centre deadzone. With inversion and stick swap off, Raw and Output meet at full deflection. Output previews saved tuning; Hardware Controller passes Raw to games.")});
            ClearSample();
        }

        private TextBlock AddCell(string text,bool numeric)
        {
            var cell=new TextBlock{Text=text,FontSize=14};
            if(numeric)cell.FontFamily=new FontFamily("Consolas");
            else cell.Foreground=new SolidColorBrush(OnexApprovedTheme.Current.Muted);
            table.Children.Add(cell);return cell;
        }

        private void Layout(bool wide)
        {
            if(wideLayout==wide)return;
            wideLayout=wide;
            table.ColumnDefinitions.Clear();table.RowDefinitions.Clear();
            int columns=wide?7:3,rows=wide?3:7;
            for(int col=0;col<columns;col++)
                table.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            for(int row=0;row<rows;row++)table.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            void Position(TextBlock cell,int row,int col)
            {
                Grid.SetRow(cell,wide?row:col);Grid.SetColumn(cell,wide?col:row);
                cell.TextAlignment=(wide?col:row)==0?TextAlignment.Left:TextAlignment.Right;
            }
            Position(inputHeading,0,0);Position(rawHeading,1,0);Position(outputHeading,2,0);
            for(int i=0;i<Keys.Length;i++){Position(inputs[i],0,i+1);Position(raw[i],1,i+1);Position(output[i],2,i+1);}
        }

        private static string Measurement(JsonObject values,string key,bool trigger)
        {
            if(values==null||!values.TryGetValue(key,out var field)||field.ValueType!=JsonValueType.Number)return "—";
            double percent=field.GetNumber();
            if(double.IsNaN(percent)||double.IsInfinity(percent))return "—";
            double normalized=Math.Min(1,Math.Max(trigger?0:-1,percent/100));
            return normalized.ToString("0.00000",CultureInfo.InvariantCulture);
        }

        private static string Travel(JsonObject tuning)
        {
            if(tuning==null || !tuning.ContainsKey("leftTriggerStart") || !tuning.ContainsKey("rightTriggerStart"))return "—";
            double leftStart=tuning.GetNamedNumber("leftTriggerStart"),rightStart=tuning.GetNamedNumber("rightTriggerStart");
            if(tuning.GetNamedBoolean("hairTriggers",false))
                return OnexStrings.Format("Hair triggers · LT deadzone {0}% · RT deadzone {1}% · Minimum 2%",leftStart,rightStart);
            if(!tuning.ContainsKey("leftTriggerEnd")||!tuning.ContainsKey("rightTriggerEnd"))return "—";
            return OnexStrings.Format("Deadzone / full press · LT {0}% / {1}% · RT {2}% / {3}%",
                leftStart,tuning.GetNamedNumber("leftTriggerEnd"),rightStart,tuning.GetNamedNumber("rightTriggerEnd"));
        }

        internal void ApplySample(JsonObject state)
        {
            var physical=state.GetNamedObject("physical");var tuned=state.GetNamedObject("tuned");
            for(int i=0;i<Keys.Length;i++)
            {
                Update(raw[i],Measurement(physical,Keys[i],i>=4));
                Update(output[i],Measurement(tuned,Keys[i],i>=4));
            }
            string next=Travel(state.GetNamedObject("tuning",null));
            if(next!=travelText){travel.Text=next;travelText=next;}
        }

        internal void ClearSample()
        {
            for(int i=0;i<Keys.Length;i++){Update(raw[i],"—");Update(output[i],"—");}
            travel.Text="—";travelText=null;
        }

        internal string RawAt(int index)=>raw[index].Text;
        internal string OutputAt(int index)=>output[index].Text;
        internal string TravelDescription=>travel.Text;
        private static void Update(TextBlock cell,string next){if(cell.Text!=next)cell.Text=next;}
    }
}
