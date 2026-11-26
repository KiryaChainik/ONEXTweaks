using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  // Roles: power/actions, devices, display/CPU, extras. Primary text stays neutral.
  private static readonly Dictionary<string,string[]> OnexRoleTokens=new Dictionary<string,string[]>
  {
   {"ONEXPLAYER",new[]{"F84809","00E2E3","A569FF","EA0FF6"}},
   {"OLED",new[]{"FFB454","00C8FF","C8B6FF","84D89A"}},
   {"Dracula",new[]{"FFB86C","8BE9FD","BD93F9","FF79C6"}},
   {"Catppuccin Mocha",new[]{"FAB387","94E2D5","CBA6F7","F5C2E7"}},
   {"Catppuccin Macchiato",new[]{"F5A97F","8BD5CA","C6A0F6","F5BDE6"}},
   {"Catppuccin Latte",new[]{"FE640B","179299","8839EF","EA76CB"}},
   {"Catppuccin Latte Soft (adapted)",new[]{"FE640B","179299","8839EF","EA76CB"}},
   {"Cobalt2",new[]{"FFC600","9EFFFF","FF68B8","FF9D00"}},
   {"Miasma",new[]{"B36D43","5F875F","D7C483","BB7744"}},
   {"Kanagawa Wave",new[]{"FFA066","7FB4CA","957FB8","D27E99"}},
   {"Kanagawa Lotus",new[]{"CC6D00","597B75","624C83","B35B79"}},
   {"Xbox 360",new[]{"107C10","0B7285","496F20","8A5A00"}},
   {"PS4",new[]{"6BB9FF","70D6C4","B8A5FF","EFA4CF"}},
   {"PS5",new[]{"0068BD","007D88","6454A0","B33F78"}},
   {"Liquid Glass",new[]{"93DDFA","91E1C6","C2BAFF","F4B9CD"}},
   {"Windows 11 Light",new[]{"005FB8","007D88","744DA9","A83279"}},
   {"Windows 11 Dark",new[]{"60CDFF","73DACA","C5A4F6","F4ACCF"}},
  };
  private sealed class OnexRoleColours{internal Color[] Accent=new Color[4],Ink=new Color[4];}
  private readonly Dictionary<string,OnexRoleColours> _onexRoleColours=new Dictionary<string,OnexRoleColours>();
  private static Color OnexOver(Color front,Color back,double opacity)=>Color.FromArgb(255,(byte)Math.Round(front.R*opacity+back.R*(1-opacity)),(byte)Math.Round(front.G*opacity+back.G*(1-opacity)),(byte)Math.Round(front.B*opacity+back.B*(1-opacity)));
  private static Color OnexReadableAccent(ThemeColors theme,Color raw,bool text)
  {
   double target=text?4.5:3,opacity=text?1:.85;var surfaces=OnexThemeContrastSurfaces(theme);
   for(int step=0;step<=100;step++)
   {
    double amount=step/100d;var colour=OnexOver(theme.TextPrimary,raw,amount);
    if(surfaces.All(surface=>OnexContrast(OnexOver(colour,surface,opacity),surface)>=target))return colour;
   }
   return theme.TextPrimary;
  }
  private OnexRoleColours OnexRoles(ThemeColors theme)
  {
   string key=theme.Name+theme.CardBackground.ToString()+theme.ButtonBackground.ToString()+theme.TextPrimary.ToString()+theme.TextSecondary.ToString();
   var approved = OnexApprovedTheme.Find(theme.Name);
   if(_onexRoleColours.TryGetValue(key,out var roles))return roles;
   if (approved != null)
   {
    var semantic = new[] { OnexThemeRole.Power, OnexThemeRole.Network, OnexThemeRole.Display, OnexThemeRole.Lighting };
    roles = new OnexRoleColours
    {
     Accent = semantic.Select(role => approved.Accent(role)).ToArray(),
     Ink = semantic.Select(role => approved.Accent(role, true)).ToArray(),
    };
    _onexRoleColours[key] = roles;
    return roles;
   }
   roles=new OnexRoleColours();var owner=OnexOwnerPaletteRecords.FirstOrDefault(record=>record.Name==theme.Name);var tokens=owner!=null?owner.Roles:OnexRoleTokens.TryGetValue(theme.Name,out var chosen)?chosen:OnexRoleTokens[OnexPalette.Name];
   for(int i=0;i<4;i++){var raw=OnexHex(tokens[i]);roles.Accent[i]=OnexReadableAccent(theme,raw,false);roles.Ink[i]=OnexReadableAccent(theme,raw,true);}
   _onexRoleColours[key]=roles;return roles;
  }
  private Color OnexRoleAccent(int role,bool text=false)
  {var roles=OnexRoles(_onexAppliedTheme??OnexPalette.CreateTheme());return text?roles.Ink[role]:roles.Accent[role];}
  private OnexApprovedTheme OnexCurrentPalette =>
   OnexApprovedTheme.Find(_onexAppliedTheme?.Name ?? currentThemeName) ?? OnexApprovedTheme.Current;

  private Color OnexSemanticAccent(OnexThemeRole role, bool text = false) => OnexCurrentPalette.Accent(role, text);

  private Color OnexFanPointColor(int degrees)
  {
   if (degrees <= 40) return OnexSemanticAccent(OnexThemeRole.Cooling);
   if (degrees <= 60) return OnexSemanticAccent(OnexThemeRole.Success);
   if (degrees <= 80) return OnexSemanticAccent(OnexThemeRole.Warning);
   return OnexSemanticAccent(OnexThemeRole.Danger);
  }

  private Color OnexMappedAccent(Color source,bool text=false)
  {
   int role=OnexSame(source,OnexPalette.Orange)?0:OnexSame(source,OnexPalette.Cyan)?1:OnexSame(source,OnexPalette.Violet)?2:OnexSame(source,OnexPalette.Magenta)?3:-1;
   return role>=0?OnexRoleAccent(role,text):(_onexAppliedTheme?.EffectiveAccent()??OnexPalette.Orange);
  }
 }
}
