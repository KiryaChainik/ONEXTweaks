// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

internal sealed class ControllerOemSystemActions:IDisposable
{
 private readonly ControllerOemRawInput input;private readonly Func<ControllerOemPolicy> policy;private readonly Func<bool> enabled;
 private readonly GameBarEntry entry;private readonly object gate=new();
 private sealed record ActionPress(ControllerOemPolicy Policy,string Kind,bool Closing,Task<bool?>? KeyboardWasVisible);
 private readonly Dictionary<string,ActionPress> pending=[];
 private (ControllerOemPolicy Policy,Task<bool?> Visibility)? keyboardPress;
 private readonly Func<Task>? inputMode,security;private readonly Func<string,ControllerBinding>? resolve;
 private int steamBusy,systemBusy;private readonly CancellationTokenSource stopping=new();private string? error;private object? lastSteam;private long steamCompleted;
 internal ControllerOemSystemActions(ControllerOemRawInput source,Func<ControllerOemPolicy> current,Func<bool> active,GameBarEntry gameBar,Func<Task>? switchMode=null,Func<string,ControllerBinding>? actionBinding=null,Func<Task>? secureAttention=null)
 {input=source;policy=current;enabled=active;entry=gameBar;inputMode=switchMode;resolve=actionBinding;security=secureAttention;source.Signal+=ObservePhysicalKeyboard;source.ActionSignal+=Observe;}
 internal object Read(){lock(gate)return new{enabled=enabled(),error,steamBusy=Volatile.Read(ref steamBusy)==1,lastSteam,steamCompleted};}
 private void ObservePhysicalKeyboard(ControllerOemSignal signal)
 {
  if(signal.Button!="KeyboardMouse")return;
  lock(gate)
  {
   if(signal.Cancelled){keyboardPress=null;return;}
   if(!signal.Pressed)return;
   var current=policy();
   // The Windows chord can dismiss a visible pane before the single-press gesture fires.
   keyboardPress=(enabled()||input.KeyboardSwitchEnabled)&&(resolve?.Invoke(signal.Button)??current.For(signal.Button)).Kind=="keyboardPopup"
    ?(current,ReadKeyboardVisibility()):null;
  }
 }
 private static async Task<bool?> ReadKeyboardVisibility()
 {
  try{return (await TouchKeyboard.ReadAsync()).Visible;}
  catch{return null;}
 }
 private void Observe(ControllerOemSignal signal)
 {
  ActionPress? action=null;
  lock(gate)
  {
   if(signal.Cancelled){pending.Remove(signal.Button);return;}
   if(!enabled()&&!(input.KeyboardSwitchEnabled&&signal.Button is "KeyboardMouse" or "KeyboardMouse:Double")){pending.Remove(signal.Button);return;}
   var current=policy();
   if(signal.Pressed)
   {
    string kind=(resolve?.Invoke(signal.Button)??current.For(signal.Button)).Kind;
    Task<bool?>? keyboardVisibility=null;
    if(signal.Button=="KeyboardMouse")
    {
     if(kind=="keyboardPopup"&&keyboardPress is {} physical&&ReferenceEquals(current,physical.Policy))keyboardVisibility=physical.Visibility;
     keyboardPress=null;
    }
    else if(signal.Button=="KeyboardMouse:Double")keyboardPress=null;
    if(kind is "widget" or "keyboardPopup" or "steamBigPicture" or "steamQuickAccess" or "inputMode" or "security")pending[signal.Button]=new(current,kind,kind=="widget"&&entry.BeginGuidePress(),keyboardVisibility);
   }
   else if(pending.Remove(signal.Button,out var stored)&&ReferenceEquals(current,stored.Policy))action=stored;
  }
  // Release dispatch prevents a physical system-button dismissal from closing
  // the widget immediately after it was opened.
  if(action?.Kind=="widget")entry.EnqueueOemAction(action.Closing,signal.Button);
  else if(action?.Kind is "inputMode" or "security")
  {
   var selected=action;if(Interlocked.CompareExchange(ref systemBusy,1,0)!=0)return;
   _=Task.Run(async()=>{try{if(stopping.IsCancellationRequested||!ReferenceEquals(policy(),selected.Policy))return;var callback=selected.Kind=="inputMode"?inputMode:security;if(callback==null)throw new IOException("This system action is unavailable");await callback();lock(gate)error=null;}catch(Exception e){lock(gate)error=e.Message;}finally{Volatile.Write(ref systemBusy,0);}});
  }
  else if(action?.Kind is "steamBigPicture" or "steamQuickAccess")
  {
   var selected=action;
   if(Interlocked.CompareExchange(ref steamBusy,1,0)!=0)return;
   _=Task.Run(async()=>{try{
    bool Current()=>!stopping.IsCancellationRequested&&enabled()&&ReferenceEquals(policy(),selected.Policy);
    var result=await DesktopActions.ExecuteOemSteam(selected.Kind,Current,stopping.Token);
    lock(gate){lastSteam=result;steamCompleted++;error=null;}
   }catch(OperationCanceledException){}catch(Exception e){lock(gate)error=e.Message;}finally{Volatile.Write(ref steamBusy,0);}});
  }
  else if(action?.Kind=="keyboardPopup")
   _=ToggleKeyboard(action,signal.Button);
 }
 private async Task ToggleKeyboard(ActionPress selected,string button)
 {
  try
  {
   bool Current()=>ReferenceEquals(policy(),selected.Policy)&&
    (enabled()||input.KeyboardSwitchEnabled&&(button is "KeyboardMouse" or "KeyboardMouse:Double"));
   if(button=="KeyboardMouse")
   {
    bool? wasVisible=selected.KeyboardWasVisible==null?null:await selected.KeyboardWasVisible;
    if(wasVisible==null)throw new IOException("Could not read touch keyboard visibility when the button was pressed.");
    await TouchKeyboard.SetVisibleAsync(!wasVisible.Value,Current,stopping.Token);
   }
   else await TouchKeyboard.ToggleAsync(Current,stopping.Token);
   lock(gate)error=null;
  }
  catch(OperationCanceledException){}
  catch(Exception e){lock(gate)error=e.Message;}
 }
 public void Dispose(){input.Signal-=ObservePhysicalKeyboard;input.ActionSignal-=Observe;stopping.Cancel();lock(gate){pending.Clear();keyboardPress=null;}}
}
