// Read-only driver and battery telemetry. No GPU control or MSR access in this process.
using System.Management;
using System.Runtime.InteropServices;
using static ONEXTweaks.Backend.IgclNative;
namespace ONEXTweaks.WidgetBridge;
internal sealed record DeviceMetrics(double? GpuLoadPercent,double? GpuTemperatureC,double? SystemPowerWatts,string GpuLoadSource,string GpuTemperatureSource,string SystemPowerSource,double? GpuClockMhz=null,double? GpuPowerWatts=null);
internal sealed class DeviceTelemetry:IDisposable
{
    private IntPtr api,adapter;
    private double? previousTime,previousActivity,previousGpuEnergy;
    private string gpuStatus="Intel telemetry unavailable";
    private DateTimeOffset retry;
    internal DeviceMetrics Read()
    {
        double? load=null,temp=null,total=null,clock=null,gpuPower=null;
        try
        {
            if(adapter==IntPtr.Zero&&DateTimeOffset.UtcNow>=retry)Initialize();
            if(adapter!=IntPtr.Zero)
            {
                var sample=Telemetry.Create();uint result=Bulk.ctlPowerTelemetryGet(adapter,ref sample);
                if(result!=0)throw new InvalidOperationException("IGCL telemetry 0x"+result.ToString("X8"));
                var time=Seconds(sample.timeStamp);var activity=Seconds(sample.globalActivityCounter);
                if(time is {} now&&activity is {} busy&&previousTime is {} before&&previousActivity is {} previous&&now>before&&now-before<=10&&busy>=previous)
                    load=Math.Clamp((busy-previous)/(now-before)*100,0,100);
                var energy=Number(sample.gpuEnergyCounter,6);
                if(time is {} stamp&&previousTime is {} previousStamp&&energy is {} joules&&previousGpuEnergy is {} previousJoules&&stamp>previousStamp&&stamp-previousStamp<=10&&joules>=previousJoules)
                    gpuPower=(joules-previousJoules)/(stamp-previousStamp);
                if(gpuPower is <0 or >100)gpuPower=null;
                previousTime=time;previousActivity=activity;previousGpuEnergy=energy;
                temp=Number(sample.gpuCurrentTemperature,5);clock=Number(sample.gpuCurrentClockFrequency,0);
                if(temp is <1 or >120)temp=null;
                gpuStatus="Intel IGCL global activity counter";
            }
        }
        catch(Exception ex){gpuStatus=ex.Message;Dispose();retry=DateTimeOffset.UtcNow.AddSeconds(10);}
        string totalSource="Total system power unavailable on AC; no whole-device AC power sensor";
        try
        {
            using var search=new ManagementObjectSearcher("root\\wmi","SELECT PowerOnline,Discharging,DischargeRate FROM BatteryStatus");
            using var rows=search.Get();double sum=0;bool valid=rows.Count>0;
            foreach(ManagementObject row in rows)
            {
                bool online=Convert.ToBoolean(row["PowerOnline"]),discharging=Convert.ToBoolean(row["Discharging"]);
                double rate=Convert.ToDouble(row["DischargeRate"]);
                if(online||!discharging||rate<=0||rate>200000){valid=false;break;}
                sum+=rate/1000.0;
            }
            if(valid){total=sum;totalSource="Whole-device battery discharge rate (Windows BatteryStatus), watts";}
        }
        catch(Exception ex){totalSource="Battery power sensor unavailable: "+ex.Message;}
        return new(load,temp,total,gpuStatus,temp.HasValue?"Intel GPU temperature sensor":"Intel driver does not expose a GPU temperature sensor",totalSource,clock,gpuPower);
    }
    private void Initialize()
    {
        Dispose();retry=DateTimeOffset.UtcNow.AddSeconds(10);
        var init=new InitArgs{Size=(uint)Marshal.SizeOf<InitArgs>(),AppVersion=1u<<16,Flags=1};
        uint result=ctlInit(ref init,out api);if(result!=0){api=IntPtr.Zero;throw new InvalidOperationException("IGCL init 0x"+result.ToString("X8"));}
        uint count=0;result=ctlEnumerateDevices(api,ref count,IntPtr.Zero);if(result!=0||count==0||count>64)throw new InvalidOperationException("Intel GPU enumeration unavailable");
        var buffer=Marshal.AllocHGlobal((int)count*IntPtr.Size);
        try
        {
            result=ctlEnumerateDevices(api,ref count,buffer);if(result!=0||count>64)throw new InvalidOperationException("Intel GPU enumeration failed");
            for(int i=0;i<count;i++)
            {
                var candidate=Marshal.ReadIntPtr(buffer,i*IntPtr.Size);var id=Marshal.AllocHGlobal(8);
                try
                {
                    var props=new AdapterProperties{Size=(uint)Marshal.SizeOf<AdapterProperties>(),DeviceId=id,DeviceIdSize=8,Name="",Reserved=new byte[108]};
                    if(ctlGetDeviceProperties(candidate,ref props)==0&&props.PciVendorId==0x8086&&props.PciDeviceId==0xB080){adapter=candidate;break;}
                }
                finally{Marshal.FreeHGlobal(id);}
            }
            if(adapter==IntPtr.Zero)throw new InvalidOperationException("Verified Intel Arc B390 adapter not found");
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
    private static double? Seconds(Item item)=>Number(item,7);
    private static double? Number(Item item,uint unit)
    {
        if(!item.supported||item.units!=unit)return null;
        double value=item.type switch{9=>item.value.D,8=>item.value.F,7=>item.value.U,6=>item.value.I,_=>double.NaN};
        return double.IsFinite(value)?value:null;
    }
    public void Dispose(){adapter=IntPtr.Zero;previousTime=previousActivity=previousGpuEnergy=null;if(api!=IntPtr.Zero){ctlClose(api);api=IntPtr.Zero;}}
}
