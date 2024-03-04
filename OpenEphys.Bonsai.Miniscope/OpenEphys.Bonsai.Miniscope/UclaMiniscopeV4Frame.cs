using OpenCV.Net;
using System.Numerics;

namespace OpenEphys.Bonsai.Miniscope
{
    public class UclaMiniscopeV4Frame
    {
        public UclaMiniscopeV4Frame(IplImage image, Vector4 quaternion, int frameNumber, bool trigger)
        {
            FrameNumber = frameNumber;
            Image = image;
            Quaternion = quaternion;
            Trigger = trigger;
        }

        public int FrameNumber { get; }
        public IplImage Image { get; }
        public Vector4 Quaternion { get; }
        public bool Trigger { get; }
    }
}
