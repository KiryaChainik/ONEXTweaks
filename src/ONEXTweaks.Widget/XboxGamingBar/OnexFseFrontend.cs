// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexFseState;private bool _onexFseBusy;
        private bool OnexFseActive=>_onexFseState?.GetNamedBoolean("active",false)==true;
        private bool OnexFseReady=>App.IsConnected&&_onexFseState?.GetNamedBoolean("supported",false)==true&&_onexFseState.GetNamedBoolean("canSet",false)&&!_onexFseBusy;
        private void ApplyOnexFseState(string json){if(JsonObject.TryParse(json,out var state))_onexFseState=state;ApplyOnexTileAvailability();}
        private async Task InvokeOnexFseAsync(bool? active=null)
        {
            if(_onexFseBusy)return;_onexFseBusy=true;ApplyOnexTileAvailability();
            try{await SendOnexHomeRequestAsync(new ValueSet{{"OnexDesktopAction",active.HasValue?(active.Value?"fseEnter":"fseExit"):"fse"},{"InGameBar",widget!=null}});}
            catch(Exception e){ShowOnexDesktopStatus(e.Message);}
            finally{_onexFseBusy=false;ApplyOnexTileAvailability();}
        }
        private void ApplyOnexFseTile(TileDefinition tile)
        {
            if(tile.StateText==null)return;bool supported=_onexFseState?.GetNamedBoolean("supported",false)==true;
            tile.StateText.Text=_onexFseBusy?global::XboxGamingBar.OnexStrings.Get("Switching…"):!supported?global::XboxGamingBar.OnexStrings.Get("Setup"):OnexFseActive?global::XboxGamingBar.OnexStrings.Get("Exit"):global::XboxGamingBar.OnexStrings.Get("Enter");ApplyOnexToggleTileVisual(tile,OnexFseActive,supported);
        }
        private void ShowOnexFseMenu(Button anchor)
        {
            var menu=CreateOnexMenu();var enter=new MenuFlyoutItem{Text=global::XboxGamingBar.OnexStrings.Get("Enter Xbox mode"),IsEnabled=OnexFseReady&&!OnexFseActive};enter.Click+=async(s,e)=>await InvokeOnexFseAsync(true);menu.Items.Add(enter);
            var exit=new MenuFlyoutItem{Text=global::XboxGamingBar.OnexStrings.Get("Return to Windows desktop"),IsEnabled=OnexFseReady&&OnexFseActive};exit.Click+=async(s,e)=>await InvokeOnexFseAsync(false);menu.Items.Add(exit);
            menu.Items.Add(new MenuFlyoutSeparator());var settings=new MenuFlyoutItem{Text=global::XboxGamingBar.OnexStrings.Get("Xbox mode settings")};settings.Click+=async(s,e)=>await InvokeOnexDesktopActionAsync("fseSettings");menu.Items.Add(settings);menu.ShowAt(anchor);
        }
    }
}
