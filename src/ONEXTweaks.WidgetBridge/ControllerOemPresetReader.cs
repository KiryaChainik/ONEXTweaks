// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
namespace ONEXTweaks.WidgetBridge;

internal sealed record ControllerOemPreset(JsonObject Mapping1,JsonObject Mapping2);
internal static class ControllerOemPresetReader
{
 // Only controller columns of the selected preset are read. No game paths,
 // names, macros, network data or unrelated SQLite tables are requested.
 internal static ControllerOemPreset Read()
 {
  string appData=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
  var files=Directory.EnumerateDirectories(appData).Select(dir=>Path.Combine(dir,"db","core.db")).Where(File.Exists).ToArray();
  var selected=new List<ControllerOemPreset>();
  foreach(string file in files)
  {
   nint db=0,statement=0;
   try
   {
    if(sqlite3_open_v2(file,out db,1,0)!=0)continue;
    sqlite3_busy_timeout(db,1000);
    // A second selected row makes the source ambiguous and is rejected.
    const string sql="SELECT keyMappingInfoJson1,keyMappingInfoJson2 FROM program_handle_preset WHERE isUsing=1 LIMIT 2";
    if(sqlite3_prepare_v2(db,sql,-1,out statement,0)!=0)continue;
    int status;while((status=sqlite3_step(statement))==100)
    {
     string one=Marshal.PtrToStringUTF8(sqlite3_column_text(statement,0))??"",two=Marshal.PtrToStringUTF8(sqlite3_column_text(statement,1))??"";
     var first=JsonNode.Parse(one) as JsonObject;var second=JsonNode.Parse(two) as JsonObject;
     if(first==null||second==null||first.Count!=20||second.Count!=20)throw new InvalidDataException("The selected OEM controller preset is incomplete");
     selected.Add(new(first,second));
    }
    if(status!=101)throw new IOException("The selected controller preset could not be read consistently");
   }
   finally{if(statement!=0)sqlite3_finalize(statement);if(db!=0)sqlite3_close(db);}
  }
  if(selected.Count!=1)throw new InvalidDataException("A single selected ONEXConsole controller preset is required");
  return selected[0];
 }
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string name,out nint db,int flags,nint vfs);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_prepare_v2(nint db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int length,out nint statement,nint tail);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_step(nint statement);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern nint sqlite3_column_text(nint statement,int index);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_finalize(nint statement);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_close(nint db);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_busy_timeout(nint db,int ms);
}
