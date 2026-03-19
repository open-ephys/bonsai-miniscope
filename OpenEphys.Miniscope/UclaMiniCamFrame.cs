using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    /// <summary>
    /// Represents a single frame captured from a UCLA MiniCAM behavioral monitoring camera.
    /// </summary>
    public class UclaMiniCamFrame
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UclaMiniCamFrame"/> class.
        /// </summary>
        /// <param name="image">The captured image.</param>
        /// <param name="frameNumber">The hardware frame counter value at the time of capture.</param>
        /// <param name="trigger">The state of the trigger input at the time of capture.</param>
        public UclaMiniCamFrame(IplImage image, int frameNumber, bool trigger)
        {
            FrameNumber = frameNumber;
            Image = image;
            Trigger = trigger;
        }

        /// <summary>
        /// Gets the hardware frame counter value at the time of capture.
        /// </summary>
        public int FrameNumber { get; }

        /// <summary>
        /// Gets the captured image.
        /// </summary>
        public IplImage Image { get; }

        /// <summary>
        /// Gets the state of the trigger input at the time of capture.
        /// </summary>
        public bool Trigger { get; }

    }
}
