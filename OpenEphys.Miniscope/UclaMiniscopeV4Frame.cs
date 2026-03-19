using System.Numerics;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    /// <summary>
    /// Represents a single frame captured from a UCLA Miniscope V4, including image data,
    /// head-orientation quaternion, and digital I/O state.
    /// </summary>
    public class UclaMiniscopeV4Frame
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UclaMiniscopeV4Frame"/> class.
        /// </summary>
        /// <param name="image">The captured image.</param>
        /// <param name="quaternion">The head-orientation quaternion from the onboard IMU.</param>
        /// <param name="frameNumber">The hardware frame counter value at the time of capture.</param>
        /// <param name="trigger">The state of the trigger input at the time of capture.</param>
        /// <param name="aux">The state of the auxiliary digital input at the time of capture.</param>
        public UclaMiniscopeV4Frame(IplImage image, Quaternion quaternion, int frameNumber, bool trigger, bool aux)
        {
            FrameNumber = frameNumber;
            Image = image;
            Quaternion = quaternion;
            Trigger = trigger;
            Aux = aux;
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
        /// Gets the head-orientation quaternion from the onboard IMU at the time of capture.
        /// </summary>
        public Quaternion Quaternion { get; }

        /// <summary>
        /// Gets the state of the trigger input at the time of capture.
        /// </summary>
        public bool Trigger { get; }

        /// <summary>
        /// Gets the state of the auxiliary digital input at the time of capture.
        /// </summary>
        public bool Aux { get; }
    }
}
