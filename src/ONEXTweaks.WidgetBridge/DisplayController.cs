// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal sealed record DisplayColors(int Saturation = 50, int Hue = 0, int Contrast = 50, int Brightness = 50, int Gamma = 100)
{
    internal void Validate()
    {
        if (Saturation is <0 or >100 || Hue is <-180 or >180 || Contrast is <0 or >100 || Brightness is <0 or >100 || Gamma is <30 or >280) throw new InvalidDataException("Invalid display color values");
    }
}
internal sealed record DisplayPreferences
{
    public DisplayColors? Colors { get; init; }
    public int? Sharpness { get; init; }
    public int? FrameGeneration { get; init; }
    public int? Vrr { get; init; }
    public int? VrrMode { get; init; }
    public int? LowLatency { get; init; }
    public int? FrameSync { get; init; }
    public int? ScalingMode { get; init; }
    public int? ScalingMethod { get; init; }
    public DisplayModeValue? Mode { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public HdrPreferences? Hdr { get; init; }
    internal void Validate()
    {
        Colors?.Validate();
        Hdr?.Validate();
        if (Sharpness is <0 or >100 || FrameGeneration is <0 or >3 || Vrr is <0 or >1 || VrrMode is <0 or >1 || LowLatency is <0 or >2 || FrameSync is <0 or >5 || ScalingMode is <1 or >2 || ScalingMethod is <0 or >2 || ScalingMode == 2 && ScalingMethod > 1 || Mode is { } mode && (!DisplayModeOrientationPolicy.ValidSize(mode.Width,mode.Height) || mode.Hz is <24 or >360)) throw new InvalidDataException("Invalid display profile");
    }
}
internal sealed record DisplaySavedState(int Schema, int Encoder, DisplayColors? Colors, int LastVrrProfile = 1,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? MatrixOwned = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool OwnsMatrix => MatrixOwned ?? Colors != null;
}
internal sealed record DisplaySettingState(bool Available, int? Value, string? Error = null, bool Reported = true,int? Minimum=null,int? Maximum=null,int? Step=null,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int[]? Choices=null);
internal sealed record DisplayState(bool Available, Dictionary<string, DisplaySettingState> Settings, DisplayModesState Modes, bool AtNativeResolution, bool ColorsOwned, bool ColorReadback, string? Error = null, HdrDisplayState? Hdr = null, DisplaySystemState? System = null)
{
    public RotationLockState? RotationLock { get; init; }
}

internal sealed class DisplayController : IDisposable
{
    private readonly object gate = new();
    private readonly IntelDisplayNative intel = new();
    private readonly WindowsDisplayModes modes = new();
    private readonly WindowsHdrDisplay hdr = new();
    private readonly WindowsDisplaySystem system = new();
    private readonly WindowsDisplayRotation rotation;
    private readonly string file;
    private DisplaySavedState saved;
    private bool colorsNeedReapply, colorReadback;
    private static readonly int[] SyncValues = IntelSyncPolicy.Values;
    internal DisplayController(string file, Func<Func<bool>, Task>? applySensorRotation = null,
        Action<Exception>? logSensorRotation = null)
    {
        rotation = new(gate, applySensorRotation, logSensorRotation);
        this.file = file;
        saved = new(1, intel.Encoder, null);
        if (File.Exists(file))
        {
            var loaded = JsonSerializer.Deserialize<DisplaySavedState>(File.ReadAllText(file), Backend.Program.Json) ?? throw new InvalidDataException("Empty display settings");
            loaded.Colors?.Validate();
            if (loaded.Schema != 1 || loaded.LastVrrProfile is <1 or >7 || loaded.LastVrrProfile == 5) throw new InvalidDataException("Invalid display settings");
            if (loaded.Encoder == intel.Encoder) saved = loaded;
        }
        colorsNeedReapply = saved.Colors != null;
    }
    private void Save(DisplaySavedState value)
    {
        string temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Backend.Program.Json));
        File.Move(temporary, file, true); saved = value;
    }
    private static string? Failure(uint result) => result == 0 ? null : "Intel driver: 0x" + result.ToString("X8");
    internal RotationLockState SetRotationLock(bool locked) => rotation.SetLocked(locked);
    internal DisplayState ReadForWidget() => Read() with { RotationLock = rotation.Read() };
    internal DisplayState Read(bool includeHdr = true)
    {
        lock (gate)
        {
            var settings = new Dictionary<string, DisplaySettingState>();
            var modeState = modes.Read();
            var hdrState = includeHdr ? hdr.Read() : null;
            if (!intel.Available) return new(false, settings, modeState, false, saved.Colors != null, false, intel.Error, hdrState,system.Read());
            bool atNative = false;
            try
            {
                intel.RefreshOutput();
                void Feature(string key, int id, Func<int, int>? map = null)
                {
                    var result = intel.Read3D(id);
                    settings[key] = new(result.Available, result.Value is int value ? map?.Invoke(value) ?? value : null, result.Available ? null : Failure(result.Result), result.Reported);
                }
                Feature("frameGeneration", 17); Feature("lowLatency", 16);
                Feature("frameSync", 9, raw => Array.IndexOf(SyncValues, raw));
                if(settings.TryGetValue("frameSync",out var sync))settings["frameSync"]=sync with{Choices=IntelSyncPolicy.Choices(intel.Read3D(9).SupportedValues)};
                Feature("vrrMode", 14, raw => raw == 1 ? 1 : 0);
                try
                {
                    int caps = intel.ReadScalingCaps(); atNative = (caps & 1) != 0;
                    var normal = intel.ReadScaling(); var retro = intel.ReadRetro();
                    int raw = BitConverter.ToInt32(normal, normal[4] >= 1 ? 24 : 8);
                    if (raw == 0) raw = BitConverter.ToInt32(normal, 8);
                    bool isRetro = retro[6] != 0;
                    settings["scalingMode"] = new(!atNative && caps != 0, isRetro ? 2 : 1, atNative ? "Only below native resolution" : null);
                    settings["scalingMethod"] = new(!atNative && caps != 0, isRetro ? BitConverter.ToInt32(retro, 8) == 2 ? 1 : 0 : raw == 2 ? 0 : raw == 4 ? 1 : 2, atNative ? "Only below native resolution" : null);
                }
                catch (Exception error) { settings["scalingMode"] = settings["scalingMethod"] = new(false, null, error.Message); }
                try
                {
                    int profile = BitConverter.ToInt32(intel.ReadArcSync(), 8);
                    bool available = intel.MonitorSupportsVrr() && atNative;
                    settings["vrr"] = new(available, profile == 5 ? 0 : 1, atNative ? available ? null : "VRR is not reported by the active monitor" : "Only at native resolution");
                    if (settings.TryGetValue("vrrMode", out var mode) && !available) settings["vrrMode"] = mode with { Available = false, Error = settings["vrr"].Error };
                }
                catch (Exception error) { settings["vrr"] = new(false, null, error.Message); }
                var sharpness = intel.ReadSharpness();
                string? sharpnessError=sharpness.Available?null:hdrState?.Enabled==true&&sharpness.Result==0x48000003?"Adaptive Sharpness доступен в SDR. При включённом HDR драйвер Intel отклоняет запрос. Отключи HDR в блоке ниже, чтобы менять резкость.":Failure(sharpness.Result);
                settings["sharpness"] = new(sharpness.Available, sharpness.Value, sharpnessError,Minimum:sharpness.Minimum,Maximum:sharpness.Maximum,Step:sharpness.Step);
                var color = saved.Colors ?? new DisplayColors();
                bool matrix = false, lut = false; string? colorError = null;
                try { var caps = intel.ReadPixelCaps(); matrix = caps.Any(block => block.Type == 3); lut = caps.Any(block => block.Type == 1 && block.Sampling == 0 && block.Samples is >=2 and <=8192 && block.Channels == 3); }
                catch (Exception error) { colorError = error.Message; }
                settings["saturation"] = new(matrix, color.Saturation, matrix ? null : colorError ?? "Intel color matrix unavailable", saved.Colors != null);
                settings["hue"] = new(matrix, color.Hue, settings["saturation"].Error, saved.Colors != null);
                settings["contrast"] = new(lut, color.Contrast, lut ? null : colorError ?? "Intel color LUT unavailable", saved.Colors != null);
                settings["brightness"] = new(lut, color.Brightness, settings["contrast"].Error, saved.Colors != null);
                settings["gamma"] = new(lut, color.Gamma, settings["contrast"].Error, saved.Colors != null);
                return new(true, settings, modeState, atNative, saved.Colors != null, colorReadback, Hdr: hdrState,System:system.Read());
            }
            catch (Exception error) { return new(false, settings, modeState, atNative, saved.Colors != null, colorReadback, error.Message, hdrState,system.Read()); }
        }
    }
    internal DisplayPreferences Capture(bool includeHdr = false)
    {
        lock (gate)
        {
            var state = Read(includeHdr);
            int? Value(string key) => state.Settings.TryGetValue(key, out var value) && value.Reported ? value.Value : null;
            return new() { Colors = saved.Colors, Sharpness = Value("sharpness"), FrameGeneration = Value("frameGeneration"), Vrr = Value("vrr"), VrrMode = Value("vrrMode"), LowLatency = Value("lowLatency"), FrameSync = Value("frameSync"), ScalingMode = Value("scalingMode"), ScalingMethod = Value("scalingMethod"), Mode = state.Modes.Current, Hdr = state.Hdr is { Available: true } value ? new(value.Enabled,value.AutoHdr,value.CurrentProfile,value.SdrBrightness) : null };
        }
    }
    private DisplayState ChangeNativeMode(Func<DisplayModeValue> change)
    {
        var previous=modes.Read().Current??throw new IOException("Windows did not report the current display mode.");int orientation=modes.ReadOrientation();var originalHdr=hdr.Snapshot();
        void RefreshImage()
        {
            intel.RefreshOutput();
            hdr.Restore(originalHdr);
            if (saved.Colors is not { } colors) return;
            try { ApplyColors(colors, allowOwnedMatrix: true); }
            catch (ProfileColorReadbackUnavailableException)
            {
                // A missing pre-write colour snapshot does not invalidate a
                // Windows mode that has already been read back successfully.
                colorsNeedReapply = true;
            }
        }

        try { change(); RefreshImage(); return Read(); }
        catch
        {
            if(modes.ReadOrientation()!=orientation)modes.SetOrientation(orientation);
            if(modes.Read().Current!=previous)modes.Set(previous);
            RefreshImage();
            throw;
        }
    }
    internal DisplayState Set(string key, JsonElement value)
    {
        lock (gate)
        {
            if (key is "hdrEnabled" or "autoHdr" or "hdrCalibration" or "sdrBrightness" or "hdrReset")
            {
                switch (key)
                {
                    case "hdrEnabled": hdr.SetEnabled(value.GetBoolean()); break;
                    case "autoHdr": hdr.SetAutoHdr(value.GetBoolean()); break;
                    case "hdrCalibration": hdr.SetCalibration(value.GetString() ?? ""); break;
                    case "sdrBrightness": hdr.SetSdrBrightness(value.GetInt32()); break;
                    case "hdrReset": hdr.Reset(); break;
                }
                return Read();
            }
            if(key=="displayScale"){system.SetScale(value.GetInt32());return Read();}
            if(key=="displayOrientation")
            {return ChangeNativeMode(()=>modes.SetOrientation(value.GetInt32()));}
            intel.RefreshOutput(); var state = Read();
            if (key == "resolution" || key == "refreshRate")
            {
                var current = state.Modes.Current ?? throw new IOException(state.Modes.Error);
                DisplayModeValue requested;
                if (key == "resolution")
                {
                    string[] size = (value.GetString() ?? "").Split('x');
                    if (size.Length != 2 || !int.TryParse(size[0], out int width) || !int.TryParse(size[1], out int height)) throw new ArgumentException("Invalid resolution");
                    var oriented=DisplayModeOrientationPolicy.PreserveOrientation(new(width,height,current.Hz),current);
                    requested = state.Modes.Modes.Where(mode => mode.Width == oriented.Width && mode.Height == oriented.Height).OrderBy(mode => Math.Abs(mode.Hz - current.Hz)).ThenByDescending(mode => mode.Hz).FirstOrDefault() ?? throw new ArgumentException("Resolution not reported by the display");
                }
                else requested = current with { Hz = value.GetInt32() };
                return ChangeNativeMode(()=>modes.Set(requested));
            }
            if (key == "reset") { ApplyColors(new DisplayColors(), allowOwnedMatrix: true); if (state.Settings.GetValueOrDefault("sharpness")?.Available == true) intel.SetSharpness(0); return Read(); }
            int number = value.GetInt32();
            if (!state.Settings.TryGetValue(key, out var setting) || !setting.Available) throw new IOException(setting?.Error ?? "Display setting unavailable");
            var colors = saved.Colors ?? new DisplayColors();
            switch (key)
            {
                case "saturation": ApplyColors(colors with { Saturation = number }, allowOwnedMatrix: true, matrixOnly: true); break;
                case "hue": ApplyColors(colors with { Hue = number }, allowOwnedMatrix: true, matrixOnly: true); break;
                case "contrast": ApplyColors(colors with { Contrast = number }, lutOnly: true); break;
                case "brightness": ApplyColors(colors with { Brightness = number }, lutOnly: true); break;
                case "gamma": ApplyColors(colors with { Gamma = number }, lutOnly: true); break;
                case "sharpness": intel.SetSharpness(number); break;
                case "lowLatency": intel.Set3D(16, number); break;
                case "frameSync": intel.Set3D(9, IntelSyncPolicy.Resolve(number,intel.Read3D(9).SupportedValues)); break;
                case "frameGeneration": intel.Set3D(17, number); break;
                case "vrrMode": if (number is <0 or >1) throw new ArgumentOutOfRangeException(nameof(value)); intel.Set3D(14, number == 1 ? 1 : 2); break;
                case "vrr":
                    if (number is <0 or >1) throw new ArgumentOutOfRangeException(nameof(value));
                    byte[] arc = intel.ReadArcSync(); int previous = BitConverter.ToInt32(arc, 8);
                    int selected = number == 0 ? 5 : previous != 5 ? previous : saved.LastVrrProfile;
                    BitConverter.GetBytes(selected).CopyTo(arc, 8); intel.WriteArcSync(arc);
                    if (previous != 5 && previous != 0) Save(saved with { LastVrrProfile = previous });
                    break;
                case "scalingMode":
                    // The two groups have different enums. Keep each group's last driver method
                    // instead of interpreting Retro's Integer(0) as GPU's Centered(0).
                    var storedNormal=intel.ReadScaling();int normalType=BitConverter.ToInt32(storedNormal,storedNormal[4]>=1?24:8);
                    if(normalType==0)normalType=BitConverter.ToInt32(storedNormal,8);
                    int storedMethod=number==2?BitConverter.ToInt32(intel.ReadRetro(),8)==2?1:0:normalType==2?0:normalType==4?1:2;
                    SetScaling(number,storedMethod);break;
                case "scalingMethod": SetScaling(state.Settings["scalingMode"].Value ?? 1, number); break;
                default: throw new ArgumentException("Unknown display setting");
            }
            return Read();
        }
    }
    private void SetScaling(int mode, int method)
    {
        if (mode is <1 or >2 || method < 0 || method > (mode == 2 ? 1 : 2)) throw new ArgumentException("Invalid Intel scaling mode");
        byte[] beforeRetro = intel.ReadRetro(), beforeNormal = intel.ReadScaling();
        try
        {
            var retro = (byte[])beforeRetro.Clone(); retro[6] = mode == 2 ? (byte)1 : (byte)0;
            if (mode == 2) BitConverter.GetBytes(method == 1 ? 2 : 1).CopyTo(retro, 8);
            if (retro[6] != beforeRetro[6] || mode == 2 && BitConverter.ToInt32(retro, 8) != BitConverter.ToInt32(beforeRetro, 8)) intel.WriteRetro(retro);
            if (mode == 1)
            { var normal = (byte[])beforeNormal.Clone(); normal[5] = 1; normal[20] = 1; BitConverter.GetBytes(new[] { 2, 4, 8 }[method]).CopyTo(normal, 8); intel.WriteScaling(normal); }
        }
        catch { intel.WriteRetro(beforeRetro); intel.WriteScaling(beforeNormal); throw; }
    }
    internal sealed class ProfileColorReadbackUnavailableException(string message) : IOException(message) { }

    private void RequireCompleteProfileColorSnapshot(bool retainsOwnedMatrix)
    {
        colorReadback = false;
        var caps = intel.ReadPixelCaps();
        var matrix = caps.LastOrDefault(block => block.Type == 3) ?? throw new ProfileColorReadbackUnavailableException("Intel color matrix unavailable before profile change");
        var lut = caps.LastOrDefault(block => block.Type == 1 && block.Sampling == 0 && block.Channels == 3) ?? throw new ProfileColorReadbackUnavailableException("Intel RGB color LUT unavailable before profile change");
        var actualMatrix = intel.ReadPixel(matrix); var actualLut = intel.ReadPixel(lut);
        bool matrixKnown = actualMatrix.Result == 0 && actualMatrix.Block == matrix && actualMatrix.Matrix is { Length: 15 } values && values.All(double.IsFinite);
        bool lutKnown = actualLut.Result == 0 && actualLut.Block == lut && lut.Samples is >= 2 and <= 8192 && lut.Channels == 3 && actualLut.Samples is { } samples && samples.Length == lut.Samples * lut.Channels && samples.All(double.IsFinite);
        bool recoverableMatrix = retainsOwnedMatrix && saved.OwnsMatrix && actualMatrix.Result == IntelMatrixReadUnsupported;
        if ((!matrixKnown && !recoverableMatrix) || !lutKnown)
            throw new ProfileColorReadbackUnavailableException("Intel did not return a complete current color configuration; no profile settings were changed.");
    }
    internal bool ApplyProfile(DisplayPreferences? desired,bool preserveImage=false)
    {
        if (desired == null) return false;
        lock (gate)
        {
            desired.Validate();
            var hdrRequest = preserveImage ? null : desired.Hdr;
            bool hasHdrRequest = hdrRequest is { } request &&
                (request.Enabled != null || request.AutoHdr != null || request.CalibrationProfile != null || request.SdrBrightness != null);
            var before = Capture(includeHdr: hasHdrRequest);
            bool modeChanged = desired.Mode != null && before.Mode != desired.Mode;
            bool hdrMayChange = false;
            if (hasHdrRequest && hdrRequest != null)
            {
                var actualHdr = before.Hdr;
                // Compare only explicitly requested fields with fresh reported HDR.
                // Unknown data is not a no-op. A mode change invalidates this
                // pre-mode comparison, even if every HDR field matched before it.
                hdrMayChange = modeChanged || actualHdr == null ||
                    (hdrRequest.Enabled is bool enabled && actualHdr.Enabled != enabled) ||
                    (hdrRequest.AutoHdr is bool auto && actualHdr.AutoHdr != auto) ||
                    (hdrRequest.CalibrationProfile is string profile && !string.Equals(profile, actualHdr.CalibrationProfile, StringComparison.OrdinalIgnoreCase)) ||
                    (hdrRequest.SdrBrightness is int brightness && actualHdr.SdrBrightness != brightness);
            }
            bool profileMayInvalidateColors = modeChanged || hdrMayChange;
            bool changingColors = !preserveImage &&
                (desired.Colors != null && (profileMayInvalidateColors || colorsNeedReapply || desired.Colors != saved.Colors) || desired.Colors == null && saved.Colors != null);
            if (changingColors) RequireCompleteProfileColorSnapshot(desired.Colors != null);
            // Invalidate BEFORE a native call: it may partly change the output and
            // then throw. Retain owned colors for a fresh verified retry; never
            // treat the cached desired values as proof of actual image state.
            void InvalidateProfileColorEvidence()
            { colorReadback = false; colorsNeedReapply |= saved.Colors != null; }
            if (modeChanged) { InvalidateProfileColorEvidence(); modes.Set(desired.Mode!); intel.RefreshOutput(); }
            if (hdrMayChange) { InvalidateProfileColorEvidence(); hdr.Apply(hdrRequest); }
            void Apply(string key, int? requested, int? old)
            { if (requested != null && requested != old && Read().Settings.GetValueOrDefault(key)?.Available == true) Set(key, JsonSerializer.SerializeToElement(requested.Value)); }
            Apply("frameGeneration", desired.FrameGeneration, before.FrameGeneration); Apply("lowLatency", desired.LowLatency, before.LowLatency); Apply("frameSync", desired.FrameSync, before.FrameSync);
            Apply("vrr", desired.Vrr, before.Vrr); Apply("vrrMode", desired.VrrMode, before.VrrMode); if(!preserveImage)Apply("sharpness", desired.Sharpness, before.Sharpness);
            if ((modeChanged || desired.ScalingMode != before.ScalingMode || desired.ScalingMethod != before.ScalingMethod) && desired.ScalingMode is int scaling && desired.ScalingMethod is int method && Read().Settings.GetValueOrDefault("scalingMode")?.Available == true) SetScaling(scaling, method);
            if(!preserveImage)
            {
                if (desired.Colors != null && (profileMayInvalidateColors || colorsNeedReapply || desired.Colors != saved.Colors)) ApplyColors(desired.Colors, allowOwnedMatrix: true);
                else if (desired.Colors == null && saved.Colors != null) RestoreUnownedColors();
            }
            else if(modeChanged)colorsNeedReapply=saved.Colors!=null;
            return modeChanged;
        }
    }
    internal bool NeedsInitialProfileColorReapply(DisplayPreferences profile)
    {
        lock (gate) return colorsNeedReapply && profile.Colors != null;
    }
    private const uint IntelMatrixReadUnsupported = 0x4800000b;

    private void ApplyColors(DisplayColors colors, bool lutOnly = false, bool allowOwnedMatrix = false, bool matrixOnly = false)
    {
        colors.Validate(); colorReadback = false;
        var caps = intel.ReadPixelCaps();
        var matrix = caps.LastOrDefault(block => block.Type == 3) ?? throw new IOException("Intel color matrix unavailable");
        var lut = caps.LastOrDefault(block => block.Type == 1 && block.Sampling == 0 && block.Channels == 3) ?? throw new IOException("Intel RGB color LUT unavailable");
        var beforeMatrix = intel.ReadPixel(matrix); var beforeLut = intel.ReadPixel(lut); var previous = saved;
        bool MatrixKnown(IntelPixelValue value) => value.Result == 0 && value.Block == matrix && value.Matrix is { Length: 15 } values && values.All(double.IsFinite);
        bool LutKnown(IntelPixelValue value) => value.Result == 0 && value.Block == lut && lut.Samples is >= 2 and <= 8192 && lut.Channels == 3 && value.Samples is { } values && values.Length == lut.Samples * lut.Channels && values.All(double.IsFinite);
        if (!MatrixKnown(beforeMatrix) || !LutKnown(beforeLut))
        {
            if (beforeMatrix.Result == IntelMatrixReadUnsupported && LutKnown(beforeLut) &&
                (lutOnly || allowOwnedMatrix && previous.OwnsMatrix && previous.Colors != null))
            {
                ApplyCompatibleColors(colors, matrix, beforeLut, lutOnly, matrixOnly);
                return;
            }
            throw new ProfileColorReadbackUnavailableException("Intel did not return a complete current color configuration; no image settings were changed.");
        }
        bool imageWriteAttempted = false;
        try
        {
            var changedMatrix = new IntelPixelValue(matrix, HueSaturation(colors.Hue, colors.Saturation), null, 0);
            var changedLut = ColorLut(lut, colors); var values = changedLut.Samples!;
            imageWriteAttempted = true;
            intel.WritePixelPair(changedMatrix, changedLut);
            var actualMatrix = intel.ReadPixel(matrix); var actualLut = intel.ReadPixel(lut);
            if (!MatrixKnown(actualMatrix) || !LutKnown(actualLut))
                throw new IOException("Intel did not verify the complete requested color configuration.");
            if (actualMatrix.Matrix!.Zip(changedMatrix.Matrix!, (a, b) => Math.Abs(a - b)).Any(delta => delta > .003) || actualLut.Samples!.Zip(values, (a, b) => Math.Abs(a - b)).Any(delta => delta > .003))
                throw new IOException("Intel color readback differs from the requested configuration");
            Save(saved with { Colors = colors, MatrixOwned = true }); colorsNeedReapply = false; colorReadback = true;
        }
        catch (Exception actionError)
        {
            colorReadback = false;
            var errors = new List<Exception> { actionError };
            if (imageWriteAttempted)
            {
                // These are complete actual reads captured for this transaction.
                // Neither cached desired colors nor RESTORE_DEFAULT is a rollback.
                try { intel.WritePixelPair(beforeMatrix, beforeLut); } catch (Exception error) { errors.Add(error); }
            }
            saved = previous;
            if (errors.Count > 1) throw new AggregateException("Intel color change and recovery failed; actual image state requires verification.", errors);
            throw;
        }
    }
    private void ApplyCompatibleColors(DisplayColors colors, IntelPixelBlock matrix, IntelPixelValue beforeLut, bool lutOnly, bool matrixOnly)
    {
        // This driver reports a matrix capability and accepts its legacy
        // setter, but rejects querying that matrix by block type. Keep all
        // exact readback and rollback available for the separate LUT.
        var previous = saved;
        var requestedLut = matrixOnly ? beforeLut : ColorLut(beforeLut.Block, colors);
        bool matrixWriteAttempted = false;
        bool lutWriteAttempted = false;
        try
        {
            if (!lutOnly)
            {
                matrixWriteAttempted = true;
                var requested = HueSaturation(colors.Hue, colors.Saturation);
                intel.WritePixel(new(matrix, requested, null, 0));
                var actual = intel.ReadPixel(matrix);
                if (actual.Result != IntelMatrixReadUnsupported &&
                    (actual.Result != 0 || actual.Matrix is not { Length: 15 } current ||
                    current.Zip(requested, (x, y) => double.IsFinite(x) && Math.Abs(x - y) <= .003).Any(match => !match)))
                    throw new IOException("Intel color matrix readback differs from the requested configuration.");
            }
            bool LutMatches()
            {
                var actualLut = intel.ReadPixel(beforeLut.Block);
                var expected = requestedLut.Samples!;
                return actualLut.Result == 0 && actualLut.Samples is { } current && current.Length == expected.Length &&
                    current.Zip(expected, (x, y) => double.IsFinite(x) && Math.Abs(x - y) <= .003).All(match => match);
            }

            if (!matrixOnly || !LutMatches())
            {
                lutWriteAttempted = true;
                intel.WritePixel(requestedLut);
                if (!LutMatches()) throw new IOException("Intel color LUT readback differs from the requested configuration.");
            }
            Save(previous with { Colors = colors, MatrixOwned = !lutOnly || previous.OwnsMatrix });
            // The matrix success acknowledgement is not a matrix readback.
            // A LUT-only edit also leaves any pending matrix restoration intact.
            if (!lutOnly) colorsNeedReapply = false;
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            if (matrixWriteAttempted && previous.Colors is { } oldColors)
            {
                try { intel.WritePixel(new(matrix, HueSaturation(oldColors.Hue, oldColors.Saturation), null, 0)); }
                catch (Exception rollback) { errors.Add(rollback); }
            }
            if (lutWriteAttempted || matrixWriteAttempted)
            {
                try { intel.WritePixel(beforeLut); }
                catch (Exception rollback) { errors.Add(rollback); }
            }
            saved = previous;
            if (errors.Count > 1) throw new AggregateException("Intel color change and recovery failed.", errors);
            throw;
        }
    }

    private static IntelPixelValue ColorLut(IntelPixelBlock block, DisplayColors colors)
    {
        var values = new double[block.Samples * block.Channels];
        double contrast = colors.Contrast / 50d, brightness = (colors.Brightness - 50) / 100d, gamma = colors.Gamma / 100d;
        for (int channel = 0; channel < block.Channels; channel++)
            for (int sample = 0; sample < block.Samples; sample++) values[channel * block.Samples + sample] = Math.Pow(Math.Clamp((sample / (double)(block.Samples - 1) - .5) * contrast + .5 + brightness, 0, 1), 1 / gamma);
        return new(block, null, values, 0);
    }
    internal void RestoreUnownedColors()
    {
        lock (gate)
        {
            colorReadback = false;
            var caps = intel.ReadPixelCaps();
            var matrix = caps.LastOrDefault(block => block.Type == 3) ?? throw new IOException("Intel color matrix unavailable");
            var lut = caps.LastOrDefault(block => block.Type == 1 && block.Sampling == 0 && block.Channels == 3) ?? throw new IOException("Intel RGB color LUT unavailable");
            if (matrix.Id == lut.Id || lut.Samples is < 2 or > 8192)
                throw new IOException("Intel color block capabilities are incomplete; no image settings were changed.");
            bool MatrixKnown(IntelPixelValue value) => value.Result == 0 && value.Block == matrix && value.Matrix is { Length: 15 } values && value.Samples == null && values.All(double.IsFinite);
            bool LutKnown(IntelPixelValue value) => value.Result == 0 && value.Block == lut && value.Matrix == null && value.Samples is { } values && values.Length == lut.Samples * lut.Channels && values.All(double.IsFinite);
            var beforeMatrix = intel.ReadPixel(matrix); var beforeLut = intel.ReadPixel(lut);
            if (!MatrixKnown(beforeMatrix) || !LutKnown(beforeLut))
                throw new IOException("Intel did not return a complete current color configuration; no image settings were changed.");
            var previous = saved;
            bool imageWriteAttempted = false;
            try
            {
                // Scope RESTORE_DEFAULT to these two captured blocks. NumBlocks=0
                // would also reset unrelated blocks and is never a safe fallback.
                imageWriteAttempted = true;
                intel.RestorePixelPair(beforeMatrix, beforeLut);
                var actualMatrix = intel.ReadPixel(matrix); var actualLut = intel.ReadPixel(lut);
                if (!MatrixKnown(actualMatrix) || !LutKnown(actualLut))
                    throw new IOException("Intel did not return a complete color configuration after reset.");
                // Successful reset acknowledgement plus complete reads are not
                // an independent proof of factory values or saved-color equality.
                Save(saved with { Colors = null, MatrixOwned = null }); colorsNeedReapply = false;
            }
            catch (Exception actionError)
            {
                var errors = new List<Exception> { actionError };
                if (imageWriteAttempted)
                {
                    try
                    {
                        intel.WritePixelPair(beforeMatrix, beforeLut);
                        var actualMatrix = intel.ReadPixel(matrix); var actualLut = intel.ReadPixel(lut);
                        if (!MatrixKnown(actualMatrix) || !LutKnown(actualLut) ||
                            actualMatrix.Matrix!.Zip(beforeMatrix.Matrix!, (a, b) => Math.Abs(a - b)).Any(delta => delta > .003) ||
                            actualLut.Samples!.Zip(beforeLut.Samples!, (a, b) => Math.Abs(a - b)).Any(delta => delta > .003))
                            throw new IOException("Intel color reset recovery could not be verified against the captured configuration.");
                    }
                    catch (Exception error) { errors.Add(error); }
                }
                saved = previous;
                if (errors.Count > 1) throw new AggregateException("Intel color reset and recovery failed; actual image state requires verification.", errors);
                throw;
            }
        }
    }
    // Intel's Rec.709 hue/saturation approach. The exact inverse keeps neutral neutral.
    // https://github.com/intel/drivers.gpu.control-library/blob/master/Samples/Color_Samples/Color_Sample_App.cpp
    private static double[] HueSaturation(int hue, int saturation)
    {
        double[] forward = [.2126, .7152, .0722, -.1146, -.3854, .5, .5, -.4542, -.0458];
        double determinant = forward[0] * (forward[4] * forward[8] - forward[5] * forward[7]) - forward[1] * (forward[3] * forward[8] - forward[5] * forward[6]) + forward[2] * (forward[3] * forward[7] - forward[4] * forward[6]);
        double[] inverse = [(forward[4]*forward[8]-forward[5]*forward[7])/determinant,(forward[2]*forward[7]-forward[1]*forward[8])/determinant,(forward[1]*forward[5]-forward[2]*forward[4])/determinant,(forward[5]*forward[6]-forward[3]*forward[8])/determinant,(forward[0]*forward[8]-forward[2]*forward[6])/determinant,(forward[2]*forward[3]-forward[0]*forward[5])/determinant,(forward[3]*forward[7]-forward[4]*forward[6])/determinant,(forward[1]*forward[6]-forward[0]*forward[7])/determinant,(forward[0]*forward[4]-forward[1]*forward[3])/determinant];
        double angle = hue * Math.PI / 180, scale = saturation / 50d, cosine = Math.Cos(angle) * scale, sine = Math.Sin(angle) * scale;
        double[] adjustment = [1, 0, 0, 0, cosine, -sine, 0, sine, cosine];
        double[] Multiply(double[] a, double[] b) { var result = new double[9]; for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++) for (int k = 0; k < 3; k++) result[row * 3 + column] += a[row * 3 + k] * b[k * 3 + column]; return result; }
        var matrix = new double[15]; Multiply(Multiply(inverse, adjustment), forward).CopyTo(matrix, 6);
        return matrix;
    }
    public void Dispose()
    {
        rotation.Dispose();
        intel.Dispose();
    }
}
