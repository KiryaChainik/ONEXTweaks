// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace ONEXTweaks.WidgetBridge;

// Marshals ONLY an explicitly supplied CSC + uniform 1D-LUT pair. It neither
// opens IGCL nor reads hardware, derives factory defaults, resets an entire pipeline, or
// establishes that a caller has a valid before-image. The caller owns those
// transaction preconditions. No hardware atomicity guarantee is implied.
internal static class IntelPixelPairWriter
{
    internal static void UseCustomPair(IntelPixelValue matrixValue, IntelPixelValue lutValue, Action<IntPtr> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        if (matrixValue?.Block is not { Type: 3 } matrixBlock || lutValue?.Block is not { Type: 1 } lutBlock)
            throw new InvalidDataException("Exactly one CSC and one uniform 1D LUT are required");
        // IDs are opaque UInt32 bit patterns carried by the existing Int32
        // record. Do not invent a positive-only ID restriction.
        if (matrixBlock.Id == lutBlock.Id || matrixValue.Result != 0 || lutValue.Result != 0)
            throw new InvalidDataException("Distinct successful color blocks are required");
        if (lutBlock.Sampling != 0 || lutBlock.Samples is < 2 or > 8192 || lutBlock.Channels is < 1 or > 3)
            throw new InvalidDataException("This Intel color LUT sampling is not supported");
        int count = checked(lutBlock.Samples * lutBlock.Channels);
        if (matrixValue.Matrix is not { Length: 15 } || matrixValue.Samples != null ||
            lutValue.Samples?.Length != count || lutValue.Matrix != null)
            throw new InvalidDataException("Complete unambiguous color payloads are required");
        // The buffers below always serialize these snapshots, even if a caller
        // mutates its managed arrays while the native callback is executing.
        var matrix = (double[])matrixValue.Matrix.Clone();
        var lut = (double[])lutValue.Samples.Clone();
        if (matrix.Any(value => !double.IsFinite(value)) || lut.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("Finite color payloads are required");

        var owned = new List<DisplayBuffer>(5);
        var errors = new List<Exception>(6);
        DisplayBuffer Buffer(int bytes, bool structure = true)
        {
            var value = new DisplayBuffer(bytes, structure);
            owned.Add(value); // Capacity is fixed before any unmanaged allocation.
            return value;
        }
        try
        {
            // Existing492 per-block ABI, unchanged: 144-byte block headers,
            // 128-byte matrix config, 40-byte LUT config and sample pointer40.
            var matrixData = Buffer(144);
            matrixData.Int(8, matrixBlock.Id); matrixData.Int(12, 3); matrixData.Int(16, 128);
            Marshal.Copy(matrix, 0, matrixData.Pointer + 24, 15);

            var samples = Buffer(checked(count * sizeof(double)), false);
            Marshal.Copy(lut, 0, samples.Pointer, count);
            var lutData = Buffer(144);
            lutData.Int(8, lutBlock.Id); lutData.Int(12, 1); lutData.Int(16, 40);
            lutData.Int(24, 0); lutData.Int(28, lutBlock.Samples); lutData.Int(32, lutBlock.Channels);
            Marshal.WriteIntPtr(lutData.Pointer, 40, samples.Pointer);

            var blocks = Buffer(checked(2 * 144), false);
            Marshal.Copy(matrixData.Bytes(), 0, blocks.Pointer, 144);
            Marshal.Copy(lutData.Bytes(), 0, blocks.Pointer + 144, 144);
            var args = Buffer(32);
            args.Int(8, 2); // SET_CUSTOM; never RESTORE_DEFAULT / NumBlocks=0.
            args.Int(16, 2);
            Marshal.WriteIntPtr(args.Pointer, 24, blocks.Pointer);
            invoke(args.Pointer); // All five owned buffers stay alive through return.
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            // Continue releasing other owned allocations if a release reports
            // an error. Preserve the original build/native error first.
            for (int n = owned.Count - 1; n >= 0; n--)
                try { owned[n].Dispose(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Color pair operation and/or cleanup failed", errors);
    }
    // The complete captured pair is required even for default reset. This
    // shares custom serialization/validation/lifetime, then selects the documented
    // block-scoped default operation. It never expands to the entire pipeline.
    internal static void UseDefaultPair(IntelPixelValue beforeMatrix, IntelPixelValue beforeLut, Action<IntPtr> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        UseCustomPair(beforeMatrix, beforeLut, args =>
        {
            Marshal.WriteInt32(args, 8, 1); // RESTORE_DEFAULT, NumBlocks stays exactly 2.
            invoke(args);
        });
    }
}
