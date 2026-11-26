using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private Border _onexDriversCard;private StackPanel _onexDriversBody,_onexDriverRows;private ToggleButton _onexDriversExpand;private FontIcon _onexDriversChevron;
  private Button _onexDriversCheck,_onexDriversRefresh;private TextBlock _onexDriversStatus;private FontIcon _onexDriversIcon;private bool _onexDriversBusy;
  private JsonArray _onexDriverData=new JsonArray();private readonly Dictionary<string,Button> _onexDriverButtons=new Dictionary<string,Button>();
  private void EnsureOnexDriverSettings()
  {
   if(_onexDriversCard!=null||!OnexFrontend.UsesOwnBackend||!(SystemScrollViewer.Content is Panel parent))return;
   var column=new StackPanel{Spacing=0};var header=new Grid{ColumnSpacing=8};
   header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   _onexDriversIcon=new FontIcon{Name="OnexDriversIcon",FontFamily=new Windows.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),Glyph="\uE90F",FontSize=16,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(_onexDriversIcon);
   var titles=new StackPanel{Spacing=0};titles.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Drivers & components"),FontSize=16,FontWeight=Windows.UI.Text.FontWeights.SemiBold});
   titles.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Installed versions and official updates"),FontSize=12,TextWrapping=TextWrapping.Wrap});Grid.SetColumn(titles,1);header.Children.Add(titles);
   _onexDriversExpand=new ToggleButton{Name="OnexDriversExpand",IsTabStop=true,Content=new FontIcon{Glyph="\uE70D",FontSize=12},Padding=new Thickness(8,4,8,4),VerticalAlignment=VerticalAlignment.Center};
   _onexDriversChevron=(FontIcon)_onexDriversExpand.Content;_onexDriversExpand.Content=null;Grid.SetColumn(_onexDriversChevron,2);header.Children.Add(_onexDriversChevron);_onexDriversExpand.Content=header;column.Children.Add(_onexDriversExpand);
   _onexDriversBody=new StackPanel{Spacing=10,Margin=new Thickness(0,12,0,0),Visibility=Visibility.Collapsed};
   _onexDriversBody.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("GitHub checks cover PawnIO, HidHide, usbip-win2 and VIIPER. Intel drivers use Intel Driver & Support Assistant; ONEXPLAYER drivers use the official model catalog. Select a vendor row to continue."),FontSize=12,TextWrapping=TextWrapping.Wrap});
   var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
   _onexDriversCheck=new Button{Name="OnexDriversCheck",Content=global::XboxGamingBar.OnexStrings.Get("Check for updates"),Padding=new Thickness(12,6,12,6)};
   _onexDriversRefresh=new Button{Name="OnexDriversRefresh",Content=global::XboxGamingBar.OnexStrings.Get("Refresh installed"),Padding=new Thickness(12,6,12,6)};
   actions.Children.Add(_onexDriversCheck);actions.Children.Add(_onexDriversRefresh);_onexDriversBody.Children.Add(actions);
   _onexDriversStatus=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Expand this section to read installed components."),FontSize=12,TextWrapping=TextWrapping.Wrap};_onexDriversBody.Children.Add(_onexDriversStatus);
   _onexDriverRows=new StackPanel{Spacing=8};_onexDriversBody.Children.Add(_onexDriverRows);column.Children.Add(_onexDriversBody);
   _onexDriversCard=new Border{Name="OnexDriversCard",Style=(Style)Resources["CardStyle"],Child=column};
   int position=parent.Children.IndexOf(OnexAboutCard);parent.Children.Insert(position<0?parent.Children.Count:position,_onexDriversCard);
   _onexDriversExpand.Checked+=async(s,e)=>{SetOnexDriversExpanded(true);if(!_onexDriversBusy&&!_onexSettingsHeaderQaProjection)await RunOnexDriverOperationAsync("inventory");};
   _onexDriversExpand.Unchecked+=(s,e)=>SetOnexDriversExpanded(false);
   _onexDriversCheck.Click+=async(s,e)=>await RunOnexDriverOperationAsync("check");
   _onexDriversRefresh.Click+=async(s,e)=>await RunOnexDriverOperationAsync("inventory");
   SetOnexDriversExpanded(false);
  }
  private void SetOnexDriversExpanded(bool expanded)
  {
   _onexDriversBody.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
   _onexDriversChevron.Glyph=expanded?"\uE70E":"\uE70D";
   Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexDriversExpand,expanded?global::XboxGamingBar.OnexStrings.Get("Hide drivers and components"):global::XboxGamingBar.OnexStrings.Get("Show drivers and components"));
   ApplyOnexSettingsDesign();
  }
  private static async Task<JsonObject> ReadOnexDriverResultAsync()
  {
   try{var file=await ApplicationData.Current.LocalFolder.GetFileAsync("onex-drivers-result.json");return JsonObject.Parse(await FileIO.ReadTextAsync(file));}catch{return null;}
  }
  private async Task RunOnexDriverOperationAsync(string action,JsonObject row=null)
  {
   if(_onexDriversBusy)return;_onexDriversBusy=true;SetOnexDriverBusy();
   try
   {
    var existing=await ReadOnexDriverResultAsync();string id=null;
    DateTimeOffset stamp;
    if(existing!=null&&!existing.GetNamedBoolean("done",true)&&DateTimeOffset.TryParse(existing.GetNamedString("utc",""),out stamp)&&(DateTimeOffset.UtcNow-stamp).TotalMinutes<8)
    {id=existing.GetNamedString("requestId","");_onexDriversStatus.Text=global::XboxGamingBar.OnexStrings.Get("Resuming the component operation…");}
    else
    {
     id=Guid.NewGuid().ToString("N");
     var request=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"action",JsonValue.CreateStringValue(action)},{"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("O"))}};
     if(row!=null){request["component"]=JsonValue.CreateStringValue(row.GetNamedString("id"));request["expectedVersion"]=JsonValue.CreateStringValue(row.GetNamedString("latest"));}
     _onexDriversStatus.Text=action=="update"?global::XboxGamingBar.OnexStrings.Get("Preparing the selected update…"):action=="check"?global::XboxGamingBar.OnexStrings.Get("Checking official releases…"):global::XboxGamingBar.OnexStrings.Get("Reading installed components…");
     var folder=ApplicationData.Current.LocalFolder;
     var temp=await folder.CreateFileAsync("onex-drivers-request.tmp",CreationCollisionOption.ReplaceExisting);await FileIO.WriteTextAsync(temp,request.Stringify());
     await temp.RenameAsync("onex-drivers-request.json",NameCollisionOption.ReplaceExisting);
    }
    await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync("DriverManager");
    bool received=false;for(int n=0;n<1200;n++)
    {
     var result=await ReadOnexDriverResultAsync();
     if(result!=null&&result.GetNamedString("requestId","")==id){
      received=true;_onexDriversStatus.Text=OnexStrings.ComponentMessage(result.GetNamedString("message",""));var data=result.GetNamedArray("rows",null);
      if(data!=null&&result.GetNamedBoolean("done",false))ApplyOnexDriverRows(data);
      if(result.GetNamedBoolean("done",false))return;
     }
     if(n==120&&!received)throw new InvalidOperationException("No component manager reply.");
     await Task.Delay(500);
    }
    _onexDriversStatus.Text=global::XboxGamingBar.OnexStrings.Get("The operation is still running or no reply was received. Reopen this section to check its status. No automatic restart is requested.");
   }
   catch{_onexDriversStatus.Text=global::XboxGamingBar.OnexStrings.Get("Could not start or read the component manager. Reopen this section and try again.");}
   finally{_onexDriversBusy=false;SetOnexDriverBusy();}
  }
  private void SetOnexDriverBusy()
  {
   _onexDriversCheck.IsEnabled=!_onexDriversBusy;_onexDriversRefresh.IsEnabled=!_onexDriversBusy;
   foreach(var button in _onexDriverButtons.Values)button.IsEnabled=!_onexDriversBusy;
  }
  private static string DriverDate(JsonObject row,string key)
  {DateTimeOffset date;return DateTimeOffset.TryParse(row.GetNamedString(key,""),out date)?date.ToLocalTime().ToString(key=="released"?"yyyy-MM-dd":"yyyy-MM-dd HH:mm"):OnexStrings.Get("Not available");}
  private void ApplyOnexDriverRows(JsonArray rows)
  {
   _onexDriverData=rows;_onexDriverRows.Children.Clear();_onexDriverButtons.Clear();
   foreach(var value in rows)
   {
    var row=value.GetObject();string id=row.GetNamedString("id");var body=new StackPanel{Spacing=4};
    var title=new Grid{ColumnSpacing=10};title.ColumnDefinitions.Add(new ColumnDefinition());title.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
    title.Children.Add(new TextBlock{Text=row.GetNamedString("name",""),FontSize=14,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
    var status=new TextBlock{Text=OnexStrings.Get(row.GetNamedString("status","Not checked")),FontSize=12,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(status,1);title.Children.Add(status);body.Children.Add(title);
    body.Children.Add(new TextBlock{Text=OnexStrings.Get(row.GetNamedString("purpose","")),FontSize=12,TextWrapping=TextWrapping.Wrap});
    string current=row.GetNamedString("current","");if(string.IsNullOrEmpty(current))current=OnexStrings.Get("Not installed / unknown");
    string latest=row.GetNamedString("latest","");if(string.IsNullOrEmpty(latest))latest=OnexStrings.Get("Not checked");
    bool vendorManaged=row.GetNamedString("status","")=="Vendor-managed";
    body.Children.Add(new TextBlock{Text=(id=="onexdrivers"?global::XboxGamingBar.OnexStrings.Format("Linked model pack: {0}  ·  Installed versions: per device",latest):(id=="onexconsole"?global::XboxGamingBar.OnexStrings.Get("ONEXConsole: "):global::XboxGamingBar.OnexStrings.Get("Installed: "))+current+(vendorManaged?"":global::XboxGamingBar.OnexStrings.Format("  ·  Latest: {0}",latest)))+(string.IsNullOrEmpty(row.GetNamedString("service",""))?"":"  ·  "+OnexStrings.ComponentServiceState(row.GetNamedString("service"))),FontSize=12,TextWrapping=TextWrapping.Wrap});
    if(!vendorManaged)body.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Format("Latest release date: {0}  ·  Last checked: {1}",DriverDate(row,"released"),DriverDate(row,"lastChecked")),FontSize=11,TextWrapping=TextWrapping.Wrap});
    else if(id=="graphics")body.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Format("Windows driver date: {0}",DriverDate(row,"released")),FontSize=11,TextWrapping=TextWrapping.Wrap});
    else if(id=="onexdrivers")body.Children.Add(new TextBlock{Text=(row.GetNamedBoolean("catalogVerified",false)?global::XboxGamingBar.OnexStrings.Get("Live catalog confirmed: "):global::XboxGamingBar.OnexStrings.Get("Last catalog attempt: "))+DriverDate(row,"lastChecked"),FontSize=11,TextWrapping=TextWrapping.Wrap});
    body.Children.Add(new TextBlock{Text=OnexStrings.ComponentMessage(row.GetNamedString("detail","")),FontSize=11,TextWrapping=TextWrapping.Wrap});
    if(row.GetNamedBoolean("canUpdate",false))body.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Select to download and update"),FontSize=12,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
    if(id=="inteldsa"||id=="graphics"||id=="onexconsole"||id=="onexdrivers")body.Children.Add(new TextBlock{Text=id=="onexdrivers"?(row.GetNamedString("source","").StartsWith("https://drive.google.com/",StringComparison.Ordinal)?global::XboxGamingBar.OnexStrings.Get("Open ONEXPLAYER 3 driver pack"):global::XboxGamingBar.OnexStrings.Get("Open official model driver catalog")):id=="onexconsole"?global::XboxGamingBar.OnexStrings.Get("Open official model driver catalog"):(row.GetNamedString("source","").EndsWith("/installer",StringComparison.Ordinal)?global::XboxGamingBar.OnexStrings.Get("Install Intel Driver & Support Assistant"):global::XboxGamingBar.OnexStrings.Get("Check Intel drivers")),FontSize=12,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
    var button=new Button{Name="OnexDriver"+id,Content=body,HorizontalContentAlignment=HorizontalAlignment.Stretch,HorizontalAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(10),IsEnabled=!_onexDriversBusy};
    Windows.UI.Xaml.Automation.AutomationProperties.SetName(button,row.GetNamedString("name")+"; "+OnexStrings.Get(row.GetNamedString("status"))+global::XboxGamingBar.OnexStrings.Get("; installed ")+current+global::XboxGamingBar.OnexStrings.Get("; latest ")+latest);
    button.Click+=async(s,e)=>{
     if(row.GetNamedBoolean("canUpdate",false)){await RunOnexDriverOperationAsync("update",row);return;}
     string source=row.GetNamedString("source","");
     if(row.GetNamedString("status")=="Vendor-managed"&&!string.IsNullOrEmpty(source)){await SendOnexHomeRequestAsync(new Windows.Foundation.Collections.ValueSet{["OnexDesktopAction"]="launch",["LaunchTarget"]=source,["InGameBar"]=widget!=null});return;}
     _onexDriversStatus.Text=OnexStrings.ComponentMessage(row.GetNamedString("detail",""));
    };
    _onexDriverButtons.Add(id,button);_onexDriverRows.Children.Add(button);
   }
   ApplyOnexSettingsDesign();
  }
 }
}
