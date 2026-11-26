// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
// Null means legacy/full capture. Turning a group off preserves its dormant local
// values; reads inherit Global, and edits of inherited settings update Global.
internal sealed record ControllerProfileGroups(bool Mappings=true,bool NintendoLayout=true,bool Vibration=true,bool GameLighting=true)
{
 internal bool All=>Mappings&&NintendoLayout&&Vibration&&GameLighting;
 internal static ControllerPolicy Compose(ControllerPolicy local,ControllerPolicy global,ControllerProfileGroups? groups){
  if(groups==null||groups.All)return local;
  return local with{Bindings=groups.Mappings?local.Bindings:global.Bindings,Oem=groups.Mappings?local.Oem:global.Oem,GamepadRemappingDisabled=groups.Mappings?local.GamepadRemappingDisabled:global.GamepadRemappingDisabled,Tuning=(local.Tuning??new()) with{NintendoLayout=groups.NintendoLayout?(local.Tuning??new()).NintendoLayout:(global.Tuning??new()).NintendoLayout},Vibration=groups.Vibration?local.Vibration:global.Vibration,ForwardGameLighting=groups.GameLighting?local.ForwardGameLighting:global.ForwardGameLighting};
 }
 private static string MappingValue(ControllerPolicy value)=>JsonSerializer.Serialize(new{Bindings=(value.Bindings??[]).OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new{p.Key,p.Value}),value.Oem,value.GamepadRemappingDisabled},Backend.Program.Json);
 internal static (ControllerPolicy Local,ControllerPolicy Global) Edit(ControllerPolicy local,ControllerPolicy global,ControllerProfileGroups? groups,ControllerPolicy proposed){
  if(groups==null||groups.All)return(proposed.Validate(),global);var effective=Compose(local,global,groups);var nextGlobal=global;
  if(!groups.Mappings){if(MappingValue(proposed)!=MappingValue(effective))nextGlobal=nextGlobal with{Bindings=proposed.Bindings,Oem=proposed.Oem,GamepadRemappingDisabled=proposed.GamepadRemappingDisabled};proposed=proposed with{Bindings=local.Bindings,Oem=local.Oem,GamepadRemappingDisabled=local.GamepadRemappingDisabled};}
  if(!groups.NintendoLayout){if((proposed.Tuning??new()).NintendoLayout!=(effective.Tuning??new()).NintendoLayout)nextGlobal=nextGlobal with{Tuning=(nextGlobal.Tuning??new()) with{NintendoLayout=(proposed.Tuning??new()).NintendoLayout}};proposed=proposed with{Tuning=(proposed.Tuning??new()) with{NintendoLayout=(local.Tuning??new()).NintendoLayout}};}
  if(!groups.Vibration){if(JsonSerializer.Serialize(proposed.Vibration,Backend.Program.Json)!=JsonSerializer.Serialize(effective.Vibration,Backend.Program.Json))nextGlobal=nextGlobal with{Vibration=proposed.Vibration};proposed=proposed with{Vibration=local.Vibration};}
  if(!groups.GameLighting){if(proposed.ForwardGameLighting!=effective.ForwardGameLighting)nextGlobal=nextGlobal with{ForwardGameLighting=proposed.ForwardGameLighting};proposed=proposed with{ForwardGameLighting=local.ForwardGameLighting};}
  return(proposed.Validate(),ReferenceEquals(nextGlobal,global)?global:nextGlobal.Validate());
 }
}
