// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Data.Json;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage;
using Windows.System.Diagnostics;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    // A separate PRAID/CoreWindow owns the Windows consent dialog. It never creates
    // a Game Bar widget or connects another client to the widget's pipe.
    internal sealed class OnexBluetoothPairingHost : Page
    {
        internal const string Scheme="onextweaks-bluetooth",FolderName="onex-bluetooth-pairing";
        internal static bool IsHostApplication=>AppInfo.Current.Id=="BluetoothPairing";
        private static OnexBluetoothPairingHost current;
        private readonly OnexApprovedTheme palette = OnexApprovedTheme.Current;
        private readonly TextBlock status;
        private readonly Button cancel;
        private StorageFolder folder;
        private string token;
        private bool completed,cancelRequested,suppressReturn,pollBusy;
        private IAsyncOperation<DevicePairingResult> operation;
        private DispatcherTimer timer;

        private OnexBluetoothPairingHost()
        {
            RequestedTheme=palette.Light?ElementTheme.Light:ElementTheme.Dark;
            Background=new SolidColorBrush(palette.Page);
            var panel=new StackPanel{Margin=new Thickness(20),Spacing=14};
            var heading=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};heading.Children.Add(new FontIcon{FontFamily=new FontFamily("Segoe Fluent Icons"),Glyph="\uE702",FontSize=19,Foreground=new SolidColorBrush(palette.Accent(OnexThemeRole.Network))});heading.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Сопряжение Bluetooth"),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold,Foreground=new SolidColorBrush(palette.Text)});panel.Children.Add(heading);
            var progress=new Grid{ColumnSpacing=12};progress.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});progress.ColumnDefinitions.Add(new ColumnDefinition());progress.Children.Add(new ProgressRing{IsActive=true,Width=22,Height=22,Foreground=new SolidColorBrush(palette.Accent(OnexThemeRole.Network)),VerticalAlignment=VerticalAlignment.Center});
            status=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Подготовка сопряжения…"),FontSize=13,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(palette.Muted)};Grid.SetColumn(status,1);progress.Children.Add(status);panel.Children.Add(progress);
            cancel=new Button{Content=global::XboxGamingBar.OnexStrings.Get("Отмена"),HorizontalAlignment=HorizontalAlignment.Right,Width=104,Height=34,MinWidth=0,MinHeight=0,Padding=new Thickness(12,6,12,6),CornerRadius=new CornerRadius(8),Background=new SolidColorBrush(palette.Raised),Foreground=new SolidColorBrush(palette.Text)};
            cancel.Click+=(s,e)=>Cancel();panel.Children.Add(cancel);Content=panel;
            Window.Current.Closed+=(s,e)=>{Cancel();if(token!=null&&!completed)_=CompleteAsync("Canceled",false,global::XboxGamingBar.OnexStrings.Get("Сопряжение отменено"));};
        }
        internal static async void Activate(Uri uri)
        {
            string requestToken=uri?.AbsolutePath.Trim('/');
            if(!IsHostApplication)
            {
                if(Guid.TryParseExact(requestToken,"N",out _))
                {
                    var data=await ApplicationData.Current.LocalFolder.CreateFolderAsync(FolderName,CreationCollisionOption.OpenIfExists);
                    await WriteResultAsync(data,requestToken,"WrongOwner",true,false,global::XboxGamingBar.OnexStrings.Get("Не удалось открыть независимое окно сопряжения"),false);
                }
                return;
            }
            if(current!=null){Window.Current.Activate();return;}
            current=new OnexBluetoothPairingHost();
            ApplicationView.PreferredLaunchViewSize=new Size(420,220);ApplicationView.PreferredLaunchWindowingMode=ApplicationViewWindowingMode.PreferredLaunchViewSize;
            Window.Current.Content=current;Window.Current.Activate();
            var view=ApplicationView.GetForCurrentView();view.Title=global::XboxGamingBar.OnexStrings.Get("ONEXTweaks · Bluetooth");view.SetPreferredMinSize(new Size(320,200));view.TryResizeView(new Size(420,220));
            var palette=current.palette;var surface=palette.Page;view.TitleBar.BackgroundColor=surface;view.TitleBar.InactiveBackgroundColor=surface;view.TitleBar.ButtonBackgroundColor=surface;view.TitleBar.ButtonInactiveBackgroundColor=surface;view.TitleBar.ForegroundColor=palette.Text;view.TitleBar.ButtonForegroundColor=palette.Text;
            if(uri?.Scheme!=Scheme||uri.Host!="pair"||!Guid.TryParseExact(requestToken,"N",out _))
            {current.status.Text=global::XboxGamingBar.OnexStrings.Get("Открой Bluetooth в ONEXTweaks, чтобы выбрать устройство.");current.cancel.Content=global::XboxGamingBar.OnexStrings.Get("Закрыть");current.cancel.Click+=(s,e)=>CoreApplication.Exit();return;}
            current.token=requestToken;await current.RunAsync();
        }
        private void Cancel(){cancelRequested=true;try{operation?.Cancel();}catch{}cancel.IsEnabled=false;status.Text=global::XboxGamingBar.OnexStrings.Get("Отмена сопряжения…");}
        private async Task RunAsync()
        {
            try
            {
                folder=await ApplicationData.Current.LocalFolder.GetFolderAsync(FolderName);
                var requestFile=await folder.GetFileAsync(token+".request.json");
                var request=JsonObject.Parse(await FileIO.ReadTextAsync(requestFile));
                if(request.GetNamedString("token")!=token||!DateTimeOffset.TryParse(request.GetNamedString("createdUtc"),out var created)||DateTimeOffset.UtcNow-created>TimeSpan.FromSeconds(45)||created>DateTimeOffset.UtcNow.AddSeconds(5))throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Запрос сопряжения устарел"));
                await requestFile.RenameAsync(token+".running.json",NameCollisionOption.FailIfExists);
                timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
                timer.Tick+=async(s,e)=>
                {
                    if(pollBusy||completed)return;pollBusy=true;
                    try{var file=await folder.TryGetItemAsync(token+".cancel.json") as StorageFile;if(file!=null){var message=JsonObject.Parse(await FileIO.ReadTextAsync(file));suppressReturn|=message.GetNamedBoolean("suppressReturn",false);Cancel();}}catch{}finally{pollBusy=false;}
                };timer.Start();
                if(request.GetNamedString("mode","pair")=="probe")
                {
                    if(await ApplicationData.Current.LocalFolder.TryGetItemAsync("onex-focus-verification-armed.json")==null)throw new InvalidOperationException("Diagnostic is not armed");
                    status.Text=global::XboxGamingBar.OnexStrings.Get("Проверка независимого окна подтверждения. Сопряжение не запускается.");
                    await Task.Delay(350);await SaveProbeImageAsync();await CompleteAsync("ProbeCompleted",false,null);return;
                }
                string deviceId=request.GetNamedString("deviceId");if(string.IsNullOrWhiteSpace(deviceId)||deviceId.Length>4096)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Некорректное устройство"));
                var device=await DeviceInformation.CreateFromIdAsync(deviceId,new[]{"System.Devices.Aep.IsConnected"},DeviceInformationKind.AssociationEndpoint);
                if(device==null)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Устройство недоступно"));
                if(cancelRequested){await CompleteAsync("Canceled",false,global::XboxGamingBar.OnexStrings.Get("Сопряжение отменено"));return;}
                if(device.Pairing.IsPaired){await CompleteAsync("AlreadyPaired",true,null);return;}
                if(!device.Pairing.CanPair)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Включи режим сопряжения на устройстве и попробуй снова"));
                status.Text=global::XboxGamingBar.OnexStrings.Get("Подтверди сопряжение в системном диалоге Windows.");
                await WriteResultAsync(folder,token,"AwaitingWindows",false,false,null,false);
                operation=device.Pairing.PairAsync(DevicePairingProtectionLevel.Default);
                var result=await operation;bool paired=result.Status==DevicePairingResultStatus.Paired||result.Status==DevicePairingResultStatus.AlreadyPaired;
                await CompleteAsync(result.Status.ToString(),paired,paired?null:global::XboxGamingBar.OnexStrings.Get("Windows не завершила сопряжение: ")+result.Status);
            }
            catch(OperationCanceledException){await CompleteAsync("Canceled",false,global::XboxGamingBar.OnexStrings.Get("Сопряжение отменено"));}
            catch(Exception error){await CompleteAsync("Error",false,error.Message);}
            finally{timer?.Stop();operation=null;await Task.Delay(150);CoreApplication.Exit();}
        }
        private async Task CompleteAsync(string state,bool paired,string error)
        {
            if(completed)return;completed=true;timer?.Stop();
            if(folder==null)folder=await ApplicationData.Current.LocalFolder.CreateFolderAsync(FolderName,CreationCollisionOption.OpenIfExists);
            await WriteResultAsync(folder,token,state,true,paired,error,suppressReturn);
        }
        private static async Task WriteResultAsync(StorageFolder data,string id,string state,bool final,bool paired,string error,bool suppressed)
        {
            var report=new JsonObject{
                ["schema"]=JsonValue.CreateNumberValue(1),["token"]=JsonValue.CreateStringValue(id),
                ["applicationId"]=JsonValue.CreateStringValue(AppInfo.Current.Id),["processId"]=JsonValue.CreateNumberValue(ProcessDiagnosticInfo.GetForCurrentProcess().ProcessId),
                ["status"]=JsonValue.CreateStringValue(state),["isFinal"]=JsonValue.CreateBooleanValue(final),["paired"]=JsonValue.CreateBooleanValue(paired),
                ["suppressReturn"]=JsonValue.CreateBooleanValue(suppressed),["error"]=JsonValue.CreateStringValue(error??""),["utc"]=JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))};
            await FileIO.WriteTextAsync(await data.CreateFileAsync(id+".result.json",CreationCollisionOption.ReplaceExisting),report.Stringify());
        }
        private async Task SaveProbeImageAsync()
        {
            var bitmap=new Windows.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(this);var pixels=await bitmap.GetPixelsAsync();
            var file=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-bluetooth-pairing-host-probe.png",CreationCollisionOption.ReplaceExisting);
            using(var stream=await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
                var bytes=new byte[pixels.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(pixels))reader.ReadBytes(bytes);
                encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,bytes);await encoder.FlushAsync();
            }
        }
    }
}
