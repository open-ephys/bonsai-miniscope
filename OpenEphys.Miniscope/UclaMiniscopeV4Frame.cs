using System.Numerics;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    public class UclaMiniscopeV4Frame
    {
        public UclaMiniscopeV4Frame(IplImage image, Quaternion quaternion, int frameNumber, bool trigger)
        {
            FrameNumber = frameNumber;
            Image = image;
            Quaternion = quaternion;
            Trigger = trigger;
        }

        public int FrameNumber { get; }
        public IplImage Image { get; }
        public Quaternion Quaternion { get; }
        public bool Trigger { get; }
    }
}
