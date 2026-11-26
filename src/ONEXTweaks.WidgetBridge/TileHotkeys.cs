// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal sealed record TileBinding(string Id,uint Mask);
internal sealed class TileChordFilter
{
    private uint held;private bool armed;
    internal void Reset(){held=0;armed=false;}
    internal string? Observe(bool connected,uint buttons,IReadOnlyList<TileBinding> bindings)
    {
        if(!connected){Reset();return null;}if(buttons==0){armed=true;held=0;return null;}
        if(!armed)return null;uint previous=held;held=buttons;
        return bindings.OrderByDescending(b=>System.Numerics.BitOperations.PopCount(b.Mask)).FirstOrDefault(b=>(buttons&b.Mask)==b.Mask&&(previous&b.Mask)!=b.Mask)?.Id;
    }
}
internal sealed class TileHotkeys:IAsyncDisposable
{
    [StructLayout(LayoutKind.Sequential)]private struct Pad{public ushort Buttons;public byte L,R;public short Lx,Ly,Rx,Ry;}
    [StructLayout(LayoutKind.Sequential)]private struct State{public uint Packet;public Pad Pad;}
    [DllImport("xinput1_4.dll",ExactSpelling=true)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint XInputGetState(uint index,out State state);
    private readonly string file;private readonly CancellationTokenSource stopping=new();private readonly Task worker;private TileBinding[] bindings=[];
    internal TileHotkeys(string folder,Func<string,Task> fire)
    {
        file=Path.Combine(folder,"..","tile-hotkeys.json");if(File.Exists(file))try{bindings=Parse(File.ReadAllText(file));}catch(Exception){}
        worker=Task.Run(async()=>{var filters=Enumerable.Range(0,4).Select(_=>new TileChordFilter()).ToArray();try{while(!stopping.IsCancellationRequested){var current=Volatile.Read(ref bindings);if(current.Length==0){await Task.Delay(100,stopping.Token);continue;}bool redirected=NativeGameBarState.IsInputRedirected();for(uint index=0;index<4;index++){if((int)index==ControllerRemapper.OutputSlot){filters[index].Reset();continue;}bool connected=XInputGetState(index,out var state)==0;if(redirected){filters[index].Reset();continue;}string? id=filters[index].Observe(connected,state.Pad.Buttons,current);if(id!=null)try{await fire(id);}catch(Exception ex){await File.WriteAllTextAsync(Path.Combine(folder,"tile-hotkey-error.txt"),ex.Message,stopping.Token);}}await Task.Delay(20,stopping.Token);}}catch(OperationCanceledException){}});
    }
    private static TileBinding[] Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);if(doc.RootElement.ValueKind!=JsonValueKind.Array||doc.RootElement.GetArrayLength()>256)throw new InvalidDataException("Invalid tile bindings");
        var result=new List<TileBinding>();foreach(var item in doc.RootElement.EnumerateArray()){string id=item.GetProperty("id").GetString()??"";uint mask=item.GetProperty("mask").GetUInt32();if(id.Length is <1 or >128||mask==0||(mask&~0xF3FFu)!=0)throw new InvalidDataException("Use standard XInput buttons for ONEXPLAYER tile shortcuts");if(result.Any(b=>b.Mask==mask))throw new InvalidDataException("Controller combination is already assigned");result.Add(new(id,mask));}return result.ToArray();
    }
    internal void Update(string json){var next=Parse(json);string temp=file+".tmp";File.WriteAllText(temp,json);File.Move(temp,file,true);Volatile.Write(ref bindings,next);}
    public async ValueTask DisposeAsync(){stopping.Cancel();await worker;stopping.Dispose();}
}
