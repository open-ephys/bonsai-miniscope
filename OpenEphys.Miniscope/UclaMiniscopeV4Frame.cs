using System.Numerics;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    public class UclaMiniscopeV4Frame
    {
        public UclaMiniscopeV4Frame(IplImage image, Quaternion quaternion, int frameNumber, bool trigger, bool aux)
        {
            FrameNumber = frameNumber;
            Image = image;
            Quaternion = quaternion;
            Trigger = trigger;
            Aux = aux;
        }

        public int FrameNumber { get; }
        public IplImage Image { get; }
        public Quaternion Quaternion { get; }
        public bool Trigger { get; }

        public bool Aux { get; }
    }
}
