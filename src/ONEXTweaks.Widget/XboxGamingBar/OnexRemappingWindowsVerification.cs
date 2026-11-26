// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexControllerSketchAsync(JsonObject report)
  {
   void Check(bool value,string why){if(!value)throw new InvalidOperationException(why);}
   var tab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   string controller=null,kbm=null;
   bool oldSync=_onexControllerSync,oldKbmSync=_kbmSync;
   var images=new JsonArray();
   async Task WaitForEditor(Func<bool> ready,string message)
   {
    for(int attempt=0;attempt<20&&!ready();attempt++)await Task.Delay(50);
    Check(ready(),message);
   }
   async Task InspectEditor(Viewbox diagram,string image)
   {
    var view=_onexInlineRemapWindow;
    Check(view?.Sketch?.Artwork==diagram&&!view.Flyout.IsOpen,"The sketch editor did not open inline.");
    var host=view.Sketch;
    host.Root.UpdateLayout();
    var panel=host.Frame.TransformToVisual(host.Root).TransformBounds(new Rect(0,0,host.Frame.ActualWidth,host.Frame.ActualHeight));
    var screen=host.Screen;
    Check(panel.Width>0&&panel.Height>0&&panel.Left>=screen.Left-1&&panel.Right<=screen.Right+1&&
        panel.Top>=screen.Top-1&&panel.Bottom<=screen.Bottom+1,"The inline editor escaped the illustrated screen.");
    await SaveOnexImage(image,host.Root,80);
    images.Add(JsonValue.CreateStringValue(image));
   }
   async Task Inspect(Viewbox view,string[] ids,string imageName)
   {
    var canvas=(Canvas)view.Child;
    var surface=(ControllerSketchSurface)canvas.Tag;
    Check(canvas.Children.OfType<Canvas>().Any(layer=>layer.Children.OfType<Windows.UI.Xaml.Shapes.Path>().Any()),
        "The controller's vector artwork did not load.");
    var buttons=canvas.Children.OfType<Button>().ToArray();
    Check(buttons.Select(button=>(string)button.Tag).OrderBy(id=>id).SequenceEqual(ids.OrderBy(id=>id)),
        "The artwork does not contain the expected mapping IDs.");
    foreach(var button in buttons)
    {
     Check(Canvas.GetLeft(button)>=0&&Canvas.GetTop(button)>=0&&Canvas.GetLeft(button)+button.Width<=canvas.Width&&
         Canvas.GetTop(button)+button.Height<=canvas.Height,"A mapping hit target is outside the artwork.");
     Check(ReferenceEquals(surface.Hit(ControllerSketchTargets[(string)button.Tag].Center),button),"An approved tap target is unreachable.");
    }
    if(ids.Contains("LT"))
    {
     Check((string)surface.Hit(new Point(65/2d,87/2d))?.Tag=="LT"&&
         (string)surface.Hit(new Point(65/2d,111/2d))?.Tag=="LB","The shoulder seam selects the wrong input.");
     Check((string)surface.Hit(new Point(1124/2d,618/2d))?.Tag=="B","B's outer finger target is missing.");
    }
    if(ids.Contains("OEM:M2"))
     Check((string)surface.Hit(new Point(1138/2d,430/2d))?.Tag=="OEM:M2","M2's outer finger target is missing.");
    view.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
    await Task.Delay(100);
    await SaveOnexImage(imageName,view,80);
    images.Add(JsonValue.CreateStringValue(imageName));
   }
   try
   {
    LegionNavItem.IsChecked=true;
    await ReadOnexControllerAsync();await ReadOnexKeyboardMouseAsync();
    Check(_onexControllerPending==null&&!_onexControllerBusy&&!_kbmBusy&&_kbmTuning.Count==0,"Protect owner pending edits.");
    controller=_onexControllerState.Stringify();kbm=_kbmState.Stringify();
    _onexControllerReadOnlyProjection=true;_kbmReadOnlyProjection=true;
    var projected=JsonObject.Parse(controller);
    projected["enabled"]=JsonValue.CreateBooleanValue(true);projected["oemEnabled"]=JsonValue.CreateBooleanValue(true);
    var policy=projected.GetNamedObject("editor").GetNamedObject("policy");
    policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(false);
    policy["bindings"]=JsonObject.Parse("{\"Y\":{\"kind\":\"macro\",\"macro\":{\"buttons\":[\"A\",\"B\"],\"mode\":\"once\",\"intervalMs\":80,\"pressMs\":40}}}");
    _onexControllerSync=true;ApplyOnexControllerState(projected.Stringify());
    if(_onexControllerOpen!="mapping")SetOnexControllerCard("mapping");
    await Inspect(_onexControllerDiagram,ControllerInputs,"onex-controller-sketch.png");
    var gamepadSketch=(ControllerSketchSurface)((Canvas)_onexControllerDiagram.Child).Tag;
    string originalHighlight=gamepadSketch.HighlightedId;
    string pressedId=originalHighlight=="B"?"A":"B";
    gamepadSketch.StartPress(_onexControllerDiagramButtons[pressedId]);
    Check(gamepadSketch.HighlightedId==pressedId&&!IsOnexRemapOpen(_onexControllerMappingFlyout),"Gamepad press did not highlight before opening the editor.");
    gamepadSketch.Cancel();
    Check(gamepadSketch.HighlightedId==originalHighlight&&!IsOnexRemapOpen(_onexControllerMappingFlyout),"Cancel left a pressed highlight or opened the editor.");
    InvokeLightingButton(_onexControllerDiagramButtons["A"]);await Task.Delay(150);
    Check(IsOnexRemapOpen(_onexControllerMappingFlyout)&&ControllerSourceId=="A","A on the sketch did not open the correct controller editor.");
    await InspectEditor(_onexControllerDiagram,"onex-controller-screen-editor.png");
    InvokeLightingButton(_onexControllerDiagramButtons["B"]);await Task.Delay(150);
    Check(ControllerSourceId=="B"&&gamepadSketch.HighlightedId=="B","The inline editor did not switch directly to B.");
    await InspectEditor(_onexControllerDiagram,"onex-controller-screen-editor-b.png");
    _onexControllerSync=true;
    _onexControllerKind.SelectedIndex=2;_onexControllerGamepadMode.SelectedIndex=1;UpdateOnexControllerMappingVisibility();
    MatchOnexControllerSketchEditorToAction();
    await WaitForEditor(()=>_onexControllerMappingFlyout?.IsOpen==true,"The macro editor never opened.");
    Check(_onexControllerMappingFlyout?.IsOpen==true&&_onexInlineRemapWindow==null&&ControllerSourceId=="B",
        "Choosing a macro did not expand its editor for the same button.");
    CloseOnexRemapEditor(_onexControllerMappingFlyout);
    await Task.Delay(100);
    InvokeLightingButton(_onexControllerDiagramButtons["Y"]);await Task.Delay(150);
    Check(_onexControllerMappingFlyout?.IsOpen==true&&_onexInlineRemapWindow==null&&ControllerSourceId=="Y","The macro did not open full-size.");
    var macroFlyout=_onexControllerMappingFlyout;
    var macroView=_onexRemapWindows.Single(item=>item.Flyout==macroFlyout);
    var presenter=OnexRemapPresenter(macroView.Editor);
    Check(presenter!=null,"Macro editor has no native presenter.");
    presenter.UpdateLayout();
    double sketchWidth=Math.Min(610,Math.Max(120,OnexRemapViewport().Width-24));
    Check(Math.Abs(presenter.ActualWidth-sketchWidth)<2,"Macro editor width differs from the sketch.");
    Check(macroView.Close.ActualWidth>=30&&macroView.Close.IsTabStop&&OnexControllerEditorFocusControls().Contains(macroView.Close),
        "The macro close button is missing or unreachable.");
    report["macroSketchWidth"]=JsonValue.CreateNumberValue(sketchWidth);
    report["macroWindowWidth"]=JsonValue.CreateNumberValue(presenter.ActualWidth);
    InvokeLightingButton(macroView.Close);
    await WaitForEditor(()=>_onexControllerMappingFlyout==null,"The close button did not dismiss the macro editor.");
    Check(_onexInlineRemapWindow==null,"Closing a macro reopened the sketch editor.");
    InvokeLightingButton(_onexControllerDiagramButtons["Y"]);
    await WaitForEditor(()=>_onexControllerMappingFlyout?.IsOpen==true,"The saved macro did not reopen.");
    // Project the same binding state returned by Restore Default without writing the owner's profile.
    policy.GetNamedObject("bindings").Remove("Y");
    ApplyOnexControllerState(projected.Stringify());
    await WaitForEditor(()=>_onexInlineRemapWindow!=null,"The reset response did not return the editor to the sketch.");
    Check(ControllerSourceId=="Y"&&_onexControllerKind.SelectedIndex==0&&gamepadSketch.HighlightedId=="Y",
        "Restoring defaults changed the selected source or left a macro selected.");
    await InspectEditor(_onexControllerDiagram,"onex-controller-screen-editor-after-reset.png");
    CloseOnexRemapEditor(_onexControllerMappingFlyout);
    SetOnexControllerCard("extra");
    await Inspect(_onexControllerExtraDiagram,OnexControllerOemSources,"onex-controller-extra-sketch.png");
    InvokeLightingButton(_onexControllerDiagramButtons["OEM:M1"]);await Task.Delay(150);
    Check(IsOnexRemapOpen(_onexControllerMappingFlyout)&&ControllerSourceId=="OEM:M1","M1 on the sketch did not open the OEM editor.");
    await InspectEditor(_onexControllerExtraDiagram,"onex-controller-oem-screen-editor.png");
    CloseOnexRemapEditor(_onexControllerMappingFlyout);

    EnsureOnexKeyboardMouseFrontend();_onexKeyboardMouseNav.IsChecked=true;
    var keyboard=JsonObject.Parse(kbm);
    keyboard.GetNamedObject("policy")["fnButton"]=JsonValue.CreateStringValue("OEM:M1");
    _kbmSync=true;ApplyOnexKeyboardMouseState(keyboard.Stringify());
    var both=ControllerInputs.Concat(OnexControllerOemSources).ToArray();
    await Inspect(_kbmDiagrams["base"],both,"onex-keyboard-mouse-base-sketch.png");
    InvokeLightingButton(_kbmDiagramKeys["base:RT"]);await Task.Delay(150);
    Check(IsOnexRemapOpen(_kbmFlyout)&&_kbmLayer=="base"&&_kbmSource=="RT","RT on the sketch did not open its base-layer editor.");
    await InspectEditor(_kbmDiagrams["base"],"onex-keyboard-base-screen-editor.png");
    CloseOnexRemapEditor(_kbmFlyout);
    var fnSketch=(ControllerSketchSurface)((Canvas)_kbmDiagrams["base"].Child).Tag;
    fnSketch.StartPress(_kbmDiagramKeys["base:OEM:M2"]);
    Check(fnSketch.HighlightedId=="OEM:M2"&&!IsOnexRemapOpen(_kbmFlyout),"Press did not highlight before opening the shared editor.");
    fnSketch.Cancel();
    InvokeLightingButton(_kbmDiagramKeys["base:OEM:M2"]);await Task.Delay(150);
    var sameEditor=_kbmFlyout;
    InvokeLightingButton(_kbmFnLayerButton);
    await Task.Delay(80);
    Check(IsOnexRemapOpen(sameEditor)&&ReferenceEquals(sameEditor,_kbmFlyout)&&_kbmLayer=="fn"&&_kbmSource=="OEM:M2","Fn switched away from the shared button editor.");
    await InspectEditor(_kbmDiagrams["base"],"onex-keyboard-fn-layer-editor.png");
    Check(_onexControllerPending==null&&_kbmTuning.Count==0,"The visual check queued owner edits.");
    report["interactiveSketchPassed"]=JsonValue.CreateBooleanValue(true);
    report["controllerPressBeforeEditor"]=JsonValue.CreateBooleanValue(true);
    report["controllerTapTargetCenters"]=JsonValue.CreateNumberValue(60);
    report["inlineBindingsAndFullMacroPassed"]=JsonValue.CreateBooleanValue(true);
    report["macroWidthCloseAndResetPassed"]=JsonValue.CreateBooleanValue(true);
    report["ownerProfileEdits"]=JsonValue.CreateNumberValue(0);
    report["images"]=images;
   }
   finally
   {
    CloseOnexRemapEditor(_onexControllerMappingFlyout);CloseOnexRemapEditor(_kbmFlyout);
    _onexControllerSync=oldSync;_kbmSync=oldKbmSync;
    if(controller!=null)ApplyOnexControllerState(controller);
    if(kbm!=null)ApplyOnexKeyboardMouseState(kbm);
    _onexControllerReadOnlyProjection=false;_kbmReadOnlyProjection=false;
    if(tab!=null)tab.IsChecked=true;
   }
  }

  private async Task VerifyOnexRemappingWindowsAsync(JsonObject report)
  {
   bool projectionEntered=false;int checks=0,cases=0,focused=0,dropdownItems=0;void Check(bool value,string why){checks++;if(!value)throw new InvalidOperationException(why);}
   Rect Bounds(FrameworkElement element)=>element.TransformToVisual(null).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
   bool Inside(Rect outer,Rect inner)=>inner.Width>0&&inner.Height>0&&inner.Left>=outer.Left-1&&inner.Top>=outer.Top-1&&inner.Right<=outer.Right+1&&inner.Bottom<=outer.Bottom+1;
   var tab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);string controller=null,kbm=null;string source=ControllerSourceId;bool oldSync=_onexControllerSync,oldKbmSync=_kbmSync;var originalSize=new Size(widget.WindowBounds.Width,widget.WindowBounds.Height);Size? ownSize=null;var sizes=new JsonArray();var geometries=new JsonArray();report["geometries"]=geometries;
   async Task Inspect(Flyout flyout,StackPanel editor,string image)
   {
    await Task.Delay(140);editor.UpdateLayout();var presenter=OnexRemapPresenter(editor);Check(flyout.IsOpen&&presenter!=null,"Remapping popup did not open");var root=Bounds(this);var box=Bounds(presenter);var viewport=OnexRemapViewport();report["lastGeometry"]=new JsonObject{{"case",JsonValue.CreateNumberValue(cases)},{"rootWidth",JsonValue.CreateNumberValue(root.Width)},{"rootHeight",JsonValue.CreateNumberValue(root.Height)},{"viewportWidth",JsonValue.CreateNumberValue(viewport.Width)},{"viewportHeight",JsonValue.CreateNumberValue(viewport.Height)},{"actualWidth",JsonValue.CreateNumberValue(presenter.ActualWidth)},{"actualHeight",JsonValue.CreateNumberValue(presenter.ActualHeight)},{"maxWidth",JsonValue.CreateNumberValue(presenter.MaxWidth)},{"maxHeight",JsonValue.CreateNumberValue(presenter.MaxHeight)},{"requestedHeight",JsonValue.CreateNumberValue(double.IsNaN(presenter.Height)?-1:presenter.Height)}};geometries.Add(JsonObject.Parse(report.GetNamedObject("lastGeometry").Stringify()));Check(Inside(root,box),"Remapping popup outside widget viewport");
    var view=_onexRemapWindows.Single(item=>ReferenceEquals(item.Flyout,flyout));
    bool wide=OnexWideAssignmentWindow(view);
    double maxWidth=Math.Min(wide?610:500,Math.Max(1,viewport.Width-8));
    Check(presenter.ActualWidth<=maxWidth+1&&presenter.ActualHeight<=Math.Min(wide?640:560,viewport.Height-40)+1,"Remapping popup did not obey responsive limits");Check(flyout.ShouldConstrainToRootBounds&&flyout.Placement==FlyoutPlacementMode.Full,"Remapping popup is not root constrained");
    report.GetNamedObject("lastGeometry")["scrollWidth"]=JsonValue.CreateNumberValue(view.Scroll.ActualWidth);report.GetNamedObject("lastGeometry")["editorWidth"]=JsonValue.CreateNumberValue(editor.ActualWidth);Check(Inside(box,Bounds(view.Scroll)),"Remapping scroll area exceeds outer popup");Check(editor.ActualWidth<=presenter.ActualWidth-40,"Remapping editor is wider than popup content area");Check(Math.Abs(presenter.ActualHeight-view.NaturalHeight)<1,"Short remapping popup did not keep natural height");Check(view.Scroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled&&view.Scroll.Padding.Left>=4&&view.Scroll.Padding.Right>=12,"Scroll focus gutter missing");
    var controls=OnexOwnedContent(editor).OfType<Control>().Where(control=>!(control is ComboBoxItem)&&control.IsEnabled&&control.IsTabStop&&control.Visibility==Visibility.Visible).ToArray();Check(controls.Length>0,"No editable controls in remapping popup");
    foreach(var control in new[]{controls.First(),controls[controls.Length/2],controls.Last()}.Distinct())
    {
     control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(80);Check(control.Focus(FocusState.Keyboard),"Remapping focus failed");control.UpdateLayout();await Task.Delay(30);Check(Inside(Bounds(view.Scroll),Bounds(control))&&Inside(Bounds(presenter),Bounds(control)),"Focused remapping control clipped by popup or scroll viewport");Check(control.FocusVisualMargin.Left>=2&&control.FocusVisualMargin.Top>=2&&control.FocusVisualMargin.Right>=2&&control.FocusVisualMargin.Bottom>=2,"Focus outline is not inset");focused++;
    }
    var choice=controls.OfType<ComboBox>().FirstOrDefault();if(choice!=null)
    {
     choice.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(70);choice.IsDropDownOpen=true;await Task.Delay(100);Check(choice.MaxDropDownHeight<=300&&choice.MaxDropDownHeight<OnexRemapViewport().Height,"Dropdown height not bounded");
     var realized=choice.Items.OfType<ComboBoxItem>().Where(item=>item.IsEnabled&&item.ActualWidth>0&&Inside(root,Bounds(item))).ToArray();Check(realized.Length>0,"Dropdown has no visible selectable rows");
     foreach(var item in new[]{realized.First(),realized.Last()}.Distinct()){Check(item.FocusVisualMargin.Left>=2&&item.FocusVisualMargin.Right>=2&&item.ContentTemplate!=null,"Dropdown row focus/wrapped text missing");dropdownItems++;}
     choice.IsDropDownOpen=false;await Task.Delay(70);
    }
    if(image!=null){try{await SaveOnexImage(image,editor,80);}catch{report["popupImageExportUnavailable"]=JsonValue.CreateBooleanValue(true);}}flyout.Hide();await Task.Delay(80);cases++;
   }
   try
   {
    LegionNavItem.IsChecked=true;await ReadOnexControllerAsync();await ReadOnexKeyboardMouseAsync();Check(_onexControllerPending==null&&!_onexControllerBusy&&_kbmTuning.Count==0&&!_kbmBusy,"Protect owner pending edits");controller=_onexControllerState.Stringify();kbm=_kbmState.Stringify();_onexControllerReadOnlyProjection=true;_kbmReadOnlyProjection=true;projectionEntered=true;
    var projected=JsonObject.Parse(controller);projected["enabled"]=JsonValue.CreateBooleanValue(true);projected["oemEnabled"]=JsonValue.CreateBooleanValue(true);var policy=projected.GetNamedObject("editor").GetNamedObject("policy");policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(false);
    policy["bindings"]=JsonObject.Parse("{\"A\":{\"kind\":\"macro\",\"macro\":{\"buttons\":[\"A\",\"B\",\"RT\"],\"mode\":\"repeatHeld\",\"intervalMs\":80,\"pressMs\":40}},\"X\":{\"kind\":\"keyboard\",\"keys\":[17,16,65,66,67]}}");
    policy["oem"]=JsonObject.Parse("{\"bindings\":{\"M1\":{\"kind\":\"keyboard\",\"keys\":[17,16,65,66,67]}},\"doublePresses\":{\"M1\":{\"enabled\":true,\"windowMs\":300,\"binding\":{\"kind\":\"gamepad\",\"target\":\"A\"}}}}");
    var keyboard=JsonObject.Parse(kbm);keyboard.GetNamedObject("policy")["fnButton"]=JsonValue.CreateStringValue("OEM:M1");
    for(int size=0;size<2;size++)
    {
     if(size==1){_onexControllerMappingFlyout?.Hide();_kbmFlyout.Hide();Check(Math.Abs(widget.WindowBounds.Width-originalSize.Width)<1&&Math.Abs(widget.WindowBounds.Height-originalSize.Height)<1,"Owner changed widget size before QA resize");Check(await widget.TryResizeWindowAsync(new Size(originalSize.Width,520)),"Supported compact-height resize refused");ownSize=new Size(originalSize.Width,520);await Task.Delay(180);Check(Math.Abs(widget.WindowBounds.Height-520)<1,"Compact-height request not applied");}
     sizes.Add(new JsonObject{{"width",JsonValue.CreateNumberValue(widget.WindowBounds.Width)},{"height",JsonValue.CreateNumberValue(widget.WindowBounds.Height)}});
     LegionNavItem.IsChecked=true;_onexControllerSync=true;ApplyOnexControllerState(projected.Stringify());if(_onexControllerOpen!="mapping")SetOnexControllerCard("mapping");SelectOnexControllerMappingSource("A",_onexControllerInputTiles["A"],true);await Inspect(_onexControllerMappingFlyout,_onexControllerMappingEditor,size==1?"onex-remap303-controller-macro.png":null);
     SelectOnexControllerMappingSource("X",_onexControllerInputTiles["X"],true);await Inspect(_onexControllerMappingFlyout,_onexControllerMappingEditor,null);
     SetOnexControllerCard("extra");SelectOnexControllerMappingSource("OEM:M1",_onexControllerInputTiles["OEM:M1"],true);await Inspect(_onexControllerMappingFlyout,_onexControllerMappingEditor,null);
     _onexKeyboardMouseNav.IsChecked=true;_kbmSync=true;ApplyOnexKeyboardMouseState(keyboard.Stringify());SetKbmSettingsCard(_kbmSections[2]);OpenKbmEditor("A","base",_kbmTiles["base:A"],true);await Inspect(_kbmFlyout,_onexRemapWindows.Single(view=>ReferenceEquals(view.Flyout,_kbmFlyout)).Editor,size==1?"onex-remap303-kbm-simple.png":null);
     SelectKbmEditorLayer("fn");_kbmSync=true;_kbmAction.SelectedIndex=_kbmAction.Items.Count-1;_kbmCustom.Visibility=Visibility.Visible;RefreshOnexRemappingWindows();await Inspect(_kbmFlyout,_onexRemapWindows.Single(view=>ReferenceEquals(view.Flyout,_kbmFlyout)).Editor,size==1?"onex-remap303-kbm-custom.png":null);
    }
    Check(_onexControllerPending==null&&_kbmTuning.Count==0,"Visual remapping test queued owner edits");report["checks"]=JsonValue.CreateNumberValue(checks);report["popupCases"]=JsonValue.CreateNumberValue(cases);report["focusedControls"]=JsonValue.CreateNumberValue(focused);report["visibleDropdownRows"]=JsonValue.CreateNumberValue(dropdownItems);report["actualWidgetSizes"]=sizes;report["ownerProfileEdits"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["uiEvidence"]=JsonValue.CreateStringValue("actual UWP popup geometry/focus/dropdown layout; projected settings and programmatic keyboard focus; no owner mappings changed");
   }
   finally
   {
    _onexControllerMappingFlyout?.Hide();_kbmFlyout?.Hide();if(projectionEntered){_onexControllerSaveTimer?.Stop();Check(_onexControllerPending==null,"Visual projection unexpectedly queued a controller edit");}_onexControllerSync=oldSync;_kbmSync=oldKbmSync;if(controller!=null)ApplyOnexControllerState(controller);if(kbm!=null)ApplyOnexKeyboardMouseState(kbm);_onexControllerReadOnlyProjection=false;_kbmReadOnlyProjection=false;
    if(ownSize.HasValue){var current=new Size(widget.WindowBounds.Width,widget.WindowBounds.Height);Check(Math.Abs(current.Width-ownSize.Value.Width)<1&&Math.Abs(current.Height-ownSize.Value.Height)<1,"Owner changed widget size during QA; no stale resize restore");Check(await widget.TryResizeWindowAsync(originalSize),"Original widget size restore failed");await Task.Delay(180);Check(Math.Abs(widget.WindowBounds.Width-originalSize.Width)<1&&Math.Abs(widget.WindowBounds.Height-originalSize.Height)<1,"Original widget size mismatch");}
    await ReadOnexControllerAsync();await ReadOnexKeyboardMouseAsync();if(source!=null)_onexControllerSource.SelectedItem=_onexControllerSource.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==source);if(_onexControllerOpen!=null)SetOnexControllerCard(null);if(tab!=null)tab.IsChecked=true;
   }
  }
 }
}
