using System;

namespace ONEXTweaks.Lighting
{
    internal static class LightingEffects
    {
        internal const int Solid = 0;
        internal const int Breathing = -1;
        internal const int ChromaBreathing = 6;
        internal const int LegacyAokClassic = 14;
        private static readonly int[] Presets = { 13, 3, 11, 5, 7, 9, 12, 2, 8, 1000, ChromaBreathing };
        private const int PreviewPeriodMs = 2600;
        // Representative colors for the on-screen diagram, in menu order.
        private static readonly int[,] Colors =
        {
            { 0xF6A22F, 0xF04852, 0x773DF6 },
            { 0x26C5FA, 0x41E39C, 0x2572FF },
            { 0xF47D2B, 0xFF4674, 0x582DAD },
            { 0xEB38EC, 0x28D2FF, 0x43ED65 },
            { 0xB78AFF, 0x5A93FF, 0xFFA4CB },
            { 0xFF2E9B, 0x2BE8E6, 0x7F37FF },
            { 0xF14A48, 0x4AE56B, 0x468AFF },
            { 0x26E2B0, 0x5499FD, 0xA68DFF },
            { 0xFBD7AA, 0xFFECA4, 0x9CD6FF },
            { 0xF26039, 0x33C3FF, 0x8962F8 },
            { 0xFF4278, 0x32DDF0, 0xA55CFF }
        };

        internal static int[] PresetIds()
        {
            return (int[])Presets.Clone();
        }

        internal static bool SupportsPresets(int zoneId)
        {
            return zoneId == 1 || zoneId == 2 || zoneId == 7;
        }

        internal static bool SupportsFactoryBreathing(int zoneId)
        {
            return zoneId == 5 || zoneId == 6;
        }

        internal static int BreathingMode(int zoneId)
        {
            return SupportsFactoryBreathing(zoneId) ? Breathing : ChromaBreathing;
        }

        internal static bool IsPreset(int effect)
        {
            return effect == LegacyAokClassic || Array.IndexOf(Presets, effect) >= 0;
        }

        internal static int PreviewColor(int effect, int rgb, long elapsedMs)
        {
            if (effect == Solid) return rgb;

            double phase = (elapsedMs % PreviewPeriodMs) / (double)PreviewPeriodMs;
            if (effect == Breathing)
            {
                double scale = .15 + .85 * (.5 + .5 * Math.Cos(phase * 2 * Math.PI));
                return Scale(rgb, scale);
            }

            int slot = Array.IndexOf(Presets, effect == LegacyAokClassic ? 1000 : effect);
            if (slot < 0) return rgb;
            double position = phase * 3;
            int segment = (int)position;
            int preview = Interpolate(Colors[slot, segment], Colors[slot, (segment + 1) % 3], position - segment);
            return effect == ChromaBreathing ? Scale(preview, .15 + .85 * (.5 + .5 * Math.Cos(phase * 2 * Math.PI))) : preview;
        }

        private static int Scale(int rgb, double scale)
        {
            return Channel(rgb, 16, scale) << 16 | Channel(rgb, 8, scale) << 8 | Channel(rgb, 0, scale);
        }

        private static int Channel(int rgb, int shift, double scale)
        {
            return (int)Math.Round((rgb >> shift & 255) * scale);
        }

        private static int Interpolate(int from, int to, double amount)
        {
            return MixChannel(from, to, 16, amount) << 16 |
                MixChannel(from, to, 8, amount) << 8 | MixChannel(from, to, 0, amount);
        }

        private static int MixChannel(int from, int to, int shift, double amount)
        {
            int a = from >> shift & 255;
            int b = to >> shift & 255;
            return (int)Math.Round(a + (b - a) * amount);
        }
    }
}
