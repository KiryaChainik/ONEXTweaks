// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using XboxGamingBar.QuickSettings;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexTileIconPaletteAsync(JsonObject report)
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            string Color(Windows.UI.Xaml.Media.Brush brush)=>(brush as Windows.UI.Xaml.Media.SolidColorBrush)?.Color.ToString()??"not-solid";
            var white=Windows.UI.Colors.White.ToString();var gray=Windows.UI.Color.FromArgb(255,112,112,112).ToString();
            Check(OnexBrandTheme,"Icon-palette QA expects the current ONEXPLAYER theme; no theme preference was changed");
            int originalColumns=qsColumnCount;double originalScroll=_releaseHome.Scroll.VerticalOffset;
            string brightnessColor=Color(RailBrightnessIcon.Foreground),soundColor=Color(RailMuteIcon.Foreground),micColor=Color(RailMicButton.Foreground);
            var rows=new JsonArray();int recovered=0,hints=0,focused=0;
            try
            {
                // Exercise the real factory before its asynchronous theme brush is available.
                var savedBrush=tileIconBrush;
                try
                {
                    tileIconBrush=null;
                    foreach(string id in new[]{"CPUBoost","Overlay","Profile","Keyboard"})
                    {
                        var probe=new TileDefinition{IconElement=BuildTileIconElement(TileActionType.None,id,"\uE765",28,null)};
                        Check(Color(OnexMainTileIcon(probe)?.Foreground)==white,"Cold factory inherited a system icon accent: "+id);
                    }
                    report["coldFactoryExplicitNeutralFallback"]=JsonValue.CreateBooleanValue(true);
                }
                finally{tileIconBrush=savedBrush;}
                foreach(int columns in new[]{4,5,6})
                {
                    qsColumnCount=columns;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(150);UpdateLayout();
                    var tiles=qsTileDefinitions.Where(t=>t.IsVisible&&!ShouldSkipTile(t)&&!qsCustomShortcuts.Contains(t)&&t.TileButton!=null).ToArray();
                    // UI-only contamination, repaired synchronously before a frame can render.
                    foreach(var tile in tiles)
                    {
                        var icon=OnexMainTileIcon(tile);if(icon!=null)icon.Foreground=new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.DodgerBlue);
                        if(tile.TileButton.Content is Grid wrapper)
                            foreach(var hint in wrapper.Children.OfType<FontIcon>().Where(i=>i.Glyph=="\uE712"))hint.Foreground=new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.DodgerBlue);
                    }
                    ApplyOnexTileAvailability();
                    foreach(var tile in tiles)
                    {
                        var icon=OnexMainTileIcon(tile);
                        if(icon!=null)
                        {
                            string color=Color(icon.Foreground);Check(color==white||color==gray,"Unexpected blue or foreign icon tint: "+tile.Id);recovered++;
                            if(!tile.IsActionable)Check(color==gray,"Unavailable icon is not neutral gray: "+tile.Id);
                            if(tile.TileButton.IsEnabled)
                            {
                                if(tile.TileButton.Focus(FocusState.Keyboard))focused++;Windows.UI.Xaml.VisualStateManager.GoToState(tile.TileButton,"PointerOver",false);
                                Check(Color(icon.Foreground)==color,"Focus/hover changed the glyph tint: "+tile.Id);
                                Windows.UI.Xaml.VisualStateManager.GoToState(tile.TileButton,"Normal",false);
                            }
                            rows.Add(new JsonObject{{"columns",JsonValue.CreateNumberValue(columns)},{"id",JsonValue.CreateStringValue(tile.Id)},{"color",JsonValue.CreateStringValue(color)},{"available",JsonValue.CreateBooleanValue(tile.IsActionable)}});
                        }
                        if(tile.TileButton.Content is Grid wrapper)
                            foreach(var hint in wrapper.Children.OfType<FontIcon>().Where(i=>i.Glyph=="\uE712"))
                            {Check(Color(hint.Foreground)==OnexPalette.Secondary.ToString(),"Details marker inherited an accent: "+tile.Id);hints++;}
                    }
                }
                // Test the requested Off/On icon contrast using cached UI state only.
                bool boost=_onexBoostOn;
                try
                {
                    _onexBoostOn=false;ApplyOnexTileCaption(qsTileMap["CPUBoost"],true);Check(Color(OnexMainTileIcon(qsTileMap["CPUBoost"]).Foreground)==gray,"Boost Off icon not gray");
                    _onexBoostOn=true;ApplyOnexTileCaption(qsTileMap["CPUBoost"],true);Check(Color(OnexMainTileIcon(qsTileMap["CPUBoost"]).Foreground)==white,"Boost On icon not neutral bright");
                }
                finally{_onexBoostOn=boost;ApplyOnexTileAvailability();}
                qsColumnCount=originalColumns;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(150);UpdateLayout();
                foreach(var tile in qsTileDefinitions.Where(t=>t.IsVisible&&!ShouldSkipTile(t)&&!qsCustomShortcuts.Contains(t)&&t.TileButton!=null))
                {
                    tile.TileButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);
                    await SaveOnexImage("onex-icon-palette-"+tile.Id+"-161.png",tile.IconElement);
                }
                Check(Color(RailBrightnessIcon.Foreground)==brightnessColor&&Color(RailMuteIcon.Foreground)==soundColor&&Color(RailMicButton.Foreground)==micColor,"Tile tint repair changed media icons");
                bool saver=OnexEnergySaverKnown&&OnexEnergySaverOn;
                Check(Color(_chgBatteryFill.Fill)==(saver?Windows.UI.Color.FromArgb(255,255,211,64):Windows.UI.Color.FromArgb(225,92,196,106)).ToString(),"Battery semantic fill lost its saver/charge color");
                report["iconColors"]=rows;report["recoveredInjectedBlueIcons"]=JsonValue.CreateNumberValue(recovered);report["neutralDetailsMarkers"]=JsonValue.CreateNumberValue(hints);report["programmaticFocusControls"]=JsonValue.CreateNumberValue(focused);report["hoverWasSyntheticVisualState"]=JsonValue.CreateBooleanValue(true);
                report["mediaAndBatterySemanticColorsPreserved"]=JsonValue.CreateBooleanValue(true);
                report["blueContaminationAndBoostStatesWereSyntheticUiOnly"]=JsonValue.CreateBooleanValue(true);
                report["exactOwnerBlueIconsIdentified"]=JsonValue.CreateBooleanValue(false);
                report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                qsColumnCount=originalColumns;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();_releaseHome.Scroll.ChangeView(null,originalScroll,null,true);
                await FileIO.WriteTextAsync(await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
        private async Task VerifyOnexReadableTileNamesAsync(JsonObject report)
        {
            void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
            var profilesReply=await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"OnexProfilesGet",true}});
            ApplyOnexProfilesState((string)profilesReply["OnexProfilesState"]);
            var hardwareReply=await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"OnexHardwareHomeGet",true}});
            ApplyOnexHardwareHomeState((string)hardwareReply["OnexHardwareHomeState"]);
            var powerReply=await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"OnexWindowsPowerGet",true}});
            ApplyOnexWindowsPowerState((string)powerReply["OnexWindowsPowerState"]);
            var displayReply=await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"OnexDisplayGet",true}});
            ApplyOnexDisplayState((string)displayReply["OnexDisplayState"]);
            string originalProfilesJson=(string)profilesReply["OnexProfilesState"],originalHardwareJson=(string)hardwareReply["OnexHardwareHomeState"],originalPowerJson=(string)powerReply["OnexWindowsPowerState"];
            var originalProfiles=_onexProfilesState;var originalHardware=_onexHardwareHome;var originalPower=_onexWindowsPower;
            string[] ids={"CPUBoost","OnexFanMode","Profile","ReShade","PowerMode"};
            string[] names={"CPU Boost","Fan Profile","Perf Profile","ScrColor Profile","Power Mode"};
            int columns=qsColumnCount;var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");
            try
            {
                PerformanceNavItem.IsChecked=true;var geometry=new JsonArray();
                foreach(int count in new[]{4,5,6})
                {
                    qsColumnCount=count;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(250);
                    for(int i=0;i<ids.Length;i++)
                    {
                        var tile=qsTileMap[ids[i]];FitOnexChargeTitle(tile);tile.NameText.UpdateLayout();
                        Check(tile.NameText.Text==names[i],"Requested full title differs: "+ids[i]);
                        Check(!tile.NameText.IsTextTrimmed&&tile.NameText.MaxLines==1&&tile.NameText.ActualHeight<=20.5,"Title clipped or wrapped: "+ids[i]);
                        geometry.Add(new JsonObject{{"columns",JsonValue.CreateNumberValue(count)},{"id",JsonValue.CreateStringValue(ids[i])},{"title",JsonValue.CreateStringValue(tile.NameText.Text)},{"fontSize",JsonValue.CreateNumberValue(tile.NameText.FontSize)}});
                    }
                }
                report["fullTitlesIn4_5_6Columns"]=geometry;
                Check(qsTileMap["Profile"].StateText.Text==OnexProfileCaption(OnexActiveProfile),"Performance caption differs from active backend profile");
                Check(qsTileMap["ReShade"].StateText.Text==OnexProfileCaption(OnexEffectiveDisplayProfile),"Color caption differs from effective backend profile");
                Check(qsTileMap["OnexFanMode"].StateText.Text==OnexFanProfileCaption,"Fan caption differs from actual preset");
                bool ac=_onexWindowsPower.GetNamedBoolean("onAc",false);int current=(int)_onexWindowsPower.GetNamedNumber(ac?"modeAc":"modeDc");
                Check(qsTileMap["PowerMode"].StateText.Text==new[]{"Efficiency","Balanced","Performance"}[current],"Mode caption differs from actual power source/mode");
                report["captionsMatchActualSharedState"]=JsonValue.CreateBooleanValue(true);
                Check(qsTileMap["Overlay"].IconElement is FontIcon chart&&chart.Glyph=="\uE9D2"&&chart.FontFamily.Source=="Segoe MDL2 Assets","Overlay icon does not use its tab chart");
                Check(qsTileMap["Profile"].IconElement is Viewbox profileBox&&profileBox.Child is PathIcon,"Performance profile is not a gauge path");
                var gauge=(PathIcon)((Viewbox)qsTileMap["Profile"].IconElement).Child;
                string gaugePath=TileActionHelper.GetFluentIconPathForTileId("Overlay");
                var expected=(PathIcon)Windows.UI.Xaml.Markup.XamlReader.Load("<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\""+gaugePath+"\" />");
                Check(gauge.Data.ToString()==expected.Data.ToString(),"Performance icon did not reuse the existing gauge artwork");
                foreach(string id in new[]{"Overlay","Profile"})
                {
                    var tile=qsTileMap[id];var icon=tile.IconElement;
                    double center=icon.TransformToVisual(tile.TileButton).TransformPoint(new Windows.Foundation.Point(icon.ActualWidth/2,icon.ActualHeight/2)).X;
                    Check(Math.Abs(center-tile.TileButton.ActualWidth/2)<=.51,"Tile icon container not centered: "+id);
                    await SaveOnexImage("onex-updated-icon-"+id+"-160.png",icon);
                    if(id=="Overlay"||tile.StateText.Text=="Global")await SaveOnexImage("onex-updated-tile-"+id+"-160.png",tile.TileButton);
                }
                report["overlayChartAndPerformanceGaugeIcons"]=JsonValue.CreateBooleanValue(true);
                // Only cached UI inputs change here. No profile mutation or hardware command is sent.
                try
                {
                    var fake=JsonObject.Parse(originalProfilesJson);var global=fake.GetNamedArray("entries").Select(v=>v.GetObject()).First(v=>v.GetNamedString("id")=="global");
                    var game=JsonObject.Parse(global.Stringify());game["id"]=JsonValue.CreateStringValue("onex-caption-ui-fixture");game["name"]=JsonValue.CreateStringValue("Example game profile");
                    game.GetNamedObject("ac")["display"]=new JsonObject();game.GetNamedObject("dc")["display"]=JsonValue.CreateNullValue();
                    fake.GetNamedArray("entries").Add(game);fake["activeId"]=game["id"];fake["currentGame"]=new JsonObject();fake["onAc"]=JsonValue.CreateBooleanValue(true);fake.GetNamedObject("flags")["Display"]=JsonValue.CreateBooleanValue(true);
                    _onexProfilesState=fake;ApplyOnexTileAvailability();
                    Check(qsTileMap["Profile"].StateText.Text=="Example game profile"&&qsTileMap["ReShade"].StateText.Text=="Example game profile","Game profile names were replaced by generic Game caption");
                    fake["onAc"]=JsonValue.CreateBooleanValue(false);ApplyOnexTileAvailability();Check(qsTileMap["ReShade"].StateText.Text=="Global","Missing DC color profile did not fall back to Global");
                    fake["onAc"]=JsonValue.CreateBooleanValue(true);fake.GetNamedObject("flags")["Display"]=JsonValue.CreateBooleanValue(false);ApplyOnexTileAvailability();Check(qsTileMap["ReShade"].StateText.Text=="Global","Disabled per-game Display group did not show Global");
                    var fan=JsonObject.Parse(originalHardwareJson);fan.GetNamedObject("fan")["name"]=JsonValue.CreateStringValue("Quiet preset");_onexHardwareHome=fan;ApplyOnexTileAvailability();Check(qsTileMap["OnexFanMode"].StateText.Text=="Quiet preset","Named fan preset not displayed");
                    var power=JsonObject.Parse(originalPowerJson);power["modeAc"]=JsonValue.CreateNumberValue(0);power["modeDc"]=JsonValue.CreateNumberValue(2);power["onAc"]=JsonValue.CreateBooleanValue(true);_onexWindowsPower=power;ApplyOnexTileAvailability();Check(qsTileMap["PowerMode"].StateText.Text=="Efficiency","AC mode caption stale");
                    power["onAc"]=JsonValue.CreateBooleanValue(false);ApplyOnexTileAvailability();Check(qsTileMap["PowerMode"].StateText.Text=="Performance","DC mode caption stale");
                    report["syntheticUiOnlyScopeCases"]=JsonValue.CreateBooleanValue(true);report["actualGameLaunched"]=JsonValue.CreateBooleanValue(false);
                }
                finally{_onexProfilesState=originalProfiles;_onexHardwareHome=originalHardware;_onexWindowsPower=originalPower;ApplyOnexTileAvailability();}
                qsColumnCount=columns;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(250);
                foreach(string id in ids)
                {
                    var tile=qsTileMap[id];
                    // Export no private app/profile names in screenshot evidence.
                    if(id=="CPUBoost"||id=="PowerMode"||id=="OnexFanMode"||tile.StateText.Text=="Global")await SaveOnexImage("onex-name-"+id+"-160.png",tile.TileButton);
                }
                var color=qsTileMap["ReShade"];Check(color.IsActionable&&string.IsNullOrEmpty(color.CustomShortcut),"Screen Color tile is unavailable or retains Home shortcut");
                Check(OnexTileIdentity(color)!="shortcut:HOME","Color profile identity still conflicts with ReShade Home action");
                color.TileButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(200);
                var nav=new Windows.UI.Xaml.Automation.Peers.RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                var rect=color.TileButton.TransformToVisual(PerformanceNavItem).TransformBounds(new Windows.Foundation.Rect(0,0,color.TileButton.ActualWidth,color.TileButton.ActualHeight));string step=run+":1";
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(step)},{"button",JsonValue.CreateStringValue("focus")},{"x",JsonValue.CreateNumberValue(nav.X+(rect.X+rect.Width/2+widget.WindowBounds.X)*scale)},{"y",JsonValue.CreateNumberValue(nav.Y+(rect.Y+rect.Height/2+widget.WindowBounds.Y)*scale)}}.Stringify());
                bool opened=false;for(int n=0;n<100;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==step&&DisplayNavItem.IsChecked==true&&widget.Visible){opened=true;break;}}catch{}}
                Check(opened,"Actual screen-color tile tap did not open own Display page");
                report["actualPointerOpensDisplayWithoutHomeShortcut"]=JsonValue.CreateBooleanValue(true);report["physicalTouchTested"]=JsonValue.CreateBooleanValue(false);
                var after=await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet{{"OnexDisplayGet",true}});var beforeDisplay=JsonObject.Parse((string)displayReply["OnexDisplayState"]);var afterDisplay=JsonObject.Parse((string)after["OnexDisplayState"]);
                Check(beforeDisplay.GetNamedObject("settings").Stringify()==afterDisplay.GetNamedObject("settings").Stringify()&&beforeDisplay.GetNamedObject("modes").Stringify()==afterDisplay.GetNamedObject("modes").Stringify(),"Navigation changed display settings");
                report["displaySettingsUnchanged"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                _onexProfilesState=originalProfiles;_onexHardwareHome=originalHardware;_onexWindowsPower=originalPower;qsColumnCount=columns;PerformanceNavItem.IsChecked=true;RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
        private void VerifyOnexTileEditorColumns(JsonObject report)
        {
            var expected = qsTileDefinitions.Where(tile => !ShouldSkipTile(tile)).OrderBy(tile => tile.Order).Select(tile => tile.Id).ToArray();
            int originalColumns = qsColumnCount;
            try
            {
                foreach (int columns in new[] { 3, 4, 5, 6 })
                {
                    qsColumnCount = columns;
                    BuildSortableGrid();
                    var rows = TileSortableGrid.Children.OfType<Grid>().ToArray();
                    if (rows.Length == 0 || rows.Any(row => row.ColumnDefinitions.Count != columns * 2 - 1))
                        throw new InvalidOperationException($"Tile editor did not render {columns} columns");
                    var cards = rows.SelectMany(row => row.Children.OfType<Border>()).ToArray();
                    if (!cards.Select(card => card.Tag as string).SequenceEqual(expected))
                        throw new InvalidOperationException("Tile editor changed the saved order or omitted hidden tiles");
                    if (cards.Any(card => Grid.GetColumn(card) + Grid.GetColumnSpan(card) > columns * 2 - 1))
                        throw new InvalidOperationException($"Tile editor card exceeded the {columns}-column grid");
                }
                report["tileEditorColumnsPassed"] = JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                qsColumnCount = originalColumns;
                BuildSortableGrid();
            }
        }
        private async Task VerifyOnexSixColumnsAsync(JsonObject report)
        {
            string Fingerprint()=>string.Join("|",qsTileDefinitions.OrderBy(t=>t.Id).Select(t=>t.Id+":"+t.Order+":"+t.IsVisible));
            string original=Fingerprint();QuickSettingsCustomize_Click(QuickSettingsCustomizeButton,null);
            ColumnCount6_Click(Column6Button,null);UpdateLayout();
            if(qsColumnCount!=6||!Column6Button.IsEnabled||(int)ApplicationData.Current.LocalSettings.Values["QS_ColumnCount"]!=6)throw new InvalidOperationException("Six-column selection was not saved");
            if(TileSortableGrid.Children.OfType<Grid>().Any(row=>row.ColumnDefinitions.Count(c=>c.Width.GridUnitType==GridUnitType.Star)!=6))throw new InvalidOperationException("Tile editor did not follow the selected six-column layout");
            await SaveOnexImage("onex-six-columns-editor.png",QuickSettingsCustomizePanel);
            QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();await Task.Delay(200);UpdateLayout();
            if(qsColumnCount!=6||Fingerprint()!=original)throw new InvalidOperationException("Six columns did not survive configuration reload or changed tile settings");
            var cells=new JsonArray();report["sixColumnCells"]=cells;double? iconY=null;double? rowTop=null;int focused=0;
            foreach(var tile in qsTileDefinitions.Where(t=>!ShouldSkipTile(t)&&t.IsVisible&&t.TileButton!=null))
            {
                var name=tile.NameText;var button=tile.TileButton;var rect=button.TransformToVisual(QuickSettingsTilesContainer).TransformBounds(new Windows.Foundation.Rect(0,0,button.ActualWidth,button.ActualHeight));
                if(rect.Left<-.5||rect.Right>QuickSettingsTilesContainer.ActualWidth+.5||name.TextWrapping!=TextWrapping.NoWrap||name.MaxLines!=1||name.ActualHeight>20.1)throw new InvalidOperationException("Six-column tile clipped or wrapped: "+tile.Id);
                var natural=new TextBlock{Text=name.Text,FontFamily=name.FontFamily,FontSize=name.FontSize,FontWeight=name.FontWeight};natural.Measure(new Windows.Foundation.Size(double.PositiveInfinity,double.PositiveInfinity));
                if(!qsCustomShortcuts.Contains(tile)&&name.IsTextTrimmed)throw new InvalidOperationException("Built-in six-column title trimmed: "+tile.Id);
                double center=tile.IconElement.TransformToVisual(button).TransformPoint(new Windows.Foundation.Point(0,tile.IconElement.ActualHeight/2)).Y;
                if(rowTop.HasValue&&Math.Abs(rowTop.Value-rect.Top)<.5&&iconY.HasValue&&Math.Abs(iconY.Value-center)>.5)throw new InvalidOperationException("Six-column icon shifted");rowTop=rect.Top;iconY=center;
                if(tile.IsActionable){if(!button.Focus(FocusState.Keyboard))throw new InvalidOperationException("Six-column focus failed: "+tile.Id);focused++;}
                cells.Add(new JsonObject{{"id",JsonValue.CreateStringValue(tile.Id)},{"title",JsonValue.CreateStringValue(name.Text)},{"width",JsonValue.CreateNumberValue(button.ActualWidth)},{"titleWidth",JsonValue.CreateNumberValue(name.ActualWidth)},{"naturalTitleWidth",JsonValue.CreateNumberValue(natural.DesiredSize.Width)},{"isTextTrimmed",JsonValue.CreateBooleanValue(name.IsTextTrimmed)}});
            }
            var rows=QuickSettingsTilesContainer.Children.OfType<Grid>().ToArray();if(rows.Length==0||rows.Any(row=>row.ColumnDefinitions.Count(c=>c.Width.GridUnitType==GridUnitType.Star)!=6))throw new InvalidOperationException("Home did not render six columns");
            _releaseHome.Scroll.ChangeView(null,0,null,true);_releaseHome.PowerInput.Focus(FocusState.Programmatic);await SaveOnexImage("onex-six-columns-home.png",this);
            report["sixColumnCells"]=cells;report["focusedTiles"]=JsonValue.CreateNumberValue(focused);report["persistedSelection"]=JsonValue.CreateBooleanValue(true);report["productionConfigReload"]=JsonValue.CreateBooleanValue(true);report["tileOrderVisibilityPreserved"]=JsonValue.CreateBooleanValue(true);report["coldProcessRestartTested"]=JsonValue.CreateBooleanValue(false);
        }
        private async Task VerifyOnexTileLayoutAsync()
        {
            var folder=ApplicationData.Current.LocalFolder;var settings=ApplicationData.Current.LocalSettings.Values;var report=new JsonObject();JsonObject handoff=null;bool pending=false;
            try
            {
                var existing=await folder.TryGetItemAsync("onex-layout-handoff.json") as StorageFile;
                if(existing!=null)
                {
                    handoff=JsonObject.Parse(await FileIO.ReadTextAsync(existing));var expected=handoff.GetNamedObject("expected");
                    foreach(var pair in expected.GetNamedObject("tiles")){var actual=qsTileMap[pair.Key];var saved=pair.Value.GetObject();if(actual.Order!=(int)saved.GetNamedNumber("order")||actual.IsVisible!=saved.GetNamedBoolean("visible"))throw new InvalidOperationException("Layout did not survive restart: "+pair.Key);}
                    if(qsColumnCount!=(int)expected.GetNamedNumber("columns"))throw new InvalidOperationException("Column count did not survive restart");
                    var config=QuickSettingsConfig.Instance;string keep=expected.GetNamedString("keep"),deleted=expected.GetNamedString("deleted"),program=expected.GetNamedString("program");
                    if(config.GetTile(keep)==null||config.GetTile(deleted)!=null||config.GetTile(program)?.ActionParam!=@"C:\programs\sample;p|q.exe")throw new InvalidOperationException("Custom tile identity/path persistence failed");
                    if(config.GetTile(keep).ControllerHotkey!="8448")throw new InvalidOperationException("Controller binding did not survive restart");
                    report["coldRestartPersistence"]=JsonValue.CreateBooleanValue(true);report["identityDeletion"]=JsonValue.CreateBooleanValue(true);report["escapedProgramPath"]=JsonValue.CreateBooleanValue(true);
                    QuickSettingsCustomize_Click(QuickSettingsCustomizeButton,null);await Task.Delay(250);await SaveOnexImage("onex-layout-editor.png");QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);
                    var after=await ReadOnexStateAsync();if(after.GetNamedString("rawLimits")!=handoff.GetNamedString("rawLimits"))throw new InvalidOperationException("Tile editor changed TDP");report["rawLimitsPreserved"]=after["rawLimits"];report["passed"]=JsonValue.CreateBooleanValue(true);
                }
                else
                {
                    handoff=new JsonObject();var backup=new JsonObject();foreach(var pair in settings.Where(p=>p.Key.StartsWith("QS_")||p.Key=="QuickSettingsTiles").ToArray())backup[pair.Key]=pair.Value is bool b?JsonValue.CreateBooleanValue(b):pair.Value is int n?JsonValue.CreateNumberValue(n):JsonValue.CreateStringValue(pair.Value.ToString());handoff["backup"]=backup;handoff["rawLimits"]=(await ReadOnexStateAsync())["rawLimits"];
                    QuickSettingsCustomize_Click(QuickSettingsCustomizeButton,null);if(!qsEditMode||QuickSettingsCustomizePanel.Visibility!=Visibility.Visible||QuickSettingsTilesContainer.Visibility!=Visibility.Collapsed||_releaseHome.PowerSection.Visibility!=Visibility.Collapsed||_releaseHome.MediaSection.Visibility!=Visibility.Collapsed||_releaseHome.StatsHost.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Editor did not replace tile grid");
                    var all=qsTileDefinitions.Where(t=>!ShouldSkipTile(t)).OrderBy(t=>t.Order).ToArray();if(all.Length<14||all.Any(t=>t.Id=="MsiCenter"))throw new InvalidOperationException("Wrong tile catalog");
                    int first=all[0].Order,second=all[1].Order;SortableTile_Click(new Button{Tag=all[0].Id},null);SortableTile_Click(new Button{Tag=all[1].Id},null);if(all[0].Order!=second||all[1].Order!=first)throw new InvalidOperationException("Swap failed");
                    bool visible=qsTileMap["OptiScaler"].IsVisible;SortableTileVisibility_Click(new Button{Tag="OptiScaler_vis"},null);if(qsTileMap["OptiScaler"].IsVisible==visible)throw new InvalidOperationException("Visibility toggle failed");
                    ColumnCount3_Click(null,null);if(qsColumnCount!=3)throw new InvalidOperationException("3 columns failed");ColumnCount4_Click(null,null);if(qsColumnCount!=4)throw new InvalidOperationException("4 columns failed");ColumnCount5_Click(null,null);if(qsColumnCount!=5)throw new InvalidOperationException("5 columns failed");
                    var ids=qsTileMap.Keys.ToArray();AddCustomShortcutTile("ONEX QA A","Ctrl+Shift+F12");var a=qsTileDefinitions.Single(t=>!ids.Contains(t.Id));ids=qsTileMap.Keys.ToArray();AddCustomShortcutTile("ONEX QA B","Ctrl+Shift+F11");var bTile=qsTileDefinitions.Single(t=>!ids.Contains(t.Id));DeleteCustomShortcutTile(a);if(QuickSettingsConfig.Instance.GetTile(a.Id)!=null||QuickSettingsConfig.Instance.GetTile(bTile.Id)==null)throw new InvalidOperationException("Deleted wrong duplicate-shortcut tile");
                    ids=qsTileMap.Keys.ToArray();AddActionTile("ONEX QA EXE",TileActionType.LaunchUserProgram,@"C:\programs\sample;p|q.exe");var program=qsTileDefinitions.Single(t=>!ids.Contains(t.Id));var stored=QuickSettingsConfig.Instance.GetTile(bTile.Id);stored.ControllerHotkey="8448";bTile.ControllerHotkey=stored.ControllerHotkey;QuickSettingsConfig.Instance.Save();
                    string exported=QuickSettingsConfig.Instance.ExportConfig();if(!QuickSettingsConfig.Instance.ImportConfig(exported)||QuickSettingsConfig.Instance.GetTile(program.Id).ActionParam!=program.ActionParam||QuickSettingsConfig.Instance.GetTile(bTile.Id).ControllerHotkey!="8448")throw new InvalidOperationException("Export/import lost custom metadata");
                    QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);var tiles=new JsonObject();foreach(var tile in qsTileDefinitions)tiles[tile.Id]=new JsonObject{{"order",JsonValue.CreateNumberValue(tile.Order)},{"visible",JsonValue.CreateBooleanValue(tile.IsVisible)}};
                    handoff["expected"]=new JsonObject{{"tiles",tiles},{"columns",JsonValue.CreateNumberValue(qsColumnCount)},{"keep",JsonValue.CreateStringValue(bTile.Id)},{"deleted",JsonValue.CreateStringValue(a.Id)},{"program",JsonValue.CreateStringValue(program.Id)}};
                    await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-layout-handoff.json",CreationCollisionOption.ReplaceExisting),handoff.Stringify());pending=true;report["stage"]=JsonValue.CreateStringValue("awaiting-restart");
                }
            }
            catch(Exception ex){report["passed"]=JsonValue.CreateBooleanValue(false);report["error"]=JsonValue.CreateStringValue(ex.ToString());}
            finally
            {
                if(!pending&&handoff!=null)
                {
                    QuickSettingsCustomizeDone_Click(QuickSettingsCustomizeCloseTopButton,null);foreach(string key in settings.Keys.Where(k=>k.StartsWith("QS_")||k=="QuickSettingsTiles").ToArray())settings.Remove(key);
                    foreach(var pair in handoff.GetNamedObject("backup")){var v=pair.Value;settings[pair.Key]=v.ValueType==JsonValueType.Boolean?(object)v.GetBoolean():v.ValueType==JsonValueType.Number?(object)(int)v.GetNumber():v.GetString();}
                    QuickSettingsConfig.Instance.Load();DefineQuickSettingsTiles();LoadQuickSettingsConfig();RebuildQuickSettingsTiles();UpdateQuickSettingsTileStates();var f=await folder.TryGetItemAsync("onex-layout-handoff.json");if(f!=null)await f.DeleteAsync();report["originalLayoutRestored"]=JsonValue.CreateBooleanValue(true);
                }
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-layout-verification.json",CreationCollisionOption.ReplaceExisting),report.Stringify());var armed=await folder.TryGetItemAsync("onex-focus-verification-armed.json");if(armed!=null)await armed.DeleteAsync();
            }
        }
    }
}
