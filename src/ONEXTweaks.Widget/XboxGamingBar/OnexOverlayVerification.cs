// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexFpsAsync(JsonObject parent=null)
        {
            var report=parent??new JsonObject();string provider=OnexFpsProvider;bool enabled=OnexFpsEnabled;int value=OnexFpsValue;var before=await ReadOnexStateAsync();
            async Task<JsonObject> ReadActual()
            {
                var response=await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"OnexFpsGet",true}});
                if(response==null||!response.TryGetValue("OnexFpsState",out var payload))throw new InvalidOperationException("No native FPS readback");
                ApplyOnexFpsState((string)payload);return JsonObject.Parse((string)payload);
            }
            try
            {
                var cases=new JsonArray();report["tapToggle"]=cases;
                foreach(string p in new[]{"Intel","RTSS"})
                {
                    int custom=p=="Intel"?73:83;await SetOnexFpsAsync(p,false,custom);
                    foreach(bool expected in new[]{true,false,true,false})
                    {
                        QuickSettingsTile_Click(qsTileMap["FPSCombined"].TileButton,new RoutedEventArgs());
                        for(int n=0;n<100&&_onexFpsBusy;n++)await Task.Delay(50);
                        var actual=await ReadActual();var intel=actual.GetNamedObject("intel");
                        if(!qsTileMap["FPSCombined"].IsActionable||OnexFpsProvider!=p||OnexFpsValue!=custom||OnexFpsEnabled!=expected)throw new InvalidOperationException("FPS tile did not preserve value/provider: "+p+"/"+custom+"/"+expected);
                        if(intel.GetNamedBoolean("enabled")!=(p=="Intel"&&expected)||(p=="Intel"&&expected&&(int)intel.GetNamedNumber("value")!=custom)||(int)actual.GetNamedNumber("rtssValue")!=(p=="RTSS"&&expected?custom:0))throw new InvalidOperationException("Native FPS providers readback/mutual exclusion failed");
                        cases.Add(actual);
                        if(expected)await SaveOnexImage("onex-fps-tap-"+p.ToLowerInvariant()+".png",QuickSettingsTilesContainer);
                    }
                }
                await ShowOnexTileDetailsAsync(qsTileMap["FPSCombined"].TileButton,"FPSCombined");await Task.Delay(250);
                var menu=_onexOpenMenus.OfType<MenuFlyout>().SingleOrDefault();if(menu==null||!menu.IsOpen||menu.Items.OfType<MenuFlyoutItem>().Count()!=11)throw new InvalidOperationException("FPS hold menu missing");
                report["holdMenuItems"]=JsonValue.CreateNumberValue(menu.Items.OfType<MenuFlyoutItem>().Count());
                if(VisualTreeHelper.GetParent(menu.Items[0]) is UIElement presenter)await SaveOnexImage("onex-fps-menu.png",presenter);CloseOnexMenus();
                var settings=ShowOnexFpsSettingsAsync(qsTileMap["FPSCombined"].TileButton);await Task.Delay(400);if(_onexDetailsDialog==null)throw new InvalidOperationException("FPS settings missing");await SaveOnexImage("onex-fps-settings.png",_onexDetailsDialog);_onexDetailsDialog.Hide();await settings;
                var after=await ReadOnexStateAsync();if(after.GetNamedString("rawLimits")!=before.GetNamedString("rawLimits"))throw new InvalidOperationException("FPS UI changed TDP");report["rawLimitsPreserved"]=after["rawLimits"];report["passed"]=JsonValue.CreateBooleanValue(true);
            }
            catch(Exception ex){report["passed"]=JsonValue.CreateBooleanValue(false);report["error"]=JsonValue.CreateStringValue(ex.ToString());}
            finally
            {
                CloseOnexMenus();await SetOnexFpsAsync(provider,enabled,value);var restored=await ReadActual();
                bool restoredOk=OnexFpsProvider==provider&&OnexFpsEnabled==enabled&&OnexFpsValue==value;report["originalFpsRestored"]=JsonValue.CreateBooleanValue(restoredOk);report["restoredNativeState"]=restored;if(!restoredOk)report["passed"]=JsonValue.CreateBooleanValue(false);
                PerformanceNavItem.IsChecked=true;await SaveOnexImage("onex-fps-home.png");
                if(parent==null){var folder=ApplicationData.Current.LocalFolder;await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-fps-verification.json",CreationCollisionOption.ReplaceExisting),report.Stringify());await(await folder.GetFileAsync("onex-focus-verification-armed.json")).DeleteAsync();}
            }
            if(parent!=null&&!report.GetNamedBoolean("passed",false))throw new InvalidOperationException(report.GetNamedString("error","FPS verification or restoration failed"));
        }
        private async Task VerifyOnexOverlayAsync()
        {
            var report=new JsonObject();int original=OnexOverlayLevel;var before=await ReadOnexStateAsync();
            try
            {
                var colors=new JsonObject();var reference=((SolidColorBrush)qsTileMap["Power"].StateText.Foreground).Color;
                foreach(string id in new[]{"OptiScaler","Keyboard","Fullscreen","ActionTaskManager","ActionExplorer","ActionEndTask","PowerMode"})
                {
                    var actual=((SolidColorBrush)qsTileMap[id].StateText.Foreground).Color;colors[id]=JsonValue.CreateStringValue(actual.ToString());
                    if(actual!=reference)throw new InvalidOperationException("Caption differs from Power Menu: "+id);
                }
                report["captionColors"]=colors;report["captionReference"]=JsonValue.CreateStringValue(reference.ToString());
                var levels=new JsonArray();
                for(int i=0;i<5;i++)
                {
                    int expected=(original+i+1)%5;
                    QuickSettingsTile_Click(qsTileMap["Overlay"].TileButton,new RoutedEventArgs());
                    for(int n=0;n<100&&_onexOverlayBusy;n++)await Task.Delay(100);
                    if(OnexOverlayLevel!=expected||!qsTileMap["Overlay"].IsActionable)throw new InvalidOperationException("Overlay tile cycle failed");
                    levels.Add(JsonValue.CreateNumberValue(OnexOverlayLevel));
                }
                report["tileTapCycle"]=levels;
                await ShowOnexTileDetailsAsync(qsTileMap["Overlay"].TileButton,"Overlay");await Task.Delay(250);
                var menu=_onexOpenMenus.OfType<MenuFlyout>().SingleOrDefault();
                if(menu==null||!menu.IsOpen||menu.Items.Count!=7)throw new InvalidOperationException("Overlay hold menu missing");
                var labels=new JsonArray();foreach(var item in menu.Items.OfType<MenuFlyoutItem>())labels.Add(JsonValue.CreateStringValue(item.Text));report["holdMenu"]=labels;
                if(VisualTreeHelper.GetParent(menu.Items[0]) is UIElement presenter)await SaveOnexImage("onex-overlay-menu.png",presenter);
                CloseOnexMenus();if(menu.IsOpen)throw new InvalidOperationException("Overlay menu survived close");
                _releaseOsdNav.IsChecked=true;
                if(!isOSDCustomizeExpanded)OSDCustomizeExpandButton_Click(OSDCustomizeExpandButton,null);
                await SaveOnexImage("onex-overlay-editor.png");
                var after=await ReadOnexStateAsync();if(after.GetNamedString("rawLimits")!=before.GetNamedString("rawLimits"))throw new InvalidOperationException("Overlay settings changed TDP");
                report["rawLimitsPreserved"]=after["rawLimits"];report["passed"]=JsonValue.CreateBooleanValue(true);
            }
            catch(Exception ex){report["passed"]=JsonValue.CreateBooleanValue(false);report["error"]=JsonValue.CreateStringValue(ex.ToString());}
            finally
            {
                CloseOnexMenus();await SetOnexOverlayLevelAsync(original);PerformanceNavItem.IsChecked=true;
                await SaveOnexImage("onex-caption-home.png");
                var folder=ApplicationData.Current.LocalFolder;await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-overlay-verification.json",CreationCollisionOption.ReplaceExisting),report.Stringify());
                await(await folder.GetFileAsync("onex-focus-verification-armed.json")).DeleteAsync();
            }
        }
    }
}
