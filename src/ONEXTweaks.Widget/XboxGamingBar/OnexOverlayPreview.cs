// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private bool _onexOverlayEditorInitialized,_onexOverlayConfigReady,_onexOverlayPreviewBusy;
        private int _onexOverlayPreviewRevision;
        private DispatcherTimer _onexOverlayPreviewDelay;
        private Border _onexOverlayPreviewSurface,_onexOverlayPreviewCard;
        private RichTextBlock _onexOverlayPreviewText;
        private TextBlock _onexOverlayPreviewStatus,_onexOverlayPreviewNotice;
        private JsonObject _onexOverlayPreviewModel;
        private string _onexOverlayPreviewRenderedConfig;
        private string _onexOverlayPreviewVisualKey,_onexOverlayPreviewVisualLanguage;
        private double _onexOverlayPreviewVisualDpi;
        private int _onexOverlayPreviewBuilds,_onexOverlayPreviewReuses;
        private readonly Dictionary<string,string> _onexOverlayExtraConfig=new Dictionary<string,string>();
        private static readonly string[] OnexOverlayFriendlyNames={"Off","Basic","Horizontal","Horizontal Custom","Vertical Custom"};
        private static readonly Dictionary<string,string> OnexOverlayMetricNames=new Dictionary<string,string>
        {
            {"AppName",global::XboxGamingBar.OnexStrings.Get("Графический API")},{"Time","Часы"},{"FPS",global::XboxGamingBar.OnexStrings.Get("FPS и время кадра")},{"Battery",global::XboxGamingBar.OnexStrings.Get("Заряд и питание")},
            {"Memory",global::XboxGamingBar.OnexStrings.Get("Оперативная память")},{"VRAM",global::XboxGamingBar.OnexStrings.Get("Память графики")},{"CPU",global::XboxGamingBar.OnexStrings.Get("Загрузка и температура CPU")},
            {"CPUClock",global::XboxGamingBar.OnexStrings.Get("Средняя частота CPU")},{"CPUCores",global::XboxGamingBar.OnexStrings.Get("Частоты по ядрам")},{"GPU",global::XboxGamingBar.OnexStrings.Get("Нагрузка, мощность и температура GPU")},
            {"GPUClock",global::XboxGamingBar.OnexStrings.Get("Частота GPU")},{"TDPLimits",global::XboxGamingBar.OnexStrings.Get("Мощность APU и лимиты")},{"Fan",global::XboxGamingBar.OnexStrings.Get("Скорость вентилятора")},
            {"FrametimeGraph",global::XboxGamingBar.OnexStrings.Get("График времени кадра")},{"FpsStats",global::XboxGamingBar.OnexStrings.Get("Статистика FPS и 1% low")}
        };
        internal static string OnexOverlayItemHelp(string id)
        {
            switch(id)
            {
                case "AppName":return global::XboxGamingBar.OnexStrings.Get("Показывает графический API игры, например D3D11 или Vulkan. Это не название исполняемого файла.");
                case "Time":return global::XboxGamingBar.OnexStrings.Get("Текущее время Windows. Например, 18:42.");
                case "FPS":return global::XboxGamingBar.OnexStrings.Get("FPS — сколько кадров игра выдаёт за секунду. Время кадра показывает, сколько длится один кадр: 60 FPS — примерно 16,7 мс. Меньше мс и ровнее значения — плавнее движение.");
                case "Battery":return global::XboxGamingBar.OnexStrings.Get("Заряд батареи в процентах; AC означает питание от зарядного устройства. Например, BAT 80% AC.");
                case "Memory":return global::XboxGamingBar.OnexStrings.Get("Используемая и общая оперативная память. Например, RAM 10.5 GB / 32.0 GB.");
                case "VRAM":return global::XboxGamingBar.OnexStrings.Get("Используемая графическая память. Встроенная графика использует в том числе общую оперативную память.");
                case "CPU":return global::XboxGamingBar.OnexStrings.Get("Общая загрузка процессора и его температура. Например, CPU 28% 55 C. Это не мощность в ваттах.");
                case "CPUClock":return global::XboxGamingBar.OnexStrings.Get("Средняя частота по показаниям ядер. Она может быть ниже максимальной частоты самого быстрого ядра: часть ядер экономит энергию.");
                case "CPUCores":return global::XboxGamingBar.OnexStrings.Get("Частоты отдельных ядер в ГГц. Например, 2.4 / 2.6 / 2.8 — так видно, как нагрузка распределяется между ядрами.");
                case "GPU":return global::XboxGamingBar.OnexStrings.Get("Загрузка графики, её фактическая мощность и температура. Недоступный датчик обозначается прочерком.");
                case "GPUClock":return global::XboxGamingBar.OnexStrings.Get("Текущая частота графики в МГц. Например, GPU CLOCK 1250 MHz.");
                case "TDPLimits":return global::XboxGamingBar.OnexStrings.Get("APU — фактическое потребление общего пакета CPU/GPU. PL1 и PL2 — заданные лимиты. Например, при PL1 15 W пакет может потреблять 8 W, если игре не требуется больше.");
                case "Fan":return global::XboxGamingBar.OnexStrings.Get("Скорость вентилятора в процентах по доступному датчику. Если получить её нельзя, отображается прочерк.");
                case "FrametimeGraph":return global::XboxGamingBar.OnexStrings.Get("Показывает, насколько равномерно игра выдаёт кадры. Ровная линия — равномерные кадры; высокие пики — задержки. Сам FPS может оставаться высоким.");
                case "FpsStats":return global::XboxGamingBar.OnexStrings.Get("Минимальный, средний и максимальный FPS, а также 1% low — результат для самых медленных 1% недавних кадров. Появляются, когда RTSS накопит данные.");
                default:return id!=null&&id.StartsWith("Blank")?global::XboxGamingBar.OnexStrings.Get("В своих строках это перенос на следующую строку. В своей колонке — пустая строка между группами. Можно перемещать стрелками вверх/вниз."):global::XboxGamingBar.OnexStrings.Get("Показатель выбранного макета.");
            }
        }
        private static string OnexOverlayMetricName(string id,int level)
        {
            if(id.StartsWith("Blank"))return (level==4?global::XboxGamingBar.OnexStrings.Get("Пустая строка "):global::XboxGamingBar.OnexStrings.Get("↵ Перенос строки "))+id.Substring(5);
            return OnexOverlayMetricNames.TryGetValue(id,out string name)?name:id;
        }
        private IPC.NamedPipeClient _onexOverlayOpeningOwner;
        private bool _onexOverlayOpening => Volatile.Read(ref _onexOverlayOpeningOwner) != null;
        private async Task OpenOnexOverlaySettingsAsync()
        {
            var owner = App.PipeClient;
            bool opened = false;
            try
            {
                if (!TryRunForOnexConnection(owner, () =>
                {
                    if (ReferenceEquals(_onexOverlayOpeningOwner, owner)) return;
                    Volatile.Write(ref _onexOverlayOpeningOwner, owner);
                    opened = true;
                    EnsureOnexOverlayEditor();
                }) || !opened) return;

                await ReadOnexOverlayConfigurationAsync(owner);
                TryRunForOnexConnection(owner, () =>
                {
                    if (_releaseOsdNav?.IsChecked != true) return;
                    if (!isOSDCustomizeExpanded) OSDCustomizeExpandButton_Click(OSDCustomizeExpandButton, null);
                    SyncOsdCustomizeLevelToActiveOverlay();
                    QueueOnexOverlayPreview();
                });
            }
            catch (Exception error)
            {
                TryRunForOnexConnection(owner, () =>
                    _onexOverlayCaption.Text = global::XboxGamingBar.OnexStrings.Format("Настройки оверлея недоступны: {0}", error.Message));
            }
            finally
            {
                if (opened) Interlocked.CompareExchange(ref _onexOverlayOpeningOwner, null, owner);
            }
        }
        private void EnsureOnexOverlayEditor()
        {
            if(_onexOverlayEditorInitialized||OSDCustomizeContent==null||_releaseOsdNav==null)return;
            _onexOverlayEditorInitialized=true;
            OSDRendererPickerRow.Visibility=Visibility.Collapsed;
            OsdRendererCaption.Text=global::XboxGamingBar.OnexStrings.Get("RTSS показывает оверлей поверх игры. Оформление общее для макетов; режим, который включён в игре, выбирается сверху.");
            var labels=new Dictionary<string,string>
            {
                {"Renderer",global::XboxGamingBar.OnexStrings.Get("Оверлей в игре")},{"Layout",global::XboxGamingBar.OnexStrings.Get("Размер и шрифт")},{"Colors",global::XboxGamingBar.OnexStrings.Get("Цвета")},{"Overlay Size",global::XboxGamingBar.OnexStrings.Get("Размер оверлея")},{"Overlay Font",global::XboxGamingBar.OnexStrings.Get("Шрифт")},
                {"Text Color",global::XboxGamingBar.OnexStrings.Get("Цвет значений")},{"Label Color",global::XboxGamingBar.OnexStrings.Get("Цвет подписей")},{"OSD Opacity",global::XboxGamingBar.OnexStrings.Get("Яркость текста")},
                {"Reduce OSD brightness to protect OLED pixels",global::XboxGamingBar.OnexStrings.Get("100% — исходные цвета. При 50% текст темнее; фон настраивается отдельно.")},
                {"Background",global::XboxGamingBar.OnexStrings.Get("Подложка")},{"Adds a dark backdrop behind the overlay for better contrast",global::XboxGamingBar.OnexStrings.Get("Тёмный фон под текстом помогает читать показатели на светлых сценах.")},
                {"Background Opacity",global::XboxGamingBar.OnexStrings.Get("Непрозрачность подложки")},{"Items to Display",global::XboxGamingBar.OnexStrings.Get("Какие показатели показывать")},
                {"Pick which of the two custom presets to edit — the checkboxes below show what that preset displays, in its own order. Basic and Horizontal are fixed ONEXTweaks presets and are not editable.",global::XboxGamingBar.OnexStrings.Get("В своих макетах включайте показатели и меняйте порядок стрелками. Кнопка ? объясняет показатель с примером; на геймпаде используйте X.")},
                {"Custom RTSS Tags",global::XboxGamingBar.OnexStrings.Get("Дополнительно: команды RTSS")},{"Add custom RTSS tags (e.g. <FR> for FPS)",global::XboxGamingBar.OnexStrings.Get("Необязательно. Дополнительный текст и команды RTSS; сложные команды могут отличаться в игре.")}
            };
            void Relabel(DependencyObject parent)
            {
                foreach(var child in ThemeContentChildren(parent))
                {
                    if(child is TextBlock text&&labels.TryGetValue(text.Text,out string translated))text.Text=translated;
                    Relabel(child);
                }
            }
            Relabel(OSDCustomizeContent);
            for(int i=0;i<PerformanceOverlayComboBox.Items.Count&&i<OnexOverlayFriendlyNames.Length;i++)if(PerformanceOverlayComboBox.Items[i] is ComboBoxItem mode)mode.Content=OnexOverlayFriendlyNames[i];
            foreach(var item in OSDTextSizeComboBox.Items.OfType<ComboBoxItem>())if(item.Tag is string percentage)item.Content=percentage+"%";
            OSDTextColorDynamicCheckBox.Content=global::XboxGamingBar.OnexStrings.Get("Цвет по показателю");OSDLabelColorDefaultCheckBox.Content=global::XboxGamingBar.OnexStrings.Get("Стандартные");
            OSDOpacityValue.Foreground=OnexDialogAccent;OSDBackgroundOpacityValue.Foreground=OnexDialogAccent;
            ToolTipService.SetToolTip(OSDTextColorDynamicCheckBox,global::XboxGamingBar.OnexStrings.Get("CPU — голубой, GPU — фиолетовый, батарея — зелёная. Это цвета типов показателей, а не шкала температуры."));
            ToolTipService.SetToolTip(OSDLabelColorDefaultCheckBox,global::XboxGamingBar.OnexStrings.Get("Подписи — названия CPU, GPU, RAM. Стандартные используют цвета типов показателей. Отдельный цвет меняет только подписи; значения настраиваются выше."));
            AddOnexOverlayHelpRow(OSDTextSizeRow,global::XboxGamingBar.OnexStrings.Get("Меняет размер текста и графика во всех макетах. Например, 200% крупнее 150%. В игре также действует сохранённый масштаб RTSS."));
            AddOnexOverlayHelpRow(OSDFontRow,global::XboxGamingBar.OnexStrings.Get("Выбирает шрифт игрового оверлея. Это не шрифт самого виджета."));
            // Parent can be null for collapsed, not-yet-loaded XAML content. Its named owner is reliable.
            OSDCustomizePresetRow.Children.Remove(OSDCustomizeLevelComboBox);
            OSDCustomizePresetRow.Visibility=Visibility.Collapsed;
            OSDCustomizeLevelComboBox.Items.Insert(0,new ComboBoxItem{Content=OnexOverlayFriendlyNames[2],Tag="2"});
            OSDCustomizeLevelComboBox.Items.Insert(0,new ComboBoxItem{Content=OnexOverlayFriendlyNames[1],Tag="1"});
            foreach(var item in OSDCustomizeLevelComboBox.Items.OfType<ComboBoxItem>())if(item.Tag is string tag&&int.TryParse(tag,out int level)&&level>=1&&level<=4)item.Content=OnexOverlayFriendlyNames[level];
            OSDCustomizeLevelComboBox.Width=200;Grid.SetColumn(OSDCustomizeLevelComboBox,1);
            var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            header.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Предпросмотр"),FontSize=18,VerticalAlignment=VerticalAlignment.Center});header.Children.Add(OSDCustomizeLevelComboBox);
            _onexOverlayPreviewStatus=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Ожидание настроек…"),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush};
            _onexOverlayPreviewNotice=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush,Visibility=Visibility.Collapsed};
            _onexOverlayPreviewText=new RichTextBlock{TextWrapping=TextWrapping.NoWrap,IsTextSelectionEnabled=false};
            _onexOverlayPreviewSurface=new Border{Padding=new Thickness(6),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Child=_onexOverlayPreviewText};
            var scroll=new ScrollViewer{Content=_onexOverlayPreviewSurface,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollMode=ScrollMode.Enabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=240,MinHeight=100,Background=OnexSettingsBrush(48,60,72),IsTabStop=false};
            var panel=new StackPanel{Spacing=8};panel.Children.Add(header);panel.Children.Add(_onexOverlayPreviewStatus);panel.Children.Add(scroll);panel.Children.Add(_onexOverlayPreviewNotice);
            _onexOverlayPreviewCard=new Border{Background=OnexDialogSurface,BorderBrush=OnexDialogBorder,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(10),Margin=new Thickness(0,0,0,12),Child=panel};
            OSDCustomizeContent.Children.Insert(0,_onexOverlayPreviewCard);
            _releaseOsdNav.Checked+=(s,e)=>QueueOnexOverlayPreview();
            QueueOnexOverlayPreview();
        }
        private void AddOnexOverlayHelpRow(Grid row,string explanation)
        {
            var parent=RTSSOptionsPanel;
            int index=parent.Children.IndexOf(row);parent.Children.RemoveAt(index);var wrapper=new StackPanel{Margin=new Thickness(0,0,0,8)};
            wrapper.Children.Add(row);wrapper.Children.Add(new TextBlock{Text=explanation,FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush});parent.Children.Insert(index,wrapper);
        }
        private bool OnexOverlayControlVisible(FrameworkElement element)
        {
            // This navigation list owns eight known controls. Collapsed XAML descendants may have no Parent yet.
            if(element==null||element.Visibility!=Visibility.Visible||_releaseOsd.Visibility!=Visibility.Visible||!isOSDCustomizeExpanded)return false;
            return !ReferenceEquals(element,OSDBackgroundOpacitySlider)||OSDBackgroundOpacityPanel.Visibility==Visibility.Visible;
        }
        private void UpdateOnexOverlayPresetUi()
        {
            if(!OnexFrontend.UsesOwnBackend)return;
            bool editable=osdCustomizeLevel>=3;OSDItemsControl.IsEnabled=editable;OSDCustomTagsTextBox.IsEnabled=editable;
            UpdateOsdCustomizeLevelHint();QueueOnexOverlayPreview();
        }
        private async void OnexOverlayHelp_Click(object sender,RoutedEventArgs e)
        {
            if(sender is FrameworkElement element&&element.DataContext is OSDItemViewModel item)await ShowOnexOverlayItemHelpAsync(item);
        }
        private async Task ShowOnexOverlayItemHelpAsync(OSDItemViewModel item)
        {
            if(_onexDetailsDialog!=null)return;
            var content=new TextBlock{Text=item.Description,TextWrapping=TextWrapping.Wrap,FontSize=15};
            var dialog=CreateOnexSettingsDialog(item.DisplayName,global::XboxGamingBar.OnexStrings.Get("Что означает показатель"),content);dialog.PrimaryButtonText="";dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Закрыть");dialog.DefaultButton=ContentDialogButton.None;
            _onexDetailsDialog=dialog;try{await dialog.ShowAsync();}finally{if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;}
        }
        private sealed class OnexOverlayConfigurationRead
        {
            internal readonly IPC.NamedPipeClient Owner;
            internal readonly TaskCompletionSource<bool> Completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal OnexOverlayConfigurationRead(IPC.NamedPipeClient owner) => Owner = owner;
        }

        private OnexOverlayConfigurationRead _onexOverlayConfigurationRead;
        private int _onexOverlayConfigurationFlushes;

        private Task ReadOnexOverlayConfigurationAsync() => ReadOnexOverlayConfigurationAsync(App.PipeClient);

        private Task ReadOnexOverlayConfigurationAsync(IPC.NamedPipeClient owner)
        {
            Task result = Task.CompletedTask;
            TryRunForOnexConnection(owner, () =>
            {
                var current = Volatile.Read(ref _onexOverlayConfigurationRead);
                if (current != null && ReferenceEquals(current.Owner, owner) && !current.Completion.Task.IsCompleted)
                {
                    result = current.Completion.Task;
                    return;
                }

                var read = new OnexOverlayConfigurationRead(owner);
                Volatile.Write(ref _onexOverlayConfigurationRead, read);
                result = read.Completion.Task;
                _ = CompleteOnexOverlayConfigurationReadAsync(read);
            });
            return result;
        }

        private async Task CompleteOnexOverlayConfigurationReadAsync(OnexOverlayConfigurationRead read)
        {
            try
            {
                await ReadOnexOverlayConfigurationCoreAsync(read.Owner);
                read.Completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                read.Completion.TrySetException(error);
            }
            finally
            {
                Interlocked.CompareExchange(ref _onexOverlayConfigurationRead, null, read);
            }
        }

        private async Task ReadOnexOverlayConfigurationCoreAsync(IPC.NamedPipeClient owner)
        {
            _onexOverlayConfigReady = false;
            bool flushing = false;
            try
            {
                if (!await TryReadOnexConnectionStateAsync(owner, "OnexOverlayGet", reply =>
                {
                    if(reply.TryGetValue("OnexOverlayState",out object state))ApplyOnexOverlayState((string)state);
                    if(reply.TryGetValue("OnexOverlayPresetsState",out object catalog))ApplyOnexOverlayPresetsState((string)catalog);
                    if(!reply.TryGetValue("OnexOverlayPreferences",out object raw))return;
                    var prefs=JsonObject.Parse((string)raw);var map=new Dictionary<string,string>(StringComparer.Ordinal);
                    if(reply.TryGetValue("OnexOverlayEditorDefaults",out object defaults))foreach(string part in ((string)defaults).Split(';')){int colon=part.IndexOf(':');if(colon>0)map[part.Substring(0,colon)]=part.Substring(colon+1);}
                    foreach(string part in prefs.GetNamedString("config","").Split(';')){int colon=part.IndexOf(':');if(colon>0)map[part.Substring(0,colon)]=part.Substring(colon+1);}
                    // Each pending Low turn holds its own contribution to the shared input guard.
                    Interlocked.Increment(ref _onexOverlayConfigurationFlushes);
                    flushing = true;
                    int GetInt(string key,int fallback,int min,int max)=>map.TryGetValue(key,out string value)&&int.TryParse(value,out int number)?Math.Max(min,Math.Min(max,number)):fallback;
                    osdTextSize=GetInt("TextSize",osdTextSize,50,300);osdOpacity=GetInt("Opacity",osdOpacity,10,100);
                    if(map.TryGetValue("Font",out string font)){var pieces=font.Split('|');osdFont=pieces[0];if(pieces.Length>1&&int.TryParse(pieces[1],out int weight))osdFontWeight=weight;}
                    if(map.TryGetValue("TextColor",out string text))osdTextColor=text;if(map.TryGetValue("LabelColor",out string label))osdLabelColor=label;
                    if(map.TryGetValue("FrametimeGraphPinned",out string pinned))frametimeGraphPinned=pinned=="1";
                    if(map.TryGetValue("Background",out string background)){var pieces=background.Split('|');osdBackgroundEnabled=pieces[0]=="1";if(pieces.Length>1&&int.TryParse(pieces[1],out int opacity))osdBackgroundOpacity=Math.Max(10,Math.Min(100,opacity));}
                    var known=new HashSet<string>{"TextSize","Font","TextColor","LabelColor","Opacity","Background","FrametimeGraphPinned"};
                    for(int level=1;level<=4;level++)
                    {
                        osdItemLabelColors[level].Clear();
                        string prefix="L"+level;known.Add(prefix);known.Add(prefix+"_Columns");known.Add(prefix+"_Order");known.Add(prefix+"_Custom");
                        if(map.TryGetValue(prefix,out string enabled)){var selected=new HashSet<string>(enabled.Split(','));foreach(string id in osdLevelConfig[level].Keys.ToArray())osdLevelConfig[level][id]=selected.Contains(id);}
                        if(map.TryGetValue(prefix+"_Order",out string order))osdLevelOrder[level]=order.Split(',').Where(id=>osdItemDisplayNames.ContainsKey(id)).Concat(osdLevelOrder[level]).Distinct().ToList();
                        osdCustomTags[level]=map.TryGetValue(prefix+"_Custom",out string custom)?custom:"";
                        osdLevelColumns[level]=GetInt(prefix+"_Columns",osdLevelColumns[level],1,10);
                        foreach(string id in osdItemDisplayNames.Keys){string key=prefix+"_"+id+"_Color";known.Add(key);if(map.TryGetValue(key,out string color))osdItemLabelColors[level][id]=color;}
                    }
                    _onexOverlayExtraConfig.Clear();foreach(var pair in map.Where(p=>!known.Contains(p.Key)))_onexOverlayExtraConfig[pair.Key]=pair.Value;
                    string fontTag=osdFont+"|"+osdFontWeight;
                    if(!OSDFontComboBox.Items.OfType<ComboBoxItem>().Any(item=>item.Tag as string==fontTag))OSDFontComboBox.Items.Add(new ComboBoxItem{Content=osdFont+global::XboxGamingBar.OnexStrings.Get(" (текущий)"),Tag=fontTag});
                    if(!OSDTextSizeComboBox.Items.OfType<ComboBoxItem>().Any(item=>item.Tag as string==osdTextSize.ToString()))OSDTextSizeComboBox.Items.Add(new ComboBoxItem{Content=osdTextSize+"%",Tag=osdTextSize.ToString()});
                    UpdateOSDLayoutUI();LoadOSDOptionsForLevel(osdCustomizeLevel);
                    OSDOpacitySlider.Value=osdOpacity;OSDOpacityValue.Text=osdOpacity+"%";OSDBackgroundToggle.IsOn=osdBackgroundEnabled;OSDBackgroundOpacitySlider.Value=osdBackgroundOpacity;OSDBackgroundOpacityValue.Text=osdBackgroundOpacity+"%";
                    OSDBackgroundOpacityPanel.Visibility=osdBackgroundEnabled?Visibility.Visible:Visibility.Collapsed;
                }) || !flushing) return;

                // Keep bindings muted through the Low dispatcher turn.
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => { });
            }
            finally
            {
                if (flushing) Interlocked.Decrement(ref _onexOverlayConfigurationFlushes);
            }

            TryRunForOnexConnection(owner, () =>
            {
                _onexOverlayConfigReady = true;
                UpdateOnexOverlayPresetUi();
            });
        }
        private void QueueOnexOverlayPreview()
        {
            if(!OnexFrontend.UsesOwnBackend||!_onexOverlayEditorInitialized||!_onexOverlayConfigReady)return;
            _onexOverlayPreviewRevision++;
            if(_releaseOsd?.Visibility!=Visibility.Visible||!isOSDCustomizeExpanded||widget!=null&&!widget.Visible)return;
            if(_onexOverlayPreviewDelay==null){_onexOverlayPreviewDelay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};_onexOverlayPreviewDelay.Tick+=async(s,e)=>{_onexOverlayPreviewDelay.Stop();await RefreshOnexOverlayPreviewAsync();};}
            _onexOverlayPreviewDelay.Stop();_onexOverlayPreviewDelay.Start();
        }
        private async Task RefreshOnexOverlayPreviewAsync()
        {
            if(_onexOverlayPreviewBusy||!App.IsConnected||!_onexOverlayConfigReady||_releaseOsd.Visibility!=Visibility.Visible||!isOSDCustomizeExpanded||widget!=null&&!widget.Visible)return;
            _onexOverlayPreviewBusy=true;
            try
            {
                int revision;
                do
                {
                    revision=_onexOverlayPreviewRevision;
                    string draft=BuildOSDConfigString(true);
                    var reply=await App.SendMessageAsync(new ValueSet{{"OnexOverlayPreviewGet",osdCustomizeLevel},{"OnexOverlayPreviewConfig",draft}});
                    if(reply.TryGetValue("Error",out object error))throw new InvalidOperationException(error.ToString());
                    if(revision==_onexOverlayPreviewRevision&&_releaseOsd.Visibility==Visibility.Visible&&(widget==null||widget.Visible)&&reply.TryGetValue("OnexOverlayPreview",out object preview)){RenderOnexOverlayPreview(JsonObject.Parse((string)preview));_onexOverlayPreviewRenderedConfig=draft;}
                }while(revision!=_onexOverlayPreviewRevision&&_releaseOsd.Visibility==Visibility.Visible&&isOSDCustomizeExpanded&&(widget==null||widget.Visible));
            }
            catch(Exception ex){_onexOverlayPreviewStatus.Text=global::XboxGamingBar.OnexStrings.Format("Предпросмотр недоступен: {0}",ex.Message);}
            finally{_onexOverlayPreviewBusy=false;}
        }
        private void RenderOnexOverlayPreview(JsonObject model)
        {
            // Keep the real preview RPC and all metadata refreshes. Only reuse
            // native text/graph objects when the complete returned model, DPI
            // and UI language are identical; any changed model still renders.
            string visualKey=model.Stringify(),language=OnexStrings.Language;
            double dpi=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
            if(_onexOverlayPreviewVisualKey==visualKey&&_onexOverlayPreviewVisualDpi==dpi&&_onexOverlayPreviewVisualLanguage==language&&_onexOverlayPreviewText.Blocks.Count>0)
            {_onexOverlayPreviewModel=model;_onexOverlayPreviewReuses++;return;}
            _onexOverlayPreviewVisualKey=null;
            _onexOverlayPreviewRenderedConfig=null;_onexOverlayPreviewModel=model;_onexOverlayPreviewText.Blocks.Clear();var paragraph=new Paragraph{Margin=new Thickness(0)};
            _onexOverlayPreviewText.Blocks.Add(paragraph);_onexOverlayPreviewText.FontFamily=new FontFamily(model.GetNamedString("fontFace","Bahnschrift"));_onexOverlayPreviewText.FontWeight=new FontWeight{Weight=(ushort)model.GetNamedNumber("fontWeight",400)};
            string baseHex=model.GetNamedString("nativeBaseColor","FFFFFF");var baseColor=Color.FromArgb(255,Convert.ToByte(baseHex.Substring(0,2),16),Convert.ToByte(baseHex.Substring(2,2),16),Convert.ToByte(baseHex.Substring(4,2),16));
            double basis=model.GetNamedNumber("nativeFontHeight",9)*model.GetNamedNumber("nativeZoomRatio",2)/dpi,size=basis;var color=baseColor;bool unsupported=false;
            string markup=model.GetNamedString("resolvedMarkup","");int at=0;
            void Text(string text)
            {
                var lines=text.Split('\n');for(int i=0;i<lines.Length;i++){if(i>0)paragraph.Inlines.Add(new LineBreak());if(lines[i].Length>0)paragraph.Inlines.Add(new Run{Text=lines[i],FontSize=size,Foreground=OnexSolidBrush(color)});}
            }
            foreach(Match match in Regex.Matches(markup,"<[^<>]+>"))
            {
                Text(markup.Substring(at,match.Index-at));string tag=match.Value.Substring(1,match.Length-2);at=match.Index+match.Length;
                if(tag.StartsWith("C=")&&Regex.IsMatch(tag.Substring(2),"^[0-9A-Fa-f]{6}$")){string hex=tag.Substring(2);color=Color.FromArgb(255,Convert.ToByte(hex.Substring(0,2),16),Convert.ToByte(hex.Substring(2,2),16),Convert.ToByte(hex.Substring(4,2),16));}
                else if(tag=="C")color=baseColor;
                else if(tag.StartsWith("S=")&&double.TryParse(tag.Substring(2),out double scale))size=basis*Math.Max(.5,Math.Min(3,scale/100));
                else if(tag=="S")size=basis;
                else if(tag=="OBJ=00000000")paragraph.Inlines.Add(new InlineUIContainer{Child=CreateOnexOverlayPreviewGraph(model,size,color)});
                else unsupported=true;
            }
            Text(markup.Substring(at));
            bool fill=model.GetNamedBoolean("backgroundEnabled",false);int opacity=(int)model.GetNamedNumber("backgroundOpacity",60);
            _onexOverlayPreviewSurface.Background=OnexSolidBrush(Color.FromArgb(fill?(byte)(opacity*255/100):(byte)0,0,0,0));
            _onexOverlayPreviewStatus.Text=global::XboxGamingBar.OnexStrings.Format("Данные для примера · {0}. Оформление обновляется сразу; игровой режим выбирается сверху.",OnexOverlayFriendlyNames[(int)model.GetNamedNumber("level",4)]);
            _onexOverlayPreviewNotice.Text=unsupported?global::XboxGamingBar.OnexStrings.Get("Некоторые дополнительные команды RTSS не поддерживаются предпросмотром. Их результат нужно смотреть в игре."):global::XboxGamingBar.OnexStrings.Get("Можно прокручивать образец, если он не помещается по ширине или высоте.");
            _onexOverlayPreviewNotice.Visibility=Visibility.Visible;
            _onexOverlayPreviewVisualKey=visualKey;_onexOverlayPreviewVisualDpi=dpi;_onexOverlayPreviewVisualLanguage=language;_onexOverlayPreviewBuilds++;
        }
        private Canvas CreateOnexOverlayPreviewGraph(JsonObject model,double fontSize,Color color)
        {
            double width=fontSize*.55*32,height=fontSize*2;var canvas=new Canvas{Width=width,Height=height,Background=OnexSettingsBrush(16,20,24)};
            var graph=new Polyline{Stroke=OnexSolidBrush(color),StrokeThickness=Math.Max(1,fontSize/16)};var samples=model.GetNamedArray("graphMs");
            for(int i=0;i<samples.Count;i++)graph.Points.Add(new Point(width*i/Math.Max(1,samples.Count-1),height*(1-Math.Max(0,Math.Min(1,samples[i].GetNumber()/50)))));
            canvas.Children.Add(graph);return canvas;
        }
    }
}
