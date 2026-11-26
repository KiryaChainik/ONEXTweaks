// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Data.Json;
using Windows.Storage;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private const string OnexControllerHomeLayoutMarker="OnexControllerHomeTilesInstalledV1";
  private static readonly string[] OnexControllerHomeTileIds={"OnexControllerProfile","OnexInputMode"};
  private static string[] PlanOnexControllerHomeTileOrder(string[] ordered,bool profileSaved,bool modeSaved)
  {
   var list=ordered.Where(id=>(id!="OnexControllerProfile"||profileSaved)&&(id!="OnexInputMode"||modeSaved)).ToList();
   if(profileSaved&&modeSaved)return list.ToArray();
   if(!profileSaved){int at=modeSaved?list.IndexOf("OnexInputMode"):(list.IndexOf("LedToggle")<0?list.Count:list.IndexOf("LedToggle")+1);list.Insert(at<0?list.Count:at,"OnexControllerProfile");}
   if(!modeSaved){int at=list.IndexOf("OnexControllerProfile")+1;list.Insert(at,"OnexInputMode");}
   return list.ToArray();
  }
  private static IJsonValue OnexControllerLayoutValue(object value)
  {if(value is bool b)return JsonValue.CreateBooleanValue(b);if(value is int n)return JsonValue.CreateNumberValue(n);throw new InvalidDataException("Unexpected tile order/visibility type");}
  private void InsertOnexControllerHomeTiles()
  {
   var values=ApplicationData.Current.LocalSettings.Values;
   if(values.TryGetValue(OnexControllerHomeLayoutMarker,out var marker)&&marker is bool done&&done)return;
   if(OnexControllerHomeTileIds.Any(id=>!qsTileMap.ContainsKey(id)))return;
   string path=Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-controller-home-layout-v1.json");JsonObject journal=null;
   try
   {
    if(File.Exists(path))journal=JsonObject.Parse(File.ReadAllText(path));
    if(journal==null)
    {
     bool profile=values.ContainsKey("QS_OnexControllerProfile_Order"),mode=values.ContainsKey("QS_OnexInputMode_Order");
     if(profile&&mode){values[OnexControllerHomeLayoutMarker]=true;return;}
     var order=PlanOnexControllerHomeTileOrder(qsTileDefinitions.OrderBy(tile=>tile.Order).Select(tile=>tile.Id).ToArray(),profile,mode);
     var updates=new JsonArray();
     void Add(string key,IJsonValue after){bool present=values.TryGetValue(key,out var before);updates.Add(new JsonObject{{"key",JsonValue.CreateStringValue(key)},{"present",JsonValue.CreateBooleanValue(present)},{"before",present?OnexControllerLayoutValue(before):JsonValue.CreateNullValue()},{"after",after}});}
     for(int i=0;i<order.Length;i++)Add("QS_"+order[i]+"_Order",JsonValue.CreateNumberValue(i));
     foreach(string id in OnexControllerHomeTileIds)if(!values.ContainsKey("QS_"+id+"_Visible"))Add("QS_"+id+"_Visible",JsonValue.CreateBooleanValue(true));
     journal=new JsonObject{{"schema",JsonValue.CreateNumberValue(1)},{"passed",JsonValue.CreateBooleanValue(false)},{"updates",updates}};
     File.WriteAllText(path,journal.Stringify());
    }
    if(journal.GetNamedNumber("schema")!=1)throw new InvalidDataException("Unknown controller tile migration schema");
    var changes=journal.GetNamedArray("updates").Select(value=>value.GetObject()).ToArray();
    // Resume only our own before/after values after an interrupted write. An
    // independent owner edit is never overwritten by a recorded layout snapshot.
    foreach(var change in changes)
    {
     string key=change.GetNamedString("key");bool isOrder=key.StartsWith("QS_",StringComparison.Ordinal)&&key.EndsWith("_Order",StringComparison.Ordinal)&&qsTileMap.ContainsKey(key.Substring(3,key.Length-9));
     bool isVisibility=OnexControllerHomeTileIds.Any(id=>key=="QS_"+id+"_Visible");
     if(!isOrder&&!isVisibility)throw new InvalidDataException("Controller tile migration has an unrelated setting");
     bool present=values.TryGetValue(key,out var current);string now=present?OnexControllerLayoutValue(current).Stringify():null;
     bool before=present==change.GetNamedBoolean("present")&&(!present||now==change.GetNamedValue("before").Stringify());
     if(!before&&(!present||now!=change.GetNamedValue("after").Stringify()))throw new InvalidOperationException("Tile layout changed during update; current values were kept");
    }
    foreach(var change in changes){var value=change.GetNamedValue("after");values[change.GetNamedString("key")]=value.ValueType==JsonValueType.Boolean?(object)value.GetBoolean():(int)value.GetNumber();}
    foreach(var tile in qsTileDefinitions){if(values.TryGetValue("QS_"+tile.Id+"_Order",out var order)&&order is int n)tile.Order=n;if(values.TryGetValue("QS_"+tile.Id+"_Visible",out var visible)&&visible is bool b)tile.IsVisible=b;}
    journal["passed"]=JsonValue.CreateBooleanValue(true);journal["staleSnapshotReplayed"]=JsonValue.CreateBooleanValue(false);File.WriteAllText(path,journal.Stringify());
    values[OnexControllerHomeLayoutMarker]=true;
   }
   catch(Exception error){Logger.Warn("Controller home tile insertion stopped: "+error.Message);}
  }
 }
}
