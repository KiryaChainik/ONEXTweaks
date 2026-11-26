// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using ONEXTweaks.Power;

namespace ONEXTweaks.WidgetBridge;

internal static class Pl2RangeVerification
{
    internal static async Task<int> Run(string report)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "qa-pl2-" + Guid.NewGuid().ToString("N"));
        string reports = Directory.CreateDirectory(Path.Combine(root, "Reports")).FullName;
        string file = Path.Combine(root, "performance-profiles.json");
        var low = new ProfileValues(14, 15, 0, 0, 0, 1, new("RTSS", false, 60), 0, 0, false);
        var factoryProfileCopy = low with { Pl1 = 25, Pl2 = Pl2ControlPolicy.Automatic(25) };
        var high = low with { Pl1 = 40, Pl2 = 52, SeparatePl2 = true };
        var live = (Pl1: 25, Pl2: 52);
        bool failAfterPowerWrite = false;
        int applies = 0;
        int factoryRestores = 0;
        var checks = new List<string>();
        string? error = null;
        PerformanceProfiles? manager = null;

        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            checks.Add(message);
        }

        void Apply(ProfileValues values)
        {
            values.Validate();
            applies++;
            live = (values.Pl1, values.EffectivePl2);
            if (failAfterPowerWrite)
            {
                failAfterPowerWrite = false;
                throw new IOException("Injected failure after changing limits");
            }
        }

        PerformanceTransitionSnapshot Capture()
        {
            var original = live;
            if (!Pl2ControlPolicy.IsFactoryReadback(original.Pl1, original.Pl2))
                return new(low with { Pl1 = original.Pl1, Pl2 = original.Pl2, SeparatePl2 = true });
            return new(factoryProfileCopy, () =>
            {
                factoryRestores++;
                live = original;
                return Task.CompletedTask;
            });
        }

        async Task Select(string id)
        {
            using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { operation = "select", id }));
            await manager!.Mutate(request.RootElement);
        }

        try
        {
            Check(Enumerable.Range(8, 33).All(w => Pl2ControlPolicy.Automatic(w) == w + 1), "Off always means PL1 + 1");
            Check(Pl2ControlPolicy.IsWritable(40, 52) && Pl2ControlPolicy.ManualCeiling(40) == 52, "Manual 40/52 is valid");
            Check(!Pl2ControlPolicy.IsWritable(40, 53) && !Pl2ControlPolicy.IsWritable(39, 52) &&
                !Pl2ControlPolicy.IsWritable(25, 52) && !Pl2ControlPolicy.IsWritable(39.5, 41), "Invalid writes and fractional baselines are rejected");
            Check(Pl2ControlPolicy.AdjustManual(39, 52) == 40 && Pl2ControlPolicy.AdjustManual(30, 52) == 31 &&
                Pl2ControlPolicy.AdjustManual(25, 35) == 35, "Moving off 40 normalizes the extended limit and preserves standard manual behavior");

            high.Validate();
            var off = high with { SeparatePl2 = false };
            off.Validate();
            Check(off.EffectivePl2 == 41 && off.Pl2 == 52, "40 W with Separate PL2 off applies 41 W and remembers 52 W");
            using (var down = JsonDocument.Parse("39"))
            {
                var reduced = ProfileSourcePatch.Apply(high, "pl1", down.RootElement);
                Check(reduced.Pl1 == 39 && reduced.EffectivePl2 == 40, "Reducing PL1 in a saved source removes the extended target");
            }
            bool rejected = false;
            try { (low with { Pl1 = 25, Pl2 = 52, SeparatePl2 = true }).Validate(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && Pl2ControlPolicy.IsFactoryReadback(live.Pl1, live.Pl2), "Factory 25/52 is readback only");

            var global = new ProfileEntry("global", "Global", "", false, false, low, low, DateTimeOffset.UnixEpoch, false);
            var turbo = new ProfileEntry("turbo", "Turbo", "", false, false, high, high, DateTimeOffset.UnixEpoch, false);
            var original = new ProfileDocument(7, global, [turbo], PerformanceProfiles.Groups.ToDictionary(x => x, _ => true), new("global", "manual"));
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(original, Backend.Program.Json));
            manager = await PerformanceProfiles.Create(reports,
                () => Task.FromResult(factoryProfileCopy),
                values => { Apply(values); return Task.CompletedTask; },
                () => null, () => false,
                new(() => null, _ => false, () => [], CaptureTransition: () => Task.FromResult(Capture())),
                startWorker: false);
            string before = await File.ReadAllTextAsync(file);
            failAfterPowerWrite = true;
            rejected = false;
            try { await Select("turbo"); }
            catch (IOException) { rejected = true; }
            Check(rejected && applies == 1 && factoryRestores == 1 && live == (25, 52) && before == await File.ReadAllTextAsync(file),
                "Failed change restores exact factory limits and leaves the profile file intact");

            await Select("global");
            Check(live == (14, 15) && factoryRestores == 1, "The saved profile applies from factory 25/52");
            await Select("turbo");
            Check(live == (40, 52), "The manual 40/52 profile loads and applies");
            await Select("global");
            var saved = JsonSerializer.Deserialize<ProfileDocument>(await File.ReadAllTextAsync(file), Backend.Program.Json)!;
            Check(live == (14, 15) && saved.Games.Single().Dc == high, "Switching away does not overwrite the manual 40/52 profile");

            var preferences = new PowerPreferenceStore(Path.Combine(root, "power-preferences.json"));
            preferences.Save(40, 41, false, 52);
            Check(preferences.Load() is { SeparatePl2: false, Pl2Watts: 41, ManualPl2Watts: 52 }, "An explicit 52 W setting survives the Off toggle");
            var prior = preferences.Load();
            preferences.Save(40, 52, true, 52);
            preferences.RestoreAfterFactoryRollback(prior, preferences.Load());
            Check(preferences.Load() == prior, "Factory rollback restores the previous preference document");
        }
        catch (Exception failure) { error = failure.ToString(); }
        finally { if (manager != null) await manager.DisposeAsync(); }

        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed = error == null, checks, error, hardwareTested = false }, Backend.Program.Json));
        return error == null ? 0 : 1;
    }
}
