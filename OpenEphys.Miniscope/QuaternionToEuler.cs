using Bonsai;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive.Linq;

namespace OpenEphys.Miniscope
{
    [Description("Transforms a quaternion into axis angle representation using the Tait-Bryan formalism. " +
        "Starting with ENU coordinates, Yaw represents the bearing, which is the rotation about Z/Up. " +
        "Pitch represents the elevation, which is the rotation about Y' after application of Yaw to the ENU coordinates. " +
        "Roll gives the bank angle, which is the rotation about X'' after application of Yaw and Pitch to the " +
        "the ENU coordinates.")]
    public class QuaternionToEuler : Transform<Quaternion, TaitBryanAngles>
    {
        public override IObservable<TaitBryanAngles> Process(IObservable<Quaternion> source)
        {
            // These axis definitions match those for the Euler angles in registers 0x1A through 0x1F
            return source.Select(q =>
            {
                var sinrCosp = 2 * (q.W * q.X + q.Y * q.Z);
                var cosrCosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
                var roll = MathF.Atan2(sinrCosp, cosrCosp) * 180f / MathF.PI; ;

                var sinp = MathF.Sqrt(1 + 2 * (q.W * q.Y - q.X * q.Z));
                var cosp = MathF.Sqrt(1 - 2 * (q.W * q.Y - q.X * q.Z));
                var pitch = (2 * MathF.Atan2(sinp, cosp) - MathF.PI / 2) * 180f / MathF.PI; ;

                var sinyCosp = 2 * (q.W * q.Z + q.X * q.Y);
                var cosyCosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
                var yaw = (MathF.Atan2(sinyCosp, cosyCosp) + MathF.PI) * 180f / MathF.PI;

                return new TaitBryanAngles(yaw, pitch, roll);
            });
        }
    }
}
