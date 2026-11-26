using System.Text.Json;
using ONEXTweaks.WidgetBridge;
var modes=new WindowsDisplayModes().Read();
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{readOnly=true,hardwareWrites=0,modes},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true}));
return modes.Available?0:1;
