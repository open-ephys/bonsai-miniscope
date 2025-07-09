using Bonsai;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive.Linq;

namespace OpenEphys.Miniscope
{
    [Description("Transforms a quaternion into axis angle representation. " +
        "Yaw: rotation about Y axis, Pitch: rotation about Z axis, Roll: rotation about X axis.")]
    public class QuaternionToEuler : Transform<Quaternion, Vector3>
    {
        public override IObservable<Vector3> Process(IObservable<Quaternion> source)
        {
            // These axis definitions match those for the Euler angles in registers 0x1A through 0x1F
            return source.Select(q =>
            {
                var yaw = MathF.Atan2(2.0f * (q.Y * q.W + q.Z * q.X), 1.0f - 2.0f * (q.Z * q.Z + q.Y * q.Y)); // Rotation about Y axis
                var pitch = MathF.Asin(2.0f * (q.Z * q.W - q.Y * q.X)); // Rotation about Z axis
                var roll = MathF.Atan2(2.0f * (q.Z * q.Y + q.X * q.W), 1.0f - 2.0f * (q.Z * q.Z + q.X * q.X)); // Rotation about X axis

                return new Vector3(yaw, pitch, roll);
            });
        }
    }
}
