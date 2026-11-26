// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexFseAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");
            async Task Refresh(){var r=await App.SendMessageAsync(new ValueSet{{"OnexFseGet",true}});ApplyOnexFseState((string)r["OnexFseState"]);}
            async Task Signal(string phase,bool target)=>await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-fse-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"phase",JsonValue.CreateStringValue(phase)},{"target",JsonValue.CreateBooleanValue(target)}}.Stringify());
            await Refresh();bool original=OnexFseActive;
            try
            {
                if(original)throw new InvalidOperationException("FSE verification expects desktop as its initial state");
                if(!OnexFseReady)throw new InvalidOperationException("Native Xbox mode not ready");
                if(!qsTileMap.TryGetValue("OnexFse",out var tile)||!tile.IsVisible||!tile.IsActionable||tile.StateText.Text!="Enter"||!OnexHasTileDetails(tile.Id))throw new InvalidOperationException("FSE tile not connected");
                if(!QuickSettings.QuickSettingsConfig.Instance.IsDuplicateOnexAction(QuickSettings.TileActionType.ToggleFse)||!QuickSettings.QuickSettingsConfig.Instance.IsDuplicateOnexAction(QuickSettings.TileActionType.None,shortcut:"Win+F11"))throw new InvalidOperationException("Duplicate FSE action not rejected");
                foreach(bool target in new[]{true,false})
                {
                    tile.TileButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);await Signal(target?"enter":"exit",target);
                    ((IInvokeProvider)new ButtonAutomationPeer(tile.TileButton).GetPattern(PatternInterface.Invoke)).Invoke();
                    bool reached=false;
                    for(int n=0;n<100;n++){await Task.Delay(150);await Refresh();if(OnexFseActive==target&&widget?.Visible==true){reached=true;break;}}
                    if(!reached||tile.StateText.Text!=(target?"Exit":"Enter"))throw new InvalidOperationException("FSE tile did not acknowledge mode "+target);
                    await SaveOnexImage(target?"onex-fse-active.png":"onex-fse-desktop.png",tile.TileButton);
                }
                ShowOnexFseMenu(tile.TileButton);await Task.Delay(100);CloseOnexMenus();report["enterExitVerified"]=JsonValue.CreateBooleanValue(true);report["nativeStateReadback"]=JsonValue.CreateBooleanValue(true);report["gameBarRestoredInBothModes"]=JsonValue.CreateBooleanValue(true);report["duplicateActionBlocked"]=JsonValue.CreateBooleanValue(true);report["holdMenuAvailable"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                CloseOnexMenus();await Signal("finished",original);
                // External driver restores the original native mode on a failed run too.
            }
        }
    }
}
