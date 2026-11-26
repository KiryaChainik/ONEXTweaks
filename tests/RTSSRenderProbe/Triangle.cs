// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential)]private struct Viewport{public float X,Y,Width,Height,Min,Max;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate IntPtr BlobData(IntPtr blob);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate nuint BlobSize(IntPtr blob);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int ShaderCreate(IntPtr device,IntPtr data,nuint size,IntPtr linkage,out IntPtr shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void ShaderBind(IntPtr context,IntPtr shader,IntPtr classes,uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void Targets(IntPtr context,uint count,[In]IntPtr[] views,IntPtr depth);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void Viewports(IntPtr context,uint count,ref Viewport viewport);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void Topology(IntPtr context,uint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void DrawCall(IntPtr context,uint vertices,uint start);
    [DllImport("d3dcompiler_47.dll",CharSet=CharSet.Ansi)]private static extern int D3DCompile([MarshalAs(UnmanagedType.LPStr)]string source,nuint length,string? name,IntPtr defines,IntPtr include,string entry,string target,uint flags,uint flags2,out IntPtr code,out IntPtr error);
    private static IntPtr[] CreateTriangle(IntPtr device,IntPtr context,IntPtr target)
    {
        const string hlsl="float4 VS(uint id:SV_VertexID):SV_POSITION { float2 p[3]={float2(-0.5,-0.5),float2(0,0.5),float2(0.5,-0.5)};return float4(p[id],0,1);} float4 PS():SV_TARGET{return float4(0.07,0.2,0.24,1);}";
        IntPtr Compile(string entry,string model,int method)
        {
            int hr=D3DCompile(hlsl,(nuint)hlsl.Length,null,IntPtr.Zero,IntPtr.Zero,entry,model,0,0,out var code,out var error);
            if(error!=IntPtr.Zero)Marshal.Release(error);Marshal.ThrowExceptionForHR(hr);
            try{Marshal.ThrowExceptionForHR(Method<ShaderCreate>(device,method)(device,Method<BlobData>(code,3)(code),Method<BlobSize>(code,4)(code),IntPtr.Zero,out var shader));return shader;}
            finally{Marshal.Release(code);}
        }
        IntPtr vs=Compile("VS","vs_4_0",12),ps=Compile("PS","ps_4_0",15);
        Method<ShaderBind>(context,11)(context,vs,IntPtr.Zero,0);Method<ShaderBind>(context,9)(context,ps,IntPtr.Zero,0);
        Method<Targets>(context,33)(context,1,new[]{target},IntPtr.Zero);Method<Topology>(context,24)(context,4);
        var viewport=new Viewport{Width=640,Height=360,Max=1};Method<Viewports>(context,44)(context,1,ref viewport);
        return new[]{vs,ps};
    }
    private static void DrawTriangle(IntPtr context)=>Method<DrawCall>(context,13)(context,3,0);
}
