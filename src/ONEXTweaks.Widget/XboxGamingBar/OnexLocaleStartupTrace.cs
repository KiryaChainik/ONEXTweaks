// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using Windows.Data.Json;
using Windows.Storage;
namespace XboxGamingBar
{
 internal static class OnexLocaleStartupTrace
 {
  // A short-lived, explicitly armed diagnostic. No profiles, identifiers, request
  // payloads or memory contents are recorded; normal startup performs no writes.
  internal static void Mark(string phase)
  {
   try
   {
    string local=ApplicationData.Current.LocalFolder.Path,arm=Path.Combine(local,"onex-locale-startup-armed.txt");
    if(!File.Exists(arm)||DateTime.UtcNow-File.GetLastWriteTimeUtc(arm)>TimeSpan.FromMinutes(30))return;
    var v=Windows.ApplicationModel.Package.Current.Id.Version;
    var report=new JsonObject{{"version",JsonValue.CreateStringValue(v.Major+"."+v.Minor+"."+v.Build+"."+v.Revision)},{"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))},{"phase",JsonValue.CreateStringValue(phase)}};
    File.WriteAllText(Path.Combine(local,"onex-locale-startup.json"),report.Stringify());
   }
   catch { }
  }
 }
}
