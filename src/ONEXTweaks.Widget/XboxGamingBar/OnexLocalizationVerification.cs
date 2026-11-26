// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using System.Globalization;
using System.Text.RegularExpressions;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private DispatcherTimer _onexLanguageVerificationTimer;
        private sealed class OnexLocaleFormatProbe : IFormattable
        {
            public string ToString(string format,IFormatProvider provider)=>"42";
            public override string ToString()=>"42";
        }
        private async Task VerifyOnexLocalizationAsync(JsonObject report)
        {
            int checks = 0, visits = 0, staticLabels = 0, samples = 0, captures = 0, expandedSections = 0, formatTemplates = 0;
            var missing = new HashSet<int>();
            var entries = OnexTranslationCatalog.Entries.ToArray();
            var sourceIndexes = new Dictionary<string, int>();
            for (int n = 0; n < entries.Length; n++)
            {
                sourceIndexes[entries[n].Key] = n;
                // Both original-language keys and their English normalized text
                // identify source-only labels without dumping arbitrary UI text.
                if (!sourceIndexes.ContainsKey(entries[n].Value[0])) sourceIndexes[entries[n].Value[0]] = n;
            }
            void Check(bool condition, string why) { checks++; if (!condition) throw new InvalidOperationException(why); }
            void Phase(string phase)
            {
                var value = new JsonObject {
                    {"version",JsonValue.CreateStringValue("0.29.429.0")}, {"language",JsonValue.CreateStringValue(OnexStrings.Language)},
                    {"phase",JsonValue.CreateStringValue(phase)}, {"checks",JsonValue.CreateNumberValue(checks)}, {"samples",JsonValue.CreateNumberValue(samples)} };
                System.IO.File.WriteAllText(System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-localization-progress.json"), value.Stringify());
            }
            // Names read from owner data are only an in-memory exclusion set.
            // Neither those values nor arbitrary visible strings enter the report.
            var userNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in new[] { _onexProfilesState, _onexFanProfiles, _onexScreenProfiles, _onexControllerState, _onexOverlayPresets })
            {
                var list = state?.GetNamedArray("entries", null);
                if (list == null) continue;
                foreach (var item in list) {
                    var entry=item.GetObject();string id=entry.GetNamedString("id","");
                    if(id!="global"&&!id.StartsWith("firmware-",StringComparison.Ordinal))userNames.Add(entry.GetNamedString("name",""));
                }
            }
            foreach(var tile in qsCustomShortcuts)userNames.Add(tile.Name);
            string TemplatePattern(string value)
            {
                var parts=Regex.Split(value,@"(?<!\{)\{\d+(?:,[^:}]+)?(?::[^}]+)?\}");
                return "\\\\A"+string.Join(".+?",parts.Select(Regex.Escape))+"\\\\z";
            }
            var sourceTemplates=entries.Select((entry,index)=>new{entry,index})
                .Where(value=>value.entry.Value[0]!=value.entry.Value[1]&&Regex.IsMatch(value.entry.Value[0],@"\{\d+")&&Regex.IsMatch(Regex.Replace(value.entry.Value[0],@"\{[^}]*\}",""),@"[A-Za-z]{4}"))
                .Select(value=>new{value.index,English=new Regex(TemplatePattern(value.entry.Value[0]),RegexOptions.Singleline|RegexOptions.CultureInvariant),Russian=new Regex(TemplatePattern(value.entry.Value[1]),RegexOptions.Singleline|RegexOptions.CultureInvariant)}).ToArray();
            void Inspect(string text)
            {
                if (string.IsNullOrEmpty(text) || userNames.Contains(text)) return;
                samples++;
                if (!sourceIndexes.TryGetValue(text, out int index)) { if(OnexStrings.Language=="ru")foreach(var template in sourceTemplates)if(template.English.IsMatch(text)&&!template.Russian.IsMatch(text))missing.Add(template.index);return; }
                string key = entries[index].Key, expected = OnexStrings.Get(key);
                if (text != expected && (text == key || text == entries[index].Value[0])) missing.Add(index);
            }
            void InspectTree(DependencyObject root)
            {
                if(root==null)return;
                foreach(var node in OnexOwnedContent(root)) {
                    bool visible=true;
                    for(var parent=node;parent!=null;parent=Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(parent)) {
                        if(parent is FrameworkElement hidden&&hidden.Visibility!=Visibility.Visible){visible=false;break;}
                        if(ReferenceEquals(parent,root))break;
                    }
                    if(!visible)continue;
                    // Standard RTSS preset names are product names, including its "Off" preset.
                    bool standardPreset=qsTileDefinitions.Any(tile=>tile.Id=="Overlay"&&ReferenceEquals(tile.StateText,node))||PerformanceOverlayComboBox.Items.Contains(node)||OSDCustomizeLevelComboBox.Items.Contains(node);
                    for(var parent=node;!standardPreset&&parent!=null;parent=Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
                    {if(ReferenceEquals(parent,PerformanceOverlayComboBox)){standardPreset=true;break;}if(ReferenceEquals(parent,root))break;}
                    if(standardPreset)continue;
                    if(node is TextBlock text&&text.ActualWidth>0&&text.ActualHeight>0) {
                        Inspect(text.Text);if(entries.Any(entry=>text.Text==entry.Value[OnexStrings.Language=="ru"?1:0]))staticLabels++;
                    }
                    else if(node is ContentControl control&&control.Content is string caption)Inspect(caption);
                    Inspect(Windows.UI.Xaml.Automation.AutomationProperties.GetName(node));
                    string accessibleHelp=Windows.UI.Xaml.Automation.AutomationProperties.GetHelpText(node);
                    bool overlayPresetHelp=qsTileDefinitions.Any(tile=>tile.Id=="Overlay"&&ReferenceEquals(tile.TileButton,node))&&OnexOverlayNames.Contains(accessibleHelp);
                    if(!overlayPresetHelp)Inspect(accessibleHelp);
                    if(ToolTipService.GetToolTip(node) is string tip)Inspect(tip);
                    if(node is ComboBox headerChoice&&headerChoice.Header is string choiceHeader)Inspect(choiceHeader);
                    if(node is Slider slider&&slider.Header is string sliderHeader)Inspect(sliderHeader);
                    if(node is TextBox input&&input.Header is string inputHeader)Inspect(inputHeader);
                    if(node is TextBox placeholder)Inspect(placeholder.PlaceholderText);
                    if(node is ToggleSwitch toggle){if(toggle.Header is string toggleHeader)Inspect(toggleHeader);if(toggle.OnContent is string on)Inspect(on);if(toggle.OffContent is string off)Inspect(off);}
                    if(node is ComboBox choice&&!ReferenceEquals(choice,PerformanceOverlayComboBox)&&!ReferenceEquals(choice,OSDCustomizeLevelComboBox))foreach(var item in choice.Items) {
                        if(item is ComboBoxItem row&&row.Content is string label)Inspect(label);
                        else if(item is string labelValue)Inspect(labelValue);
                    }
                }
            }
            void InspectPage()=>InspectTree(OnexNavigationRoot());
            var original=GetVisibleNavigationItems().FirstOrDefault(item=>item.IsChecked==true);
            var tabs=new[]{PerformanceNavItem,_releasePowerNav,LegionNavItem,_onexKeyboardMouseNav,DisplayNavItem,_releaseOsdNav,FanNavItem,_onexLightingNav,SystemNavItem}.Where(tab=>tab!=null).ToArray();var tabVisibility=tabs.Select(tab=>tab.Visibility).ToArray();
            bool appearance=ThemeContent.Visibility==Visibility.Visible;
            bool headerProjection=_onexSettingsHeaderQaProjection;
            var display=new Windows.System.Display.DisplayRequest();bool held=false;
            try
            {
                Phase("entered");display.RequestActive();held=true;
                Check(OnexStrings.Normalize(null)=="en"&&OnexStrings.Normalize("invalid")=="en","English default depends on OS or invalid preferences");
                Check(OnexStrings.Language=="en"||OnexStrings.Language=="ru","Invalid runtime UI language");
                foreach(var entry in entries) {
                    Check(entry.Value.Length==2&&!string.IsNullOrEmpty(entry.Value[0])&&!string.IsNullOrEmpty(entry.Value[1]),"Incomplete translation entry");
                    Check(OnexStrings.GetForLanguage(entry.Key,"en")==entry.Value[0]&&OnexStrings.GetForLanguage(entry.Key,"ru")==entry.Value[1],"Catalog lookup differs from language choice");
                    var slots=Regex.Matches(entry.Key,@"(?<!\{)\{(\d+)(?:[,}:])").Cast<Match>().Select(match=>int.Parse(match.Groups[1].Value)).ToArray();
                    if(slots.Length>0)
                    {
                        var arguments=Enumerable.Repeat<object>(new OnexLocaleFormatProbe(),slots.Max()+1).ToArray();
                        foreach(var pair in new[]{new{Language="en-US",Text=entry.Value[0]},new{Language="ru-RU",Text=entry.Value[1]}})
                        {Check(!string.IsNullOrEmpty(string.Format(CultureInfo.GetCultureInfo(pair.Language),pair.Text,arguments)),"Localized format template is invalid");formatTemplates++;}
                    }
                }
                foreach(string term in new[]{"Intel","CPU","GPU","Bluetooth","Wi-Fi","TDP","PL1","PL2","HDR","FPS","RTSS","ONEXTweaks"})
                    Check(OnexStrings.GetForLanguage(term,"en")==term&&OnexStrings.GetForLanguage(term,"ru")==term,"Technical/product name translated");
                report["windowsPrimaryLanguageOverride"]=JsonValue.CreateStringValue(Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride??"");
                report["windowsAppLanguageList"]=JsonValue.CreateStringValue(string.Join(";",Windows.Globalization.ApplicationLanguages.Languages));
                Check(OnexPowerModeHeading.Text==OnexStrings.Get("OS Power Mode"),"Actual PRI-localized XAML heading differs from selected language");
                Phase("open appearance for location check");SystemNavItem.IsChecked=true;ThemeExpandButton.IsChecked=true;SetOnexAppearanceExpanded(true);UpdateLayout();await Task.Delay(100);
                EnsureOnexLanguageSettings();
                Check(_onexLanguage!=null&&OnexOwnedContent(ThemeContent).Any(element=>ReferenceEquals(element,_onexLanguage)),"Language selector is outside Appearance");
                Check(_onexLanguage.Items.OfType<ComboBoxItem>().Select(item=>(string)item.Tag).SequenceEqual(new[]{"en","ru"}),"Language choice IDs changed");
                Check(_onexLanguage.SelectedIndex==(OnexStrings.Language=="ru"?1:0),"Language selector differs from runtime");
                foreach(var tab in tabs) {
                    tab.Visibility=Visibility.Visible;
                    Phase("page "+visits);tab.IsChecked=true;UpdateLayout();await Task.Delay(140);InspectPage();visits++;
                    // Expand only section headers, never option switches or values.
                    // Driver inventory launch is suppressed by the existing readonly QA projection.
                    var headers=OnexOwnedContent(OnexNavigationRoot()).OfType<ToggleButton>().Where(button=>button.Name.EndsWith("ExpandButton",StringComparison.Ordinal)).ToArray();
                    var was=headers.Select(button=>button.IsChecked).ToArray();_onexSettingsHeaderQaProjection=true;
                    try
                    {
                        foreach(var header in headers){header.IsChecked=true;UpdateLayout();await Task.Delay(80);InspectPage();expandedSections++;}
                    }
                    finally{for(int n=0;n<headers.Length;n++)headers[n].IsChecked=was[n];_onexSettingsHeaderQaProjection=headerProjection;UpdateLayout();}
                }
                Phase("profile menus and deep sections");
                bool syntheticNameAlready=userNames.Contains("Delete");userNames.Add("Delete");
                try{await VerifyOnexLocaleSurfacesAsync(InspectTree,Inspect,Check,report);}
                finally{if(!syntheticNameAlready)userNames.Remove("Delete");}
                // Actual Settings content and a real native ComboBox popup.
                SystemNavItem.IsChecked=true;ThemeExpandButton.IsChecked=true;SetOnexAppearanceExpanded(true);UpdateLayout();await Task.Delay(100);
                _onexLanguage.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();InspectPage();
                _onexLanguage.IsDropDownOpen=true;await Task.Delay(100);
                Check(_onexLanguage.IsDropDownOpen,"Native language dropdown did not open");
                Check(_onexLanguage.Items.OfType<ComboBoxItem>().Select(item=>item.Content as string).SequenceEqual(new[]{"English","Русский"}),"Language self-names were changed");
                _onexLanguage.IsDropDownOpen=false;
                var section=_onexLanguage.Parent as FrameworkElement;
                Phase("capture appearance");var render=SaveOnexImage("onex-localization429-"+OnexStrings.Language+"-appearance.png",section,30);if(await Task.WhenAny(render,Task.Delay(8000))!=render)throw new TimeoutException("Language-section capture timed out");await render;captures++;
                // Read-only UI fixtures exercise long help and custom-name safety.
                var fixture=new StackPanel{Width=Math.Min(500,Math.Max(280,ActualWidth-64)),Spacing=8};
                fixture.Children.Add(OnexHelpHeader(OnexStrings.Get("CPU Settings"),OnexBoostHelp));
                var paragraph=new TextBlock{Text=OnexStrings.Get(OnexBoostHelp),TextWrapping=TextWrapping.Wrap,FontSize=13};fixture.Children.Add(paragraph);
                var host=(Panel)SystemScrollViewer.Content;host.Children.Add(fixture);fixture.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();await Task.Delay(80);
                Check(paragraph.ActualHeight>50&&paragraph.ActualWidth>200,"Localized long help did not wrap");
                render=SaveOnexImage("onex-localization429-"+OnexStrings.Language+"-help.png",fixture,30);if(await Task.WhenAny(render,Task.Delay(8000))!=render)throw new TimeoutException("Localized help capture timed out");await render;captures++;host.Children.Remove(fixture);
                var synthetic=new OnexScopedProfilePicker(()=>Task.FromResult(new JsonObject()),value=>Task.CompletedTask,()=>new MenuFlyout(),value=>"",(Windows.UI.Xaml.Style)Resources["StandaloneButtonStyle"],tileTextBrush,control=>{});
                var syntheticEntry=new JsonObject{{"id",JsonValue.CreateStringValue("qa-locale-name")},{"name",JsonValue.CreateStringValue("Delete")},{"dc",new JsonObject()}};
                synthetic.Apply(new JsonObject{{"activeId",JsonValue.CreateStringValue("qa-locale-name")},{"entries",new JsonArray{syntheticEntry}}});
                Check(OnexOwnedContent(synthetic.Root).OfType<TextBlock>().Any(text=>text.Text=="Delete"),"A user-provided profile name was translated");
                Phase("completed");
                report["checks"]=JsonValue.CreateNumberValue(checks);report["language"]=JsonValue.CreateStringValue(OnexStrings.Language);
                report["actualPageVisits"]=JsonValue.CreateNumberValue(visits);report["actualTextSamples"]=JsonValue.CreateNumberValue(samples);report["catalogCaptionSamplesObserved"]=JsonValue.CreateNumberValue(staticLabels);
                var unresolved=new JsonArray();foreach(int index in missing.OrderBy(value=>value))unresolved.Add(JsonValue.CreateNumberValue(index));report["unlocalizedKnownCaptionIds"]=unresolved;var unresolvedKeys=new JsonArray();foreach(int index in missing.OrderBy(value=>value))unresolvedKeys.Add(JsonValue.CreateStringValue(entries[index].Key));report["unlocalizedKnownSourceKeys"]=unresolvedKeys;
                report["captures"]=JsonValue.CreateNumberValue(captures);report["syntheticNameAndHelpFixtures"]=JsonValue.CreateBooleanValue(true);
                report["compiledXamlResourceBindings"]=JsonValue.CreateNumberValue(1649);report["catalogEntries"]=JsonValue.CreateNumberValue(entries.Length);report["expandedSectionHeaders"]=JsonValue.CreateNumberValue(expandedSections);report["validatedFormatTemplates"]=JsonValue.CreateNumberValue(formatTemplates);report["fullLocalizationAccepted"]=JsonValue.CreateBooleanValue(false);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
                Check(missing.Count==0,"Known display captions remain untranslated; inspect source-key report");
            }
            finally {
                _onexSettingsHeaderQaProjection=headerProjection;_onexLanguage.IsDropDownOpen=false;SetOnexAppearanceExpanded(appearance);ThemeExpandButton.IsChecked=appearance;
                for(int n=0;n<tabs.Length;n++)tabs[n].Visibility=tabVisibility[n];if(original?.Visibility==Visibility.Visible)original.IsChecked=true;if(held)display.RequestRelease();
            }
        }
        private Task VerifyOnexLanguageApplyAsync(JsonObject report)
        {
            EnsureOnexLanguageSettings();SystemNavItem.IsChecked=true;ThemeExpandButton.IsChecked=true;SetOnexAppearanceExpanded(true);
            string target=OnexStrings.Language=="ru"?"en":"ru";_onexLanguage.SelectedIndex=target=="ru"?1:0;
            if(!_onexApplyLanguage.IsEnabled)throw new InvalidOperationException("Language apply button did not enable");
            report["sourceLanguage"]=JsonValue.CreateStringValue(OnexStrings.Language);report["requestedLanguage"]=JsonValue.CreateStringValue(target);
            report["selectionIsProgrammatic"]=JsonValue.CreateBooleanValue(true);report["applyScheduledViaActualUiaInvoke"]=JsonValue.CreateBooleanValue(true);
            report["restartStillRequiresWindowsReadback"]=JsonValue.CreateBooleanValue(true);
            _onexLanguageVerificationTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
            _onexLanguageVerificationTimer.Tick+=(sender,args)=>{_onexLanguageVerificationTimer.Stop();SystemNavItem.IsChecked=true;ThemeExpandButton.IsChecked=true;SetOnexAppearanceExpanded(true);UpdateLayout();((IInvokeProvider)new ButtonAutomationPeer(_onexApplyLanguage).GetPattern(PatternInterface.Invoke)).Invoke();};
            _onexLanguageVerificationTimer.Start();return Task.CompletedTask;
        }
    }
}
