using System;
using System.Numerics;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    /// <summary>
    /// Specifies the Miniscope DAQ digital input pins.
    /// </summary>
    [Flags]
    public enum MiniscopeDaqDigitalIn
    {
        /// <summary>
        /// Specifies no digital input pin.
        /// </summary>
        None = 0,
        /// <summary>
        /// Specifies digital input 0.
        /// </summary>
        DigitalIn0 = 1,
        /// <summary>
        /// Specifies digital input 1.
        /// </summary>
        DigitalIn1 = 2,
    }

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
        /// <param name="digitalIn">The digital inputs that are high at the time of capture.</param>
        public UclaMiniscopeV4Frame(IplImage image, Quaternion quaternion, int frameNumber, MiniscopeDaqDigitalIn digitalIn)
        {
            FrameNumber = frameNumber;
            Image = image;
            Quaternion = quaternion;
            DigitalIn = digitalIn;
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
        /// Gets the digital inputs that are high at the time of capture.
        /// </summary>
        public MiniscopeDaqDigitalIn DigitalIn { get; }
    }
}
