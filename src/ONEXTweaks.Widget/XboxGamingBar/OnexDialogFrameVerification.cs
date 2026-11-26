using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexDialogFramesAsync(JsonObject report)
  {
   int checks=0,windows=0,scrollSamples=0,containerFocus=0;ContentDialog dialog=null;Task opening=null;MenuFlyout menu=null;
   var oldName=currentThemeName;var oldTheme=_onexAppliedTheme;
   var oldNav=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   void Check(bool ok,string why){checks++;report["checks"]=JsonValue.CreateNumberValue(checks);if(!ok)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<160;n++){if(ready())return;await Task.Delay(50);}throw new TimeoutException(why);}
   void Frame(ContentDialog value){
    Check(value.BorderThickness.Equals(new Thickness(0))&&(value.BorderBrush as SolidColorBrush)?.Color.A==0,"Dialog still owns a decorative frame");
    var native=FindDescendantByName(value,"BackgroundElement") as Border;
    Check(native!=null,"Native ContentDialog background unavailable");
    Check(native.BorderThickness.Equals(new Thickness(0)),"Native dialog template still draws its outer frame");
   }
   try{
    Check(!OnexSourceEditorsPending&&_onexDetailsDialog==null,"Preserve pending owner UI");
    var resourceA=new Button();var resourceB=new Button();var resourceKeys=new[]{"ProbeAccent","ProbeAccentHover","ProbeAccentPressed"};
    int resourceBefore=_onexOwnedResourceBrushesCreated;
    SetOnexThemeResourceGroup(resourceA,"accent",resourceKeys,Colors.Red);SetOnexThemeResourceGroup(resourceB,"accent",resourceKeys,Colors.Red);
    var resourceInk=(SolidColorBrush)resourceA.Resources[resourceKeys[0]];
    Check(_onexOwnedResourceBrushesCreated-resourceBefore==2&&resourceKeys.All(key=>ReferenceEquals(resourceA.Resources[key],resourceInk)),"Resource aliases allocate duplicate native brushes");
    Check(!ReferenceEquals(resourceInk,resourceB.Resources[resourceKeys[0]]),"Mutable resource brush shared across controls");
    SetOnexThemeResourceGroup(resourceA,"text",new[]{"ProbeText"},Colors.Red);resourceA.Foreground=(Brush)resourceA.Resources["ProbeText"];resourceA.BorderBrush=resourceInk;
    SetOnexThemeResourceGroup(resourceA,"accent",resourceKeys,Colors.Blue);
    Check(ReferenceEquals(resourceA.BorderBrush,resourceInk)&&resourceInk.Color==Colors.Blue&&((SolidColorBrush)resourceA.Foreground).Color==Colors.Red&&((SolidColorBrush)resourceB.Resources[resourceKeys[0]]).Color==Colors.Red,"Native resource update changed independent text/control or broke held template reference");
    var resourceCaller=new SolidColorBrush(Colors.Green);var resourceScope=new Button();foreach(string key in resourceKeys)resourceScope.Resources[key]=resourceCaller;
    StoreOnexScopedResourceGroup(resourceScope,"accent",resourceKeys,new SolidColorBrush(Colors.Blue));
    Check(resourceCaller.Color==Colors.Green,"Scoped resource paint mutated caller brush");RestoreOnexScopedResource(resourceScope);
    Check(resourceKeys.All(key=>resourceScope.Resources[key] is SolidColorBrush restored&&restored.Color==Colors.Green),"Scoped resource restore lost native original colors");
    var resourceAlias=new Button();SetOnexThemeResourceGroup(resourceAlias,"accent",resourceKeys,Colors.Green);StoreOnexScopedResourceGroup(resourceAlias,"accent",resourceKeys,new SolidColorBrush(Colors.Blue));RestoreOnexScopedResource(resourceAlias);
    Check(resourceKeys.All(key=>((SolidColorBrush)resourceAlias.Resources[key]).Color==Colors.Green),"Native alias snapshot captured colors after mutation");
    report["resourceOwnershipActualUwp"]=JsonValue.CreateBooleanValue(true);
    var propertyA=new Border{BorderBrush=new SolidColorBrush(Colors.Red)};var propertyB=new Border();
    SetOnexThemeBrush(propertyA,Border.BorderBrushProperty,Colors.Red);SetOnexThemeBrush(propertyB,Border.BorderBrushProperty,Colors.Red);
    var immutableRed=OnexSolidBrush(Colors.Red);
    Check(ReferenceEquals(propertyA.BorderBrush,immutableRed)&&ReferenceEquals(propertyB.BorderBrush,immutableRed),"Equal native property colours still own duplicate brushes");
    SetOnexThemeBrush(propertyA,Border.BorderBrushProperty,Colors.Blue);
    Check(((SolidColorBrush)propertyA.BorderBrush).Color==Colors.Blue&&((SolidColorBrush)propertyB.BorderBrush).Color==Colors.Red&&immutableRed.Color==Colors.Red,"Property update mutated another control or immutable cache");
    var resourceCached=new Button();resourceCached.Resources["Caller"]=immutableRed;StoreOnexScopedResourceGroup(resourceCached,"accent",new[]{"Caller"},OnexSolidBrush(Colors.Blue));
    Check(immutableRed.Color==Colors.Red&&((SolidColorBrush)resourceCached.Resources["Caller"]).Color==Colors.Blue,"Mutable resource group changed immutable property cache");RestoreOnexScopedResource(resourceCached);
    Check(((SolidColorBrush)resourceCached.Resources["Caller"]).Color==Colors.Red,"Immutable caller resource restore lost colour");
    report["immutablePropertyBrushSharingActualUwp"]=JsonValue.CreateBooleanValue(true);
    var keyChoice=OnexSettingsChoice("");PopulateOnexKeyboardChoices(keyChoice);var otherChoice=OnexSettingsChoice("");
    Check(keyChoice.Items.Count==OnexKeyboardChoiceCodes.Length&&keyChoice.Items.Cast<object>().All(item=>item is string),"Keyboard values eagerly own native item controls");
    bool exactKeys=true;foreach(int key in OnexKeyboardChoiceCodes){SelectOnexKeyboardChoice(keyChoice,key);exactKeys&=SelectedOnexKeyboardChoice(keyChoice)==key;}
    Check(exactKeys,"Data-backed keyboard choice changed a virtual key value");SelectOnexKeyboardChoice(keyChoice,int.MaxValue);
    Check(keyChoice.SelectedIndex==0&&SelectedOnexKeyboardChoice(keyChoice)==0,"Unknown keyboard choice must remain None");
    Check(_kbmFlyout==null&&_kbmKeys.Count==0,"Keyboard remapping editor was built without opening it");
    Check(ReferenceEquals(keyChoice.ItemTemplate,otherChoice.ItemTemplate),"Identical choice text templates are repeatedly parsed");
    report["dataBackedKeyboardChoicesActualUwp"]=JsonValue.CreateBooleanValue(true);
    foreach(string name in new[]{"ONEXPLAYER","Windows 11 Light"}){
     currentThemeName=name;_onexAppliedTheme=name==OnexPalette.Name?OnexPalette.CreateTheme():WidgetThemes[name];
     foreach(bool longContent in new[]{false,true}){
      var body=new StackPanel{Spacing=12};
      var first=new Button{Content="First selectable control",Style=OnexSettingsButtonStyle(false),Height=54,HorizontalAlignment=HorizontalAlignment.Stretch};
      var check=new CheckBox{Content="Readable checkbox · synthetic preview",IsChecked=true};
      body.Children.Add(first);body.Children.Add(check);
      var lazy=new StackPanel{Visibility=Visibility.Collapsed};var lazyButton=new Button{Content="Deferred appearance preview",Style=OnexSettingsButtonStyle(false)};lazy.Children.Add(lazyButton);body.Children.Add(lazy);
      ComboBox preview=null;Button scrollAnchor=null;
      if(longContent){preview=OnexSettingsChoice("");preview.ItemsSource=new[]{"Alpha preview","Beta preview"};preview.SelectedIndex=0;body.Children.Add(preview);scrollAnchor=new Button{Content="Scroll focus anchor",Style=OnexSettingsButtonStyle(false),Height=54,HorizontalAlignment=HorizontalAlignment.Stretch};body.Children.Add(scrollAnchor);body.Children.Add(new Border{Height=900});}
      var last=new Button{Content="Last selectable control",Style=OnexSettingsButtonStyle(false),Height=54,HorizontalAlignment=HorizontalAlignment.Stretch};body.Children.Add(last);
      dialog=CreateOnexSettingsDialog("Tile detail preview","Known synthetic controls; no settings handlers.",body);dialog.PrimaryButtonText="";dialog.CloseButtonText="Close";dialog.DefaultButton=ContentDialogButton.Close;
      _onexDetailsDialog=dialog;opening=dialog.ShowAsync().AsTask();await Wait(()=>dialog.IsLoaded&&FindDescendantByName(dialog,"BackgroundElement")!=null,"Dialog not loaded");await Task.Delay(90);dialog.UpdateLayout();Frame(dialog);windows++;
      ApplyOnexUiFonts(lazy);ApplyOnexCatalogSurfaces(lazy,_onexAppliedTheme);
      Check(_onexDeferredAppearance.TryGetValue(lazy,out var pending)&&pending.Font&&pending.Surface,"Collapsed appearance was prepared eagerly");
      lazy.Visibility=Visibility.Visible;dialog.UpdateLayout();
      Check(!pending.Font&&!pending.Surface&&lazyButton.FontFamily.Source==_onexUiTypeface.Source,"Expanded appearance did not flush before layout");
      Check(lazyButton.Foreground is SolidColorBrush lazyInk&&OnexContrast(lazyInk.Color,_onexAppliedTheme.ButtonBackground)>=4.5,"Deferred editor text unreadable");
      lazy.Visibility=Visibility.Collapsed;ApplyOnexUiFonts(lazy);ApplyOnexCatalogSurfaces(lazy,_onexAppliedTheme);
      body.Visibility=Visibility.Collapsed;lazy.Visibility=Visibility.Visible;Check(pending.Font&&pending.Surface,"Hidden parent triggered eager appearance");
      body.Visibility=Visibility.Visible;FlushOnexVisibleDeferredAppearance(body);Check(!pending.Font&&!pending.Surface,"Tab entry did not flush deferred child appearance");
      lazy.Visibility=Visibility.Collapsed;dialog.UpdateLayout();
      var scroll=dialog.Content as ScrollViewer;Check(scroll!=null,"Shared tile viewport missing");
      Check(longContent?scroll.ScrollableHeight>100:scroll.ScrollableHeight<1,"Short/scrolling dialog layout incorrect");
      ConfigureOnexFocus(scroll);
      if(scroll.Focus(FocusState.Keyboard)){
       if(ReferenceEquals(FocusManager.GetFocusedElement(),scroll))containerFocus++;
       FocusOnexOwnedControl(scroll);
       Check(!_onexFocusLayers.Any(reference=>reference.TryGetTarget(out var layer)&&IsOnexFocusInkVisible(layer)&&(layer.Target is ScrollViewer||layer.Target is ContentDialog||layer.Target is MenuFlyoutPresenter)),"A container retained whole-window focus ink");
      }
      Check(first.Focus(FocusState.Keyboard),"Body button cannot receive keyboard/gamepad focus");FocusOnexOwnedControl(first);
      Check(_onexControlLayers.TryGetValue(first,out var scope)&&IsOnexFocusInkVisible(scope),"Leaf control focus removed with dialog frame");
      var ink=scope.Ink;Check(ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(first),ink.Root),"Leaf focus is no longer target-local");
      Check(!first.UseSystemFocusVisuals&&ink.Radius>=8,"Duplicate or square leaf focus");
      if(longContent){
       Check(scrollAnchor.Focus(FocusState.Keyboard),"Scroll anchor cannot receive focus");FocusOnexOwnedControl(scrollAnchor);ink=scope.Ink;
       Check(ReferenceEquals(scope.Target,scrollAnchor),"Scroll anchor did not become active target");
       // This fixture deliberately scrolls while retaining its focused control.
       // Disable the native focus-follow policy for this sequence only.
       await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,()=>{});
       scroll.BringIntoViewOnFocusChange=false;
       report["fixtureFocusFollowDisabled"]=JsonValue.CreateBooleanValue(true);
       int geometry=_onexFocusGeometryWrites;Check(scroll.ChangeView(null,160,null,false),"Native dialog scroll refused");
       for(int n=0;n<12;n++){await Task.Delay(20);Check(ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(scrollAnchor),ink.Root)&&ink.Visual.Offset==System.Numerics.Vector3.Zero,"Focus drifted outside scrolling target");scrollSamples++;}
       report["lastScrollOffset"]=JsonValue.CreateNumberValue(scroll.VerticalOffset);report["globalGeometryWritesDuringScroll"]=JsonValue.CreateNumberValue(_onexFocusGeometryWrites-geometry);
       Check(scroll.VerticalOffset>80,"Native dialog did not retain requested scroll offset");
       // The global counter also includes other targets and legitimate size
       // changes when the scrollbar appears. Test this target's coordinates.
       Check(ink.Visual.Offset==System.Numerics.Vector3.Zero&&Math.Abs(ink.Width-scrollAnchor.ActualWidth)<1&&Math.Abs(ink.Height-scrollAnchor.ActualHeight)<1,"Target-local focus geometry mismatches its current control");
       scroll.ChangeView(null,0,null,true);await Task.Delay(70);scroll.BringIntoViewOnFocusChange=true;
       preview.IsDropDownOpen=true;await Task.Delay(90);int visibleRows=0;
       for(int row=0;row<preview.Items.Count;row++)if(preview.ContainerFromIndex(row) is ComboBoxItem item){visibleRows++;Check(OnexHasInlineFocus(item)&&!item.UseSystemFocusVisuals,"Lazy dropdown row lost owned focus");Check(item.Foreground is SolidColorBrush textInk&&OnexContrast(textInk.Color,(item.Background as SolidColorBrush)?.Color??_onexAppliedTheme.ButtonBackground)>=4.5,"Lazy dropdown row has unreadable theme text");}
       Check(visibleRows==2,"Lazy dropdown rows not realised");preview.IsDropDownOpen=false;await Task.Delay(40);
      }
      Check(check.Focus(FocusState.Keyboard),"Checkbox cannot receive focus");FocusOnexOwnedControl(check);Check(ink.Visual.Opacity==0,"Previous control retained its focus stroke");
      var close=FindDescendantByName(dialog,"CloseButton") as Button;Check(close!=null&&close.Focus(FocusState.Keyboard),"Dialog footer focus unavailable");
      report["footerSystemFocus"]=JsonValue.CreateBooleanValue(close.UseSystemFocusVisuals);
      report["footerOwnedTemplate"]=JsonValue.CreateBooleanValue(OnexHasInlineFocus(close));
      Check(OnexHasInlineFocus(close)&&!close.UseSystemFocusVisuals,"Footer must use one owned focus template");
      FocusOnexOwnedControl(close);
      bool FooterActive()=>_onexControlLayers.TryGetValue(close,out var footerScope)&&ReferenceEquals(footerScope.Target,close)&&IsOnexFocusInkVisible(footerScope)&&ReferenceEquals(ElementCompositionPreview.GetElementChildVisual(close),footerScope.Ink.Root);
      await Wait(FooterActive,"Footer target-local focus did not become active");
      Check(FooterActive()&&close.FocusVisualPrimaryThickness.Equals(new Thickness(0)),"Footer needs one target-local ring without a duplicate inline stroke");
      report["footerTargetLocalFocus"]=JsonValue.CreateBooleanValue(true);
      if(!longContent){
       menu=CreateOnexMenu();menu.Items.Add(new MenuFlyoutItem{Text="Global Profile · Xbox 360"});menu.Items.Add(new MenuFlyoutSeparator());menu.Items.Add(new MenuFlyoutItem{Text="Custom preview · DualShock 4"});
       menu.ShowAt(first);await Task.Delay(80);Check(menu.Items.OfType<MenuFlyoutItem>().All(item=>item.IsEnabled),"Compact profile rows disabled");
       Check(menu.Items.OfType<MenuFlyoutSeparator>().Count()==1,"Compact profile grouping lost");menu.Hide();menu=null;
      }
      // RenderTargetBitmap explicitly excludes Popup content. Native template,
      // leaf/footer focus and actual animated scrolling assertions remain above.
      report["popupBitmapCaptureUnsupported"]=JsonValue.CreateBooleanValue(true);
      dialog.Hide();await opening;opening=null;_onexDetailsDialog=null;dialog=null;HideOnexAllFocusLayers();
     }
    }
    currentThemeName=oldName;_onexAppliedTheme=oldTheme;
    _releasePowerNav.IsChecked=true;await Wait(()=>_onexPerformanceProfilePicker.Choose.IsLoaded&&_onexPerformanceProfilePicker.Choose.ActualWidth>0,"Remapping preview anchor not visible");int remapPopups=0;
    foreach(bool tall in new[]{false,true}){
     var popupBody=new StackPanel{Spacing=12};var popupFirst=new Button{Content="Remap first preview",Style=OnexSettingsButtonStyle(false),MinHeight=44};
     popupBody.Children.Add(popupFirst);var popupChoice=OnexSettingsChoice("");popupChoice.ItemsSource=new[]{"Alpha preview","Beta preview"};popupChoice.SelectedIndex=0;popupBody.Children.Add(popupChoice);if(tall)popupBody.Children.Add(new Border{Height=900});
     var popupLast=new Button{Content="Remap last preview",Style=OnexSettingsButtonStyle(false),MinHeight=44};popupBody.Children.Add(popupLast);
     var popup=CreateOnexRemappingWindow(popupBody,OnexPalette.Orange);var popupView=_onexRemapWindows.Single(item=>ReferenceEquals(item.Flyout,popup));bool preparedBeforeShown=false;
     Check(popupView.Width<0&&popupView.NaturalHeight<0,"Closed remapping editor was prepared at construction");
     popup.Opening+=(sender,args)=>preparedBeforeShown=popupView.Width>0&&popupView.NaturalHeight>0;
     try{
      popup.ShowAt(_onexPerformanceProfilePicker.Choose);await Wait(()=>popup.IsOpen&&popupBody.IsLoaded,"Deferred remapping popup not loaded");await Task.Delay(90);
      var presenter=OnexRemapPresenter(popupBody);Check(preparedBeforeShown&&presenter!=null,"Remapping bounds were not ready in Opening");
      Check(presenter.ActualWidth>0&&presenter.ActualWidth<=500.5&&presenter.ActualHeight<=Math.Min(560,OnexRemapViewport().Height-40)+1,"Deferred popup exceeded viewport");
      Check(Math.Abs(presenter.ActualHeight-popupView.NaturalHeight)<1,"Deferred popup lost natural height");
      Check(tall?popupView.Scroll.ScrollableHeight>100:popupView.Scroll.ScrollableHeight<1,"Deferred short/tall popup scroll layout changed");
      foreach(var target in new[]{popupFirst,popupLast}){target.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(70);Check(target.Focus(FocusState.Keyboard),"Deferred popup control cannot focus");FocusOnexOwnedControl(target);Check(_onexControlLayers.TryGetValue(target,out var targetLayer)&&ReferenceEquals(targetLayer.Target,target)&&IsOnexFocusInkVisible(targetLayer),"Deferred popup lost owned leaf focus");}
      remapPopups++;
     }finally{popup.Hide();_onexRemapWindows.Remove(popupView);HideOnexAllFocusLayers();}
    }
    report["actualDeferredRemappingPopups"]=JsonValue.CreateNumberValue(remapPopups);report["remappingPreparedInOpening"]=JsonValue.CreateBooleanValue(true);
    _onexKeyboardMouseNav.IsChecked=true;SetKbmSettingsCard(_kbmSections[2]);await Wait(()=>_kbmTiles["base:A"].IsLoaded&&_kbmTiles["base:A"].ActualWidth>0,"Keyboard first-use anchor not visible");Check(_kbmFlyout==null,"Keyboard tab focus eagerly built its popup");
    string previousKbmSource=_kbmSource,previousKbmLayer=_kbmLayer;bool previousKbmSync=_kbmSync,previousKbmProjection=_kbmReadOnlyProjection;
    try{
     _kbmReadOnlyProjection=true;OpenKbmEditor("A","base",_kbmTiles["base:A"],true);
     await Wait(()=>_kbmFlyout?.IsOpen==true&&_kbmAction.IsLoaded,"Real keyboard editor did not load on first use");await Task.Delay(90);
     Check(_kbmKeys.Count==5&&_kbmKeys.All(choice=>choice.Items.Count==OnexKeyboardChoiceCodes.Length&&choice.Items.Cast<object>().All(item=>item is string)),"First-use keyboard editor lost its complete five-key catalog");
     var liveView=_onexRemapWindows.Single(item=>ReferenceEquals(item.Flyout,_kbmFlyout));var livePresenter=OnexRemapPresenter(liveView.Editor);
     Check(livePresenter!=null&&liveView.Width>0&&liveView.NaturalHeight>0&&livePresenter.ActualWidth<=500.5,"First-use keyboard popup was not sized before display");
     Check(_kbmAction.Focus(FocusState.Keyboard),"First-use keyboard action list cannot focus");
     _kbmSync=true;_kbmAction.SelectedIndex=_kbmAction.Items.Count-1;RefreshOnexRemappingWindows();await Task.Delay(90);
     Check(_kbmCustom.Visibility==Visibility.Visible&&_kbmKey1.Focus(FocusState.Keyboard),"First-use custom keyboard controls not available");
     Check(_kbmTuning.Count==0&&!_kbmBusy,"First-use visual editor queued an owner edit");
     report["actualKeyboardEditorFirstUse"]=JsonValue.CreateBooleanValue(true);
    }finally{
     _kbmFlyout?.Hide();_kbmSource=previousKbmSource;_kbmLayer=previousKbmLayer;_kbmSync=previousKbmSync;_kbmReadOnlyProjection=previousKbmProjection;RefreshKbmMapping();
    }
    var real=ShowOnexTileDetailsAsync(qsTileMap["OnexControllerProfile"].TileButton,"OnexControllerProfile");
    await Wait(()=>_onexDetailsDialog?.IsLoaded==true,"Real controller detail window did not open");dialog=_onexDetailsDialog;await Task.Delay(90);Frame(dialog);windows++;dialog.Hide();await real;dialog=null;
    // Packaging changed to .NET Native: exercise the real independent PRAID
    // through its existing probe branch, without touching a radio or device.
    string pairingToken=Guid.NewGuid().ToString("N");
    var pairingFolder=await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFolderAsync(OnexBluetoothPairingHost.FolderName,Windows.Storage.CreationCollisionOption.OpenIfExists);
    JsonObject pairingResult=null;
    try{
     var pairingRequest=new JsonObject{{"schema",JsonValue.CreateNumberValue(1)},{"token",JsonValue.CreateStringValue(pairingToken)},{"createdUtc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))},{"mode",JsonValue.CreateStringValue("probe")},{"deviceId",JsonValue.CreateStringValue("")}};
     await Windows.Storage.FileIO.WriteTextAsync(await pairingFolder.CreateFileAsync(pairingToken+".request.json",Windows.Storage.CreationCollisionOption.FailIfExists),pairingRequest.Stringify());
     var pairReply=await SendOnexHomeRequestAsync(new Windows.Foundation.Collections.ValueSet{{"OnexBluetoothPairHost",pairingToken}});
     for(int attempt=0;attempt<220;attempt++){
      // File creation precedes the asynchronous write. An empty/incomplete
      // result is still pending; keep the same bounded wait and final checks.
      try{if(await pairingFolder.TryGetItemAsync(pairingToken+".result.json") is Windows.Storage.StorageFile resultFile&&JsonObject.TryParse(await Windows.Storage.FileIO.ReadTextAsync(resultFile),out var candidate)&&candidate.GetNamedBoolean("isFinal",false)){pairingResult=candidate;break;}}catch(System.Runtime.InteropServices.COMException){}catch(ArgumentException){}
      await Task.Delay(250);
     }
     Check(pairingResult?.GetNamedString("status","")=="ProbeCompleted","Native independent Bluetooth host probe failed");
     Check(pairingResult.GetNamedString("applicationId","")=="BluetoothPairing"&&pairingResult.GetNamedNumber("processId",0)!=Windows.System.Diagnostics.ProcessDiagnosticInfo.GetForCurrentProcess().ProcessId,"Bluetooth host reused widget process/PRAID");
     report["independentPairingHostProbe"]=JsonValue.CreateBooleanValue(true);report["radioPairingPerformed"]=JsonValue.CreateBooleanValue(false);
    }finally{
     foreach(string suffix in new[]{".request.json",".running.json",".result.json",".cancel.json"})try{var item=await pairingFolder.TryGetItemAsync(pairingToken+suffix);if(item!=null)await item.DeleteAsync();}catch(System.Runtime.InteropServices.COMException){}catch(System.IO.FileNotFoundException){}
    }
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualDialogs"]=JsonValue.CreateNumberValue(windows);
    report["animatedScrollSamples"]=JsonValue.CreateNumberValue(scrollSamples);report["actualContainerFocusCases"]=JsonValue.CreateNumberValue(containerFocus);
    report["themes"]=new JsonArray{JsonValue.CreateStringValue("ONEXPLAYER"),JsonValue.CreateStringValue("Windows 11 Light")};
    report["temporaryThemeFieldsOnly"]=JsonValue.CreateBooleanValue(true);report["ownerSettingsWritten"]=JsonValue.CreateBooleanValue(false);
    report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["compositionFocusPixelsCaptured"]=JsonValue.CreateBooleanValue(false);
   }finally{
    menu?.Hide();dialog?.Hide();if(opening!=null)await opening;_onexDetailsDialog=null;HideOnexAllFocusLayers();
    currentThemeName=oldName;_onexAppliedTheme=oldTheme;ApplyOnexTabPalette();if(oldNav!=null)oldNav.IsChecked=true;
   }
  }
 }
}
