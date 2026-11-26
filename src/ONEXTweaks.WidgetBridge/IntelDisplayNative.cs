// SPDX-License-Identifier: AGPL-3.0-only
// Intel IGCL ABI: public igcl_api.h, verified for the Windows x64 target with clang.
// See third-party/Intel-IGCL-LICENSE.txt.
using System.Runtime.InteropServices;
using System.Text;
using static ONEXTweaks.Backend.IgclNative;

namespace ONEXTweaks.WidgetBridge;

internal sealed class DisplayBuffer : IDisposable
{
    internal IntPtr Pointer { get; }
    internal int Length { get; }
    internal DisplayBuffer(int length, bool structure = true)
    {
        Length = length;
        Pointer = Marshal.AllocHGlobal(length);
        unsafe { new Span<byte>((void*)Pointer, length).Clear(); }
        if (structure) Int(0, length);
    }
    internal int Int(int offset) => Marshal.ReadInt32(Pointer, offset);
    internal void Int(int offset, int value) => Marshal.WriteInt32(Pointer, offset, value);
    internal byte Byte(int offset) => Marshal.ReadByte(Pointer, offset);
    internal void Byte(int offset, byte value) => Marshal.WriteByte(Pointer, offset, value);
    internal double Double(int offset) => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(Pointer, offset));
    internal void Double(int offset, double value) => Marshal.WriteInt64(Pointer, offset, BitConverter.DoubleToInt64Bits(value));
    internal byte[] Bytes() { var bytes = new byte[Length]; Marshal.Copy(Pointer, bytes, 0, bytes.Length); return bytes; }
    internal void Copy(byte[] bytes) { if (bytes.Length != Length) throw new InvalidDataException("Invalid display structure"); Marshal.Copy(bytes, 0, Pointer, bytes.Length); }
    public void Dispose() => Marshal.FreeHGlobal(Pointer);
}

internal sealed record IntelDisplayFeature(bool Available, int? Value, uint Result, ulong SupportedValues = 0, bool Reported = true,int? Minimum=null,int? Maximum=null,int? Step=null);
internal sealed record IntelPixelBlock(int Id, int Type, int Sampling, int Samples, int Channels);
internal sealed record IntelPixelValue(IntelPixelBlock Block, double[]? Matrix, double[]? Samples, uint Result);

internal sealed class IntelDisplayNative : IDisposable
{
    private const string Library = "ControlLib.dll";
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlGetSupported3DCapabilities(IntPtr adapter, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlGetSet3DFeature(IntPtr adapter, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlEnumerateDisplayOutputs(IntPtr adapter, ref uint count, IntPtr outputs);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlGetDisplayProperties(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlGetSupportedScalingCapability(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlGetCurrentScaling(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlSetCurrentScaling(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlGetSetRetroScaling(IntPtr adapter, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlGetIntelArcSyncProfile(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlSetIntelArcSyncProfile(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlGetIntelArcSyncInfoForMonitor(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlGetSharpnessCaps(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlGetCurrentSharpness(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlSetCurrentSharpness(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlPixelTransformationGetConfig(IntPtr output, IntPtr args);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport(Library)] private static extern uint ctlPixelTransformationSetConfig(IntPtr output, IntPtr args);
    private IntPtr api, adapter, output;
    private readonly Dictionary<int, ulong> supported3D = new();
    internal string? Error { get; private set; }
    internal bool Available => adapter != IntPtr.Zero && output != IntPtr.Zero;
    internal int Encoder { get; private set; }
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal IntelDisplayNative()
    {
        try
        {
            var init = new InitArgs { Size = (uint)Marshal.SizeOf<InitArgs>(), AppVersion = 65536 };
            Check(ctlInit(ref init, out api), "Intel display initialization");
            uint count = 0;
            Check(ctlEnumerateDevices(api, ref count, IntPtr.Zero), "Intel adapters");
            if (count is 0 or >64) throw new IOException("No Intel display adapter");
            using var handles = new DisplayBuffer((int)count * 8, false);
            using var deviceId = new DisplayBuffer(8, false);
            uint capacity = count;
            Check(ctlEnumerateDevices(api, ref count, handles.Pointer), "Intel adapters");
            if (count > capacity) throw new IOException("Intel adapters changed");
            for (int index = 0; index < count; index++)
            {
                IntPtr candidate = Marshal.ReadIntPtr(handles.Pointer, index * 8);
                var properties = new AdapterProperties { Size = (uint)Marshal.SizeOf<AdapterProperties>(), DeviceId = deviceId.Pointer, DeviceIdSize = 8, Name = "", Reserved = new byte[108] };
                if (ctlGetDeviceProperties(candidate, ref properties) == 0 && properties.PciVendorId == 0x8086 && properties.PciDeviceId == 0xB080) { adapter = candidate; break; }
            }
            if (adapter == IntPtr.Zero) throw new IOException("Intel Arc B390 not found");
            using var caps = new DisplayBuffer(24);
            Check(ctlGetSupported3DCapabilities(adapter, caps.Pointer), "Intel graphics capabilities");
            int features = caps.Int(8);
            if (features is <0 or >256) throw new IOException("Invalid Intel feature count");
            if (features > 0)
            {
                using var entries = new DisplayBuffer(features * 72, false);
                Marshal.WriteIntPtr(caps.Pointer, 16, entries.Pointer);
                Check(ctlGetSupported3DCapabilities(adapter, caps.Pointer), "Intel graphics capabilities");
                if (caps.Int(8) > features) throw new IOException("Intel capabilities changed");
                for (int index = 0; index < caps.Int(8); index++) supported3D[Marshal.ReadInt32(entries.Pointer, index * 72)] = (ulong)Marshal.ReadInt64(entries.Pointer, index * 72 + 8);
            }
            RefreshOutput();
        }
        catch (Exception error) { Error = error.Message; Dispose(); }
    }
    internal static void Check(uint result, string action) { if (result != 0) throw new IOException(action + ": 0x" + result.ToString("X8")); }
    internal void RefreshOutput()
    {
        if (adapter == IntPtr.Zero) throw new IOException(Error ?? "Intel display unavailable");
        uint count = 0;
        Check(ctlEnumerateDisplayOutputs(adapter, ref count, IntPtr.Zero), "Intel display outputs");
        if (count is 0 or >64) throw new IOException("No Intel display output");
        using var handles = new DisplayBuffer((int)count * 8, false);
        uint capacity = count;
        Check(ctlEnumerateDisplayOutputs(adapter, ref count, handles.Pointer), "Intel display outputs");
        if (count > capacity) throw new IOException("Intel outputs changed");
        var active = new List<(IntPtr Handle, int Encoder, int Width, int Height)>();
        for (int index = 0; index < count; index++)
        {
            var handle = Marshal.ReadIntPtr(handles.Pointer, index * 8);
            using var properties = new DisplayBuffer(200);
            if (ctlGetDisplayProperties(handle, properties.Pointer) == 0 && (properties.Int(48) & 3) == 3)
                active.Add((handle, properties.Int(8), properties.Int(88), properties.Int(92)));
        }
        // Never silently apply one display's settings to another attached monitor.
        var selected = active.FirstOrDefault(value => value.Encoder == Encoder);
        if (selected.Handle == IntPtr.Zero && active.Count == 1) selected = active[0];
        if (selected.Handle == IntPtr.Zero) throw new IOException(active.Count == 0 ? "No active Intel display" : "More than one Intel display is active");
        output = selected.Handle; Encoder = selected.Encoder; Width = selected.Width; Height = selected.Height;
    }
    internal IntelDisplayFeature Read3D(int feature, string? application = null)
    {
        if (!supported3D.TryGetValue(feature, out ulong mask)) return new(false, null, 0x4000000A);
        using var args = NewFeature(feature, false, application, out var name);
        try
        {
            uint result = ctlGetSet3DFeature(adapter, args.Pointer);
            // No override stored is distinct from a reported driver value.
            if (feature == 17 && result == 0x40000014) return new(true, 0, result, mask, false);
            return new(result == 0, result == 0 ? args.Int(32) : null, result, mask);
        }
        finally { if (name != IntPtr.Zero) Marshal.FreeHGlobal(name); }
    }
    private static DisplayBuffer NewFeature(int feature, bool set, string? application, out IntPtr name)
    {
        name = IntPtr.Zero;
        var args = new DisplayBuffer(56);
        args.Int(8, feature); args.Int(28, 4); args.Byte(25, set ? (byte)1 : (byte)0);
        if (!string.IsNullOrEmpty(application))
        {
            if (Path.GetFileName(application) != application || !application.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { args.Dispose(); throw new ArgumentException("Invalid graphics application"); }
            byte[] bytes = Encoding.UTF8.GetBytes(application + "\0");
            if (bytes.Length > 128) { args.Dispose(); throw new ArgumentException("Graphics application name is too long"); }
            name = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, name, bytes.Length);
            Marshal.WriteIntPtr(args.Pointer, 16, name); args.Byte(24, (byte)(bytes.Length - 1));
        }
        return args;
    }
    internal void Set3D(int feature, int value, string? application = null)
    {
        bool valid = feature switch { 9 => IntelSyncPolicy.Values.Contains(value), 14 or 16 => value is >=0 and <=2, 17 => value is >=0 and <=3, _ => false };
        if (!valid || !supported3D.ContainsKey(feature)) throw new ArgumentException("Unsupported Intel graphics setting");
        if(feature==9&&!IntelSyncPolicy.Supports(supported3D[feature],value))throw new InvalidOperationException("This Intel Sync mode is not supported by the current graphics driver.");
        var before = Read3D(feature, application);
        void Write(int requested)
        {
            using var args = NewFeature(feature, true, application, out var name);
            try { args.Int(32, requested); Check(ctlGetSet3DFeature(adapter, args.Pointer), "Intel graphics change"); }
            finally { if (name != IntPtr.Zero) Marshal.FreeHGlobal(name); }
        }
        try
        {
            Write(value); var actual = Read3D(feature, application);
            if (!actual.Available || !actual.Reported || actual.Value != value) throw new IOException("Intel graphics readback differs from the requested value");
        }
        catch
        {
            if(before.Available && before.Value is int original) Write(original);
            throw;
        }
    }
    internal byte[] ReadScaling()
    {
        using var args = new DisplayBuffer(28); args.Byte(4, 1);
        uint result = ctlGetCurrentScaling(output, args.Pointer);
        if (result == 0x40000009) { args.Byte(4, 0); result = ctlGetCurrentScaling(output, args.Pointer); }
        Check(result, "Intel scaling read"); return args.Bytes();
    }
    internal int ReadScalingCaps() { using var args = new DisplayBuffer(12); Check(ctlGetSupportedScalingCapability(output, args.Pointer), "Intel scaling capabilities"); return args.Int(8); }
    internal void WriteScaling(byte[] bytes)
    {
        using var args = new DisplayBuffer(28); args.Copy(bytes); Check(ctlSetCurrentScaling(output, args.Pointer), "Intel scaling change");
        byte[] actual = ReadScaling(); int expected = BitConverter.ToInt32(bytes, 8);
        int current = BitConverter.ToInt32(actual, 8), preferred = BitConverter.ToInt32(actual, 24);
        if (current != expected && preferred != expected) throw new IOException("Intel scaling readback differs from the requested mode");
    }
    internal byte[] ReadRetro() { using var args = new DisplayBuffer(12); args.Byte(5, 1); Check(ctlGetSetRetroScaling(adapter, args.Pointer), "Intel retro scaling read"); return args.Bytes(); }
    internal void WriteRetro(byte[] bytes)
    {
        using var args = new DisplayBuffer(12); args.Copy(bytes); args.Byte(5, 0);
        // Unconfigured disabled reads as type0; Intel rejects that enum on SET even when disabled.
        if(bytes[6]==0&&args.Int(8)==0)args.Int(8,1);
        Check(ctlGetSetRetroScaling(adapter, args.Pointer), "Intel retro scaling change");
        var actual = ReadRetro(); if (actual[6] != bytes[6] || bytes[6] != 0 && BitConverter.ToInt32(actual, 8) != BitConverter.ToInt32(bytes, 8)) throw new IOException("Intel retro scaling readback differs from the requested mode");
    }
    internal byte[] ReadArcSync() { using var args = new DisplayBuffer(28); Check(ctlGetIntelArcSyncProfile(output, args.Pointer), "Intel VRR read"); return args.Bytes(); }
    internal bool MonitorSupportsVrr() { using var args = new DisplayBuffer(24); return ctlGetIntelArcSyncInfoForMonitor(output, args.Pointer) == 0 && args.Byte(5) != 0; }
    internal void WriteArcSync(byte[] bytes)
    {
        using var args = new DisplayBuffer(28); args.Copy(bytes); Check(ctlSetIntelArcSyncProfile(output, args.Pointer), "Intel VRR change");
        if (BitConverter.ToInt32(ReadArcSync(), 8) != BitConverter.ToInt32(bytes, 8)) throw new IOException("Intel VRR readback differs from the requested profile");
    }
    internal IntelDisplayFeature ReadSharpness()
    {
        using var properties=new DisplayBuffer(256*20,false);using var caps = new DisplayBuffer(24);
        Marshal.WriteIntPtr(caps.Pointer,16,properties.Pointer);caps.Byte(12,2);
        uint result = ctlGetSharpnessCaps(output, caps.Pointer);
        if (result != 0 || (caps.Int(8) & 2) == 0) return new(false, null, result!=0?result:0x4000000A);
        int count=caps.Byte(12),selected=-1;
        for(int i=0;i<count;i++)if(properties.Int(i*20)==2){selected=i;break;}
        // This G3 driver advertises the adaptive flag but labels its sole range
        // property type1. Current/set calls still use the advertised adaptive2.
        if(selected<0&&count==1)selected=0;
        if(selected<0)return new(false,null,0x4000000A);
        float minimum=BitConverter.Int32BitsToSingle(properties.Int(selected*20+4)),maximum=BitConverter.Int32BitsToSingle(properties.Int(selected*20+8)),step=BitConverter.Int32BitsToSingle(properties.Int(selected*20+12));
        if(!float.IsFinite(minimum)||!float.IsFinite(maximum)||!float.IsFinite(step)||minimum<0||maximum>100||maximum<minimum||step<1||minimum!=MathF.Round(minimum)||maximum!=MathF.Round(maximum)||step!=MathF.Round(step))return new(false,null,0x4000000A);
        using var args = new DisplayBuffer(16); args.Int(8, 2); result = ctlGetCurrentSharpness(output, args.Pointer);
        return new(result == 0, result == 0 ? args.Byte(5) == 0 ? 0 : (int)Math.Round(BitConverter.Int32BitsToSingle(args.Int(12))) : null, result,Minimum:(int)minimum,Maximum:(int)maximum,Step:(int)step);
    }
    internal object ProbeSharpness()
    {
        if(!Available)return new{readOnly=true,available=false,error=Error};
        using var legacy=new DisplayBuffer(24);uint legacyResult=ctlGetSharpnessCaps(output,legacy.Pointer);
        // NumFilterTypes is a byte. Allocate enough for every representable entry;
        // the public contract requires caller-owned pFilterProperty storage.
        using var properties=new DisplayBuffer(256*20,false);using var caps=new DisplayBuffer(24);
        Marshal.WriteIntPtr(caps.Pointer,16,properties.Pointer);caps.Byte(12,2);
        uint result=ctlGetSharpnessCaps(output,caps.Pointer);int count=result==0?caps.Byte(12):0;
        var filters=new List<object>();
        if(result==0)for(int i=0;i<count;i++)
        {
            int offset=i*20;float Read(int n)=>BitConverter.Int32BitsToSingle(properties.Int(offset+n));
            filters.Add(new{type=properties.Int(offset),minimum=Read(4),maximum=Read(8),step=Read(12),defaultValue=Read(16)});
        }
        using var current=new DisplayBuffer(16);current.Int(8,2);uint currentResult=ctlGetCurrentSharpness(output,current.Pointer);
        return new{readOnly=true,available=true,encoder=Encoder,width=Width,height=Height,legacyCapsResult="0x"+legacyResult.ToString("X8"),allocatedCapsResult="0x"+result.ToString("X8"),flags=caps.Int(8),count,filters,currentResult="0x"+currentResult.ToString("X8"),enabled=current.Byte(5)!=0,intensity=currentResult==0?(float?)BitConverter.Int32BitsToSingle(current.Int(12)):null};
    }
    internal void SetSharpness(int value)
    {
        var feature=ReadSharpness();
        if(!feature.Available)throw new InvalidOperationException("Adaptive Sharpness is unavailable on the active display");
        int minimum=feature.Minimum??throw new IOException("Sharpness minimum is unknown"),maximum=feature.Maximum??throw new IOException("Sharpness maximum is unknown"),step=feature.Step??throw new IOException("Sharpness step is unknown");
        if(value!=0&&(value<minimum||value>maximum||(value-minimum)%step!=0))throw new ArgumentOutOfRangeException(nameof(value),"Use a sharpness level reported by the Intel driver");
        var before=ReadSharpnessRaw();
        using var args = new DisplayBuffer(16); args.Int(8, 2); args.Int(12, BitConverter.SingleToInt32Bits(value)); args.Byte(5, value > 0 ? (byte)1 : (byte)0);
        try{Check(ctlSetCurrentSharpness(output, args.Pointer), "Intel sharpness change");if(ReadSharpness().Value!=value)throw new IOException("Intel sharpness readback differs from the requested value");}
        catch{WriteSharpnessRaw(before);throw;}
    }
    internal byte[] ReadSharpnessRaw(){using var args=new DisplayBuffer(16);args.Int(8,2);Check(ctlGetCurrentSharpness(output,args.Pointer),"Intel sharpness read");return args.Bytes();}
    internal void WriteSharpnessRaw(byte[] bytes){using var args=new DisplayBuffer(16);args.Copy(bytes);Check(ctlSetCurrentSharpness(output,args.Pointer),"Intel sharpness restore");if(!ReadSharpnessRaw().SequenceEqual(bytes))throw new IOException("Exact Intel sharpness state did not restore");}
    internal List<IntelPixelBlock> ReadPixelCaps()
    {
        using var args = new DisplayBuffer(272);
        Check(ctlPixelTransformationGetConfig(output, args.Pointer), "Intel color capabilities");
        int count = args.Int(256); if (count is <1 or >32) throw new IOException("No Intel color pipeline reported");
        using var blocks = new DisplayBuffer(count * 144, false);
        for (int index = 0; index < count; index++) Marshal.WriteInt32(blocks.Pointer, index * 144, 144);
        Marshal.WriteIntPtr(args.Pointer, 264, blocks.Pointer);
        Check(ctlPixelTransformationGetConfig(output, args.Pointer), "Intel color capabilities");
        if (args.Int(256) > count) throw new IOException("Intel color capabilities changed");
        var result = new List<IntelPixelBlock>();
        for (int index = 0; index < args.Int(256); index++)
        { IntPtr block = blocks.Pointer + index * 144; result.Add(new(Marshal.ReadInt32(block, 8), Marshal.ReadInt32(block, 12), Marshal.ReadInt32(block, 24), Marshal.ReadInt32(block, 28), Marshal.ReadInt32(block, 32))); }
        return result;
    }
    private static DisplayBuffer PixelBlockBuffer(IntelPixelBlock block, out DisplayBuffer? samples)
    {
        samples = null;
        ArgumentNullException.ThrowIfNull(block);
        if (block.Type == 1)
        {
            if (block.Sampling != 0 || block.Samples is <2 or >8192 || block.Channels is <1 or >3)
                throw new IOException("This Intel color LUT sampling is not supported");
        }
        else if (block.Type is not (3 or 4)) throw new IOException("Unsupported Intel color block");
        var args = new DisplayBuffer(144);
        try
        {
            args.Int(8, block.Id); args.Int(12, block.Type);
            if (block.Type is 3 or 4) args.Int(16, 128);
            else
            {
                args.Int(16, 40); args.Int(24, 0); args.Int(28, block.Samples); args.Int(32, block.Channels);
                samples = new DisplayBuffer(checked(block.Samples * block.Channels * 8), false);
                Marshal.WriteIntPtr(args.Pointer, 40, samples.Pointer);
            }
            return args;
        }
        catch (Exception primary)
        {
            Exception? sampleError = null, blockError = null;
            var ownedSamples = samples; samples = null;
            try { ownedSamples?.Dispose(); } catch (Exception error) { sampleError = error; }
            try { args.Dispose(); } catch (Exception error) { blockError = error; }
            if (sampleError != null || blockError != null)
            {
                var errors = new List<Exception>(3) { primary };
                if (sampleError != null) errors.Add(sampleError);
                if (blockError != null) errors.Add(blockError);
                throw new AggregateException("Intel color buffer allocation and/or cleanup failed", errors);
            }
            throw;
        }
    }
    internal IntelPixelValue ReadPixel(IntelPixelBlock block)
    {
        using var data = PixelBlockBuffer(block, out var samples);
        using (samples)
        {
            using var args = new DisplayBuffer(272); args.Int(8, 1); args.Int(256, 1); Marshal.WriteIntPtr(args.Pointer, 264, data.Pointer);
            uint result = ctlPixelTransformationGetConfig(output, args.Pointer);
            double[]? matrix = null, values = null;
            if (result == 0)
            {
                if (block.Type is 3 or 4) { matrix = new double[15]; Marshal.Copy(data.Pointer + 24, matrix, 0, 15); }
                else if (samples != null) { values = new double[block.Samples * block.Channels]; Marshal.Copy(samples.Pointer, values, 0, values.Length); }
            }
            return new(block, matrix, values, result);
        }
    }
    internal void WritePixel(IntelPixelValue value, bool restoreDefault = false)
    {
        using var data = PixelBlockBuffer(value.Block, out var samples);
        using (samples)
        {
            if (!restoreDefault)
            {
                if (value.Matrix is { Length: 15 } matrix) Marshal.Copy(matrix, 0, data.Pointer + 24, 15);
                else if (samples != null && value.Samples?.Length == value.Block.Samples * value.Block.Channels) Marshal.Copy(value.Samples, 0, samples.Pointer, value.Samples.Length);
                else throw new InvalidDataException("Invalid Intel color payload");
            }
            using var args = new DisplayBuffer(32); args.Int(8, restoreDefault ? 1 : 2); args.Int(16, 1); Marshal.WriteIntPtr(args.Pointer, 24, data.Pointer);
            Check(ctlPixelTransformationSetConfig(output, args.Pointer), restoreDefault ? "Intel color reset" : "Intel color change");
        }
    }
    internal void WritePixelPair(IntelPixelValue matrix, IntelPixelValue lut)
    {
        IntelPixelPairWriter.UseCustomPair(matrix, lut,
            args => Check(ctlPixelTransformationSetConfig(output, args), "Intel color pair change"));
    }
    internal void RestorePixelPair(IntelPixelValue beforeMatrix, IntelPixelValue beforeLut)
    {
        IntelPixelPairWriter.UseDefaultPair(beforeMatrix, beforeLut,
            args => Check(ctlPixelTransformationSetConfig(output, args), "Intel color pair reset"));
    }
    public void Dispose()
    {
        output = adapter = IntPtr.Zero;
        if (api != IntPtr.Zero) { ctlClose(api); api = IntPtr.Zero; }
    }
}
