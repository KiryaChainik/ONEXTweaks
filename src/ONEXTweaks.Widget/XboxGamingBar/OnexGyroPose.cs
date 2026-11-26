// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Numerics;

namespace XboxGamingBar
{
    // The mesh and the ONEXPLAYER 3 accelerometer share a right-handed chassis
    // frame: X points right, Y up, and Z out of the glass.
    internal sealed class OnexGyroPose
    {
        private const double MaxGap = .15;
        private const double DegreesToRadians = Math.PI / 180;
        private Vector3? gravityReference;
        private double? packetTime, rateTime;
        private long lastSequence = -1;
        private Vector3 previousRate;

        internal Quaternion Orientation { get; private set; } = Quaternion.Identity;

        internal void Recenter()
        {
            Orientation = Quaternion.Identity;
            gravityReference = null;
            Pause();
        }

        internal void Pause()
        {
            packetTime = rateTime = null;
            lastSequence = -1;
            previousRate = Vector3.Zero;
        }

        internal void Observe(Vector3? acceleration, Vector3 sensorRate, long sequence, double ageMs, double now)
        {
            double packetInterval = packetTime.HasValue ? now - packetTime.Value : 0;
            packetTime = now;

            // Each sensor event contributes at most once. The diagnostic RPC may
            // publish the same gyroscope sample several times after movement stops.
            if (sequence >= 0 && sequence != lastSequence && ageMs >= 0 && ageMs <= MaxGap * 1000)
            {
                // On this handheld the gyrometer and accelerometer have
                // different axes. Upper edge raised is -gyroY, right edge
                // raised is +gyroX, and face-up clockwise is +gyroZ.
                var angularRate = new Vector3(-sensorRate.Y, -sensorRate.X, -sensorRate.Z);
                double sampledAt = now - ageMs / 1000;
                double interval = rateTime.HasValue ? sampledAt - rateTime.Value : 0;
                if (packetInterval > 0 && packetInterval <= MaxGap)
                {
                    if (interval > 0 && interval <= MaxGap && sequence > lastSequence)
                        Integrate((previousRate + angularRate) * .5f, interval);
                    else if (!rateTime.HasValue || interval > MaxGap)
                        Integrate(angularRate, Math.Min(packetInterval, .025));
                }
                lastSequence = sequence;
                rateTime = sampledAt;
                previousRate = angularRate;
            }
            if (acceleration.HasValue) CorrectGravity(acceleration.Value, packetInterval);
        }

        internal void Integrate(Vector3 degreesPerSecond, double seconds)
            => Rotate(degreesPerSecond * (float)DegreesToRadians, seconds);

        internal void CorrectGravity(Vector3 acceleration, double seconds)
        {
            float length = acceleration.Length();
            if (!(length > .8f && length < 1.2f)) return;

            Vector3 measured = acceleration / length;
            if (!gravityReference.HasValue)
            {
                gravityReference = Vector3.Transform(measured, Orientation);
                return;
            }

            // Gravity constrains tilt, while rotation about gravity is free.
            Vector3 predicted = Vector3.Transform(gravityReference.Value, Quaternion.Conjugate(Orientation));
            float confidence = 1 - Math.Abs(length - 1) / .2f;
            Rotate(Vector3.Cross(measured, predicted) * (.8f * confidence), seconds);
        }

        private void Rotate(Vector3 radiansPerSecond, double seconds)
        {
            if (!(seconds > 0 && seconds <= MaxGap)) return;
            float speed = radiansPerSecond.Length();
            if (!(speed > 1e-7f) || float.IsInfinity(speed)) return;
            var delta = Quaternion.CreateFromAxisAngle(radiansPerSecond / speed, speed * (float)seconds);
            Orientation = Quaternion.Normalize(Orientation * delta);
        }

        // Components of the shortest relative rotation. For one-axis motion
        // the other two stay zero, including as Y passes a quarter turn.
        internal Vector3 RotationDegrees
        {
            get
            {
                Quaternion q = Orientation;
                if (q.W < 0) q = Quaternion.Negate(q);
                var axis = new Vector3(q.X, q.Y, q.Z);
                float length = axis.Length();
                if (length < 1e-7f) return Vector3.Zero;
                double degrees = 2 * Math.Atan2(length, q.W) / DegreesToRadians;
                return axis * (float)(degrees / length);
            }
        }
    }
}
