// SPDX-License-Identifier: AGPL-3.0-only
// ABI: installed RTSS SDK Include/RTSSSharedMemory.h and its SharedMemory sample.
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace ONEXTweaks.WidgetBridge;

internal sealed record FrameStatistics(int Frames,double Minimum,double Average,double Maximum,double OnePercentLow);
internal sealed record RtssApp(uint ProcessId,string Name,double? Fps,double? FrameMs,uint Flags,FrameStatistics? Statistics=null);
internal sealed record RtssWrite(uint Version,int Slot,uint Frame,string Text,IReadOnlyList<RtssApp> Applications);

internal static class RtssMemory
{
    internal const string Owner="ONEXTweaks";
    internal static unsafe RtssWrite Write(string text,bool graph,bool release=false)
    {
        using var memory=MemoryMappedFile.OpenExisting("RTSSSharedMemoryV2",MemoryMappedFileRights.ReadWrite);
        using var view=memory.CreateViewAccessor(0,0,MemoryMappedFileAccess.ReadWrite);
        byte* pointer=null;view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        try
        {
            pointer+=view.PointerOffset;
            uint Read(int offset)=>view.ReadUInt32(offset);
            if(Read(0)!=0x52545353||Read(4)<0x2000e)throw new IOException("Unsupported RTSS shared-memory signature/version");
            uint version=Read(4),size=Read(20),offset=Read(24),count=Read(28);
            if(size<4608||size>1048576||count>256||count<2||(ulong)offset+(ulong)size*count>(ulong)view.Capacity)throw new IOException("Invalid RTSS OSD array");
            // Same bit-0 lock as the SDK. Never clear another writer's lock.
            ref int busy=ref *(int*)(pointer+36);bool locked=false;
            for(int attempt=0;attempt<20;attempt++){if((Interlocked.Or(ref busy,1)&1)==0){locked=true;break;}Thread.Sleep(1);}
            if(!locked)throw new IOException("RTSS renderer is busy");
            int slot=-1;string actual="";
            try
            {
                string CString(long at,int bytes){var data=new byte[bytes];view.ReadArray(at,data,0,bytes);return Encoding.ASCII.GetString(data).Split('\0')[0];}
                for(int pass=0;pass<2&&slot<0;pass++)for(int i=1;i<count;i++)
                {
                    long at=offset+i*(long)size;string owner=CString(at+256,256);
                    if(owner==Owner||pass==1&&owner.Length==0&&!release){slot=i;break;}
                }
                if(slot>=0)
                {
                    long at=offset+slot*(long)size;
                    if(release){view.WriteArray(at,new byte[size],0,(int)size);}
                    else
                    {
                        var owner=Encoding.ASCII.GetBytes(Owner+'\0');view.WriteArray(at+256,owner,0,owner.Length);
                        int textOffset=version>=0x20014?266752:512,textCapacity=version>=0x20014?32768:4096;
                        if(size<textOffset+textCapacity)throw new IOException("RTSS extended text field lies outside the slot");
                        byte[] data=Encoding.ASCII.GetBytes(text);if(data.Length>=textCapacity)throw new InvalidDataException("OSD exceeds RTSS extended text capacity");
                        view.WriteArray(at+textOffset,new byte[textCapacity],0,textCapacity);view.WriteArray(at+textOffset,data,0,data.Length);
                        if(graph)
                        {
                            if(size<4644)throw new IOException("RTSS slot cannot hold a graph object");
                            long obj=at+4608;
                            view.Write(obj,0x47523030u);view.Write(obj+4,36u);view.Write(obj+8,-32);view.Write(obj+12,-2);view.Write(obj+16,1);
                            view.Write(obj+20,4u);view.Write(obj+24,0f);view.Write(obj+28,50000f);view.Write(obj+32,0u);
                        }
                        actual=CString(at+textOffset,textCapacity);if(actual!=text)throw new IOException("RTSS OSD text readback differs");
                    }
                    view.Write(32,Read(32)+1);
                }
                else if(!release)throw new IOException("All RTSS OSD slots are occupied");
            }
            finally{Interlocked.And(ref busy,~1);}
            var apps=new List<RtssApp>();uint appSize=Read(8),appOffset=Read(12),appCount=Read(16);
            if(appSize>=288&&appCount<=1024&&(ulong)appOffset+(ulong)appCount*appSize<=(ulong)view.Capacity)
                for(int i=0;i<appCount;i++)
                {
                    long at=appOffset+i*(long)appSize;uint pid=view.ReadUInt32(at);if(pid==0)continue;
                    var nameBytes=new byte[260];view.ReadArray(at+4,nameBytes,0,260);string name=Encoding.ASCII.GetString(nameBytes).Split('\0')[0];
                    uint time0=view.ReadUInt32(at+268),time1=view.ReadUInt32(at+272),frames=view.ReadUInt32(at+276),us=view.ReadUInt32(at+280);
                    double? fps=time0>0&&time1>time0?1000.0*frames/(time1-time0):null;
                    FrameStatistics? statistics=null;
                    // SDK v2.5: the recent 1024 frametimes, in microseconds. No benchmark-recording state is changed.
                    if(appSize>=5028)
                    {
                        var history=new uint[1024];view.ReadArray(at+924,history,0,history.Length);
                        double[] times=history.Where(t=>t>0&&t<10000000).Select(t=>(double)t).OrderDescending().ToArray();
                        if(times.Length>0)statistics=new(times.Length,1e6/times[0],1e6/times.Average(),1e6/times[^1],1e6/times.Take(Math.Max(1,(int)Math.Ceiling(times.Length*.01))).Average());
                    }
                    apps.Add(new(pid,Path.GetFileName(name),fps,us>0?us/1000.0:null,view.ReadUInt32(at+264),statistics));
                }
            return new(version,slot,Read(32),actual,apps);
        }
        finally{view.SafeMemoryMappedViewHandle.ReleasePointer();}
    }
}
