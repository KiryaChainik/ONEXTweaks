// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private bool _onexTabPalettePainting;
        private readonly ConditionalWeakTable<FrameworkElement,Dictionary<string,object>> _onexScopeOriginals=new ConditionalWeakTable<FrameworkElement,Dictionary<string,object>>();
        private readonly List<WeakReference<FrameworkElement>> _onexAccentScopes=new List<WeakReference<FrameworkElement>>();
        private sealed class OnexScopedBrushSnapshot
        {internal readonly Color Color;internal OnexScopedBrushSnapshot(Color color){Color=color;}}
        private void StoreOnexScopedResourceGroup(FrameworkElement scope,string role,IEnumerable<string> keys,object value)
        {
            if(!_onexScopeOriginals.TryGetValue(scope,out var original)){original=new Dictionary<string,object>();_onexScopeOriginals.Add(scope,original);_onexAccentScopes.Add(new WeakReference<FrameworkElement>(scope));}
            var names=keys as string[]??keys.ToArray();
            // Resource lookup can resolve theme/merged dictionaries. Only a
            // pre-existing LOCAL entry belongs in this scope's restoration map;
            // inherited values must reappear by removing our local override.
            // Capture the original before mutating the shared private role brush.
            HashSet<object> originalLocalKeys=null;
            foreach(string key in names)if(!original.ContainsKey(key))
            {
                if(originalLocalKeys==null)originalLocalKeys=new HashSet<object>(scope.Resources.Keys);
                bool local=originalLocalKeys.Contains(key);bool found=scope.Resources.TryGetValue(key,out var current);
                if(App.OnexTileRevisionStage==209){if(local&&found)_onexOriginalLocalValuesCaptured++;else if(found){_onexOriginalInheritedLookupsSkipped++;if(key=="ToggleButtonBorderBrushChecked")_onexOriginalCheckedBorderInheritedLookupsSkipped++;}}
                original[key]=local&&found?(current is SolidColorBrush oldBrush?(object)new OnexScopedBrushSnapshot(oldBrush.Color):current):null;
            }
            object owned=value is SolidColorBrush desired?OnexOwnedResourceBrush(scope,role,desired.Color):value;
            foreach(string key in names)try{if(!scope.Resources.TryGetValue(key,out var current)||!ReferenceEquals(current,owned))scope.Resources[key]=owned;}
            catch(Exception ex){Logger.Warn("Optional ONEX grouped scope resource rejected: "+key+" / "+ex.HResult.ToString("X8"));}
        }
        private int _onexOriginalLocalValuesCaptured,_onexOriginalInheritedLookupsSkipped,_onexOriginalCheckedBorderInheritedLookupsSkipped;
        private string _onexRestoreScopeType="",_onexRestoreKey="",_onexRestoreOperation="",_onexRestoreOriginalType="";
        private bool _onexRestoreKeyLocal;private int _onexRestoreLocalCount;
        private void RestoreOnexScopedResources()
        {
            foreach(var reference in _onexAccentScopes)if(reference.TryGetTarget(out var scope))RestoreOnexScopedResource(scope);
            _onexAccentScopes.Clear();
        }
        private void RestoreOnexScopedResource(FrameworkElement scope)
        {
            if(!_onexScopeOriginals.TryGetValue(scope,out var original))return;
            // Lookup can resolve merged resources. Remove only an entry that
            // exists in this dictionary's local map; an already-absent override
            // is already restored and must not be removed a second time.
            if(App.OnexTileRevisionStage==209){_onexRestoreOperation="enumerate-local";_onexRestoreKey="";_onexRestoreScopeType=scope.GetType().Name;}
            var localKeys=new HashSet<object>(scope.Resources.Keys);
            foreach(var item in original)
            {
                bool local=localKeys.Contains(item.Key);
                if(App.OnexTileRevisionStage==209){_onexRestoreKey=item.Key;_onexRestoreKeyLocal=local;_onexRestoreLocalCount=localKeys.Count;_onexRestoreOriginalType=item.Value?.GetType().Name??"absent";_onexRestoreOperation=item.Value==null?"remove-owned-override":item.Value is OnexScopedBrushSnapshot?"restore-solid-colour":"restore-original-object";}
                if(item.Value==null){if(local)scope.Resources.Remove(item.Key);}
                else if(item.Value is OnexScopedBrushSnapshot saved)
                {
                    scope.Resources.TryGetValue(item.Key,out var current);
                    if(current is SolidColorBrush present&&present.Color==saved.Color)continue;
                    // Grouped role brushes can back several live native keys.
                    // Preserve every private role reference, not just the first
                    // system-accent alias. The subsequent palette pass assigns
                    // the current semantic role before the page renders.
                    // Never mutate a platform/shared brush.
                    bool owned=current is SolidColorBrush&&_onexOwnedResourceGroups.TryGetValue(scope,out var groups)&&groups.Values.Any(brush=>ReferenceEquals(brush,current));
                    if(owned)
                    {
                        if(App.OnexTileRevisionStage==209)_onexRestoreOperation="restore-owned-colour-in-place";
                        ((SolidColorBrush)current).Color=saved.Color;
                    }
                    else scope.Resources[item.Key]=new SolidColorBrush(saved.Color);
                }
                else scope.Resources[item.Key]=item.Value;
            }
            _onexScopeOriginals.Remove(scope);
        }
        private Color OnexNavigationAccent(string tag)
        {switch(tag){case "KeyboardMouse":case "OnexOsd":case "Fan":case "Lighting":case "System":return OnexRoleAccent(1);case "Display":case "Legion":return OnexRoleAccent(2);case "Game":return OnexRoleAccent(3);default:return OnexRoleAccent(0);}}
        private Color OnexPerformanceAccent(string id)
        {switch(id){case "cpu":case "resolution":return OnexRoleAccent(2);case "mode":case "intel":case "fan":return OnexRoleAccent(1);case "fps":return OnexRoleAccent(3);default:return OnexRoleAccent(0);}}
        private Brush OnexPerformanceAccentBrush(string id)=>OnexSolidBrush(OnexPerformanceAccent(id));
        private static IEnumerable<DependencyObject> OnexOwnedContent(DependencyObject root)
        {
            if(root==null||root is FrameworkElement hidden&&hidden.Visibility==Visibility.Collapsed)yield break;yield return root;
            // Public logical content only. Never enumerate private UWP template descendants.
            if(root is ComboBox closed&&!closed.IsDropDownOpen)yield break;
            foreach(var child in ThemeContentChildren(root))foreach(var item in OnexOwnedContent(child))yield return item;
        }
        private void OnexAccentResources(FrameworkElement scope,Color color)
        {
            var accent=OnexSolidBrush(color);
            var keys=OnexControlAccentKeys.Concat(new[]{"SystemControlHighlightListAccentLowBrush","SystemControlHighlightListAccentMediumBrush","SystemControlHighlightListAccentHighBrush","ComboBoxSelectionIndicatorBrush","ComboBoxSelectionIndicatorForeground","ComboBoxSelectionIndicatorForegroundPointerOver","ComboBoxSelectionIndicatorForegroundPressed","ToggleSwitchFillOn","ToggleSwitchFillOnPointerOver","ToggleSwitchFillOnPressed","ToggleSwitchStrokeOn","ToggleSwitchStrokeOnPointerOver","ToggleSwitchStrokeOnPressed","ListViewItemSelectionIndicatorBrush","CheckBoxCheckBackgroundFillChecked","CheckBoxCheckBackgroundFillCheckedPointerOver","CheckBoxCheckBackgroundFillCheckedPressed","CheckBoxCheckBackgroundStrokeChecked"}).Distinct().ToArray();
            StoreOnexScopedResourceGroup(scope,"accent",keys,accent);
            if(scope is ToggleSwitch toggle)SetOnexThemeBrush(toggle,Control.BackgroundProperty,color);
            if(scope is CheckBox check)UpdateOnexOwnedCheck(check,color);
        }
        private void PaintOnexPageContent(DependencyObject root,Color color,bool preserveFanCurve=false)
        {
            foreach(var item in OnexOwnedContent(root))
            {
                if(item is Control owned)ConfigureOnexFocus(owned);
                if(item is ComboBox choice){OnexAccentResources(choice,color);choice.FocusVisualMargin=new Thickness(2);}
                else if(item is ToggleSwitch||item is CheckBox)OnexAccentResources((FrameworkElement)item,color);
                else if(item is Slider slider&&(!preserveFanCurve||!OnexFanForms.Any(form=>form.Sliders.Contains(slider))))slider.Foreground=OnexSolidBrush(color);
                else if(item is Button help&&help.Content as string=="?")help.Foreground=OnexSolidBrush(color);
            }
        }
        private void PaintOnexProfileHeader(Border card,OnexScopedProfilePicker picker,Color color)
        {if(card==null)return;card.BorderBrush=OnexSolidBrush(color);card.BorderThickness=new Thickness(1);var theme=_onexAppliedTheme??OnexPalette.CreateTheme();picker?.ApplyAccent(OnexSolidBrush(color),OnexSolidBrush(theme.TextPrimary),OnexSolidBrush(OnexSettingsCaptionColour(theme)));}
        private void PaintOnexPerformanceCards()
        {
            if(_onexPerfSections!=null)foreach(var section in _onexPerfSections)
            {
                var color=OnexPerformanceAccent(section.Id);var icon=OnexOwnedContent(section.Header.Content as DependencyObject).OfType<FontIcon>().FirstOrDefault();if(icon!=null){icon.Foreground=OnexSolidBrush(color);icon.Opacity=.85;}
                section.Header.BorderBrush=section.Id==_onexPerfOpen?OnexSolidBrush(color):(Brush)Resources["CardBorderBrush"];section.Detail.BorderBrush=OnexSolidBrush(color);section.Detail.BorderThickness=new Thickness(1);PaintOnexPageContent(section.Detail,color);
            }
            foreach(var form in new[]{_onexDcEditor,_onexAcEditor}.Where(value=>value!=null))foreach(var card in form.Cards)
            {
                var color=OnexPerformanceAccent(card.Id);var icon=OnexOwnedContent(card.Header.Content as DependencyObject).OfType<FontIcon>().FirstOrDefault();if(icon!=null){icon.Foreground=OnexSolidBrush(color);icon.Opacity=.85;}
                card.Header.BorderBrush=ReferenceEquals(form.Open,card)?OnexSolidBrush(color):(Brush)Resources["CardBorderBrush"];card.Detail.BorderBrush=OnexSolidBrush(color);card.Detail.BorderThickness=new Thickness(1);PaintOnexPageContent(card.Detail,color);
            }
        }
        // Space page-level sections only. Nested cards keep their own compact rhythm.
        private void ApplyOnexPageSpacing()
        {
            foreach(var page in new[]{GameScrollViewer})
                if(page?.Content is StackPanel body&&body.Spacing<12)body.Spacing=12;
            foreach(var page in new[]{SystemScrollViewer,PerformanceScrollViewer,DisplayScrollViewer,FanScrollViewer,LegionScrollViewer,_onexKeyboardMousePage,OnexOverlayScrollViewer})
                if(page?.Visibility==Visibility.Visible&&page.Content is StackPanel body)NormalizeOnexProfilePageSpacing(body);
            if(DisplayTopGroup!=null){DisplayTopGroup.Spacing=0;DisplayTopGroup.Padding=new Thickness(0);}
            foreach(var header in new[]{_onexPerformanceProfilePickerCard,_onexScreenProfilePickerCard,_onexFanProfilePickerCard,_onexControllerPickerCard})
                if(header!=null)header.Margin=new Thickness(header.Margin.Left,0,header.Margin.Right,0);
        }
        private static bool OnexVisiblePageSection(FrameworkElement item)
        {
            if(item==null||item.Visibility!=Visibility.Visible)return false;
            if(item is Panel panel)return panel.Children.OfType<FrameworkElement>().Any(OnexVisiblePageSection);
            if(item is Border border)return border.Child is FrameworkElement child?OnexVisiblePageSection(child):border.Height>0||border.MinHeight>0;
            if(item is TextBlock text)return !string.IsNullOrEmpty(text.Text);
            return true;
        }
        private static void NormalizeOnexProfilePageSpacing(StackPanel body)
        {
            // UWP StackPanel.Spacing also leaves gaps around hidden legacy/empty sections.
            // Use margins on actual visible sections only; never observe layout to reflow it.
            body.Spacing=0;body.Padding=new Thickness(16,8,16,16);bool first=true;
            foreach(var item in body.Children.OfType<FrameworkElement>())
            {
                bool section=OnexVisiblePageSection(item);item.Margin=new Thickness(item.Margin.Left,section&&!first?12:0,item.Margin.Right,0);
                if(section)first=false;
            }
        }
        private void ApplyOnexTabPalette()
        {
            if(!OnexFrontend.UsesOwnBackend||_onexTabPalettePainting)return;
            ApplyOnexPageSpacing();
            _onexTabPalettePainting=true;
            try
            {
                foreach(var nav in GetVisibleNavigationItems())ApplyNavPillTheme(nav);
                if(_releasePowerNav?.IsChecked==true){PaintOnexPageContent(PerformanceScrollViewer.Content as DependencyObject,OnexRoleAccent(0));PaintOnexPerformanceCards();PaintOnexProfileHeader(_onexPerformanceProfilePickerCard,_onexPerformanceProfilePicker,OnexRoleAccent(0));}
                if(DisplayScrollViewer?.Visibility==Visibility.Visible){PaintOnexPageContent(DisplayScrollViewer.Content as DependencyObject,OnexRoleAccent(2));
                    foreach(var value in new[]{DisplaySaturationValueText,DisplayBrightnessValueText,DisplayHueValueText,DisplayGammaValueText,DisplayContrastValueText})value.Foreground=OnexSolidBrush(OnexRoleAccent(2,true));
                    DisplaySharpnessValueText.Foreground=DisplaySharpnessSlider.IsEnabled?OnexSolidBrush(OnexRoleAccent(2,true)):OnexSettingsBrush(160,160,160);PaintOnexProfileHeader(_onexScreenProfilePickerCard,_onexScreenProfilePicker,OnexRoleAccent(2));_onexScreenSavedProfiles?.Accent(OnexSolidBrush(OnexRoleAccent(2)));if(_onexHdrCard!=null){_onexHdrCard.BorderBrush=OnexSolidBrush(OnexRoleAccent(2));_onexHdrCard.BorderThickness=new Thickness(1);}}
                if(FanScrollViewer?.Visibility==Visibility.Visible){PaintOnexPageContent(FanScrollViewer.Content as DependencyObject,OnexRoleAccent(1),true);PaintOnexProfileHeader(_onexFanProfilePickerCard,_onexFanProfilePicker,OnexRoleAccent(1));_onexFanSavedProfiles?.Accent(OnexSolidBrush(OnexRoleAccent(1)));}
                if(OnexOverlayScrollViewer?.Visibility==Visibility.Visible){PaintOnexOverlayMetricRows();PaintOnexPageContent(OnexOverlayScrollViewer.Content as DependencyObject,OnexRoleAccent(1));OnexOverlaySettingsCard.BorderBrush=OnexSolidBrush(OnexRoleAccent(1));OnexOverlaySettingsCard.BorderThickness=new Thickness(1);PaintOnexProfileHeader(_onexOverlayPresetHeader,_onexOverlayPresetPicker,OnexRoleAccent(1));_onexOverlaySavedPresets?.Accent(OnexSolidBrush(OnexRoleAccent(1)));}
                if(SystemScrollViewer?.Visibility==Visibility.Visible){PaintOnexPageContent(SystemScrollViewer.Content as DependencyObject,OnexRoleAccent(1));ApplyOnexSettingsDesign();}
                if(_onexKeyboardMousePage?.Visibility==Visibility.Visible)
                {
                    PaintOnexPageContent(_kbmBody,OnexKeyboardMouseAccent);
                    PaintKbmSettingsCards();
                }
                if(_onexLightingPage?.Visibility==Visibility.Visible)PaintOnexPageContent(_onexLightingPage,OnexNavigationAccent("Lighting"));
                if(LegionScrollViewer?.Visibility==Visibility.Visible){PaintOnexPageContent(LegionScrollViewer.Content as DependencyObject,OnexRoleAccent(2));PaintOnexProfileHeader(_onexControllerPickerCard,_onexControllerPicker,OnexRoleAccent(2));PaintOnexControllerCards();_onexControllerSavedProfiles?.Accent(OnexSolidBrush(OnexRoleAccent(2)));}
                if(GameScrollViewer?.Visibility==Visibility.Visible)PaintOnexPageContent(GameScrollViewer.Content as DependencyObject,OnexRoleAccent(3));
            }
            finally{ApplyOnexConnectedCardOutlines();_onexTabPalettePainting=false;}
        }
    }
}
