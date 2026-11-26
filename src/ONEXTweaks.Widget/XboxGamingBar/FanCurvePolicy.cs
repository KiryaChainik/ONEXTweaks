// SPDX-License-Identifier: AGPL-3.0-only
using System;

namespace ONEXTweaks.Fans
{
    internal static class FanCurvePolicy
    {
        internal const int AxisStart = 20;
        internal const int FullSpeedTemperature = 100;
        internal const int AxisMinimumDuty = 15;
        internal const int MaximumDuty = 100;
        private static readonly int[] Temperatures = { 25, 45, 55, 65, 75, 85, 95 };
        private static readonly int[] MinimumDuties = { 15, 20, 20, 20, 20, 20, 50 };

        internal static int Count { get { return Temperatures.Length; } }
        internal static int TemperatureAt(int index) { return Temperatures[index]; }
        internal static int MinimumAt(int index) { return MinimumDuties[index]; }
        internal static int[] TemperaturePoints() { return (int[])Temperatures.Clone(); }

        internal static int[] Expand(int[] points)
        {
            if (points == null || (points.Length != 5 && points.Length != Count))
                throw new ArgumentException("Five legacy or seven current fan points are required.");
            int[] current = points.Length == Count ? (int[])points.Clone() :
                new[] { points[0], points[0], points[1], points[2], points[3], points[4], MaximumDuty };
            Validate(current);
            return current;
        }

        internal static void Validate(int[] points)
        {
            if (points == null || points.Length != Count)
                throw new ArgumentException("Seven fan points are required.");
            for (int i = 0; i < Count; i++)
            {
                if (points[i] < MinimumAt(i) || points[i] > MaximumDuty ||
                    (i > 0 && points[i] < points[i - 1]))
                    throw new ArgumentException("The fan curve must respect each point's minimum and never decrease.");
            }
        }

        internal static int PercentAt(double temperature, int[] points)
        {
            Validate(points);
            if (double.IsNaN(temperature) || double.IsInfinity(temperature) || temperature < 1 || temperature > 110)
                throw new ArgumentException("Fan CPU sensor unavailable.");
            if (temperature >= FullSpeedTemperature) return MaximumDuty;
            if (temperature <= TemperatureAt(0)) return points[0];
            for (int i = 1; i < Count; i++)
            {
                if (temperature <= TemperatureAt(i))
                    return Interpolate(temperature, TemperatureAt(i - 1), points[i - 1], TemperatureAt(i), points[i]);
            }
            return Interpolate(temperature, TemperatureAt(Count - 1), points[Count - 1],
                FullSpeedTemperature, MaximumDuty);
        }

        private static int Interpolate(double temperature, int fromTemperature, int fromDuty, int toTemperature, int toDuty)
        {
            return fromDuty + (int)((temperature - fromTemperature) * (toDuty - fromDuty) /
                (toTemperature - fromTemperature));
        }
    }
}
