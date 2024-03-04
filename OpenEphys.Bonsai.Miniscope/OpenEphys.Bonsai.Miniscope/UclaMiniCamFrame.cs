using OpenCV.Net;

namespace OpenEphys.Bonsai.Miniscope
{
    public class UclaMiniCamFrame
    {
        public UclaMiniCamFrame(IplImage image, int frameNumber, bool trigger)
        {
            FrameNumber = frameNumber;
            Image = image;
            Trigger = trigger;
        }

        public int FrameNumber { get; }
        public IplImage Image { get; }
        public bool Trigger { get; }

    }
}
