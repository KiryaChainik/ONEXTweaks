using System;
namespace ONEXTweaks.Power
{
 // Pure monotonic policy; it never stores owner colors or accesses hardware.
 internal sealed class GameLightingLease
 {
  internal string Id="",SuppressedId="";internal int Epoch,Red,Green,Blue;internal bool Active,HasColor,Dirty;internal long Expires,NextWrite;internal string Status="Off";
  internal bool Update(string id,int epoch,int r,int g,int b,long now)
  {
   Guid parsed;if(!Guid.TryParseExact(id,"N",out parsed)||epoch<1||r<0||r>255||g<0||g>255||b<0||b>255||now<0)throw new ArgumentException("Invalid temporary game lighting request");
   if(epoch<Epoch)return false;
   if(id==SuppressedId){Status="Stopped by manual lighting change";return false;}
   if(epoch==Epoch&&Id.Length>0&&id!=Id)throw new ArgumentException("Lighting session identity changed");
   if(epoch>Epoch){Epoch=epoch;Id=id;HasColor=false;NextWrite=0;}
   bool changed=!Active||!HasColor||Red!=r||Green!=g||Blue!=b;
   Red=r;Green=g;Blue=b;Active=true;HasColor=true;Expires=now+5000;Status="Active";
   Dirty=Dirty||changed;return Dirty&&now>=NextWrite;
  }
  internal void Applied(long now){NextWrite=now+1000;Dirty=false;}
  internal bool Expire(long now){if(!Active||now<Expires)return false;Active=false;Status="Expired";return true;}
  internal bool End(string id,int epoch){if(id!=Id||epoch!=Epoch)return false;bool active=Active;Active=false;Status="Off";return active;}
  internal void Manual(){if(Id.Length>0)SuppressedId=Id;Active=false;Status="Stopped by manual lighting change";}
  internal void Disconnect(){Active=false;Epoch=0;Id="";HasColor=false;Status="Disconnected";}
 }
}
