using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Bonsai;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    /// <summary>
    /// Produces a video sequence acquired from a UCLA Miniscope V3 using legacy DAQ firmware.
    /// </summary>
    [Description("Produces a video sequence acquired from a UCLA Miniscope V3 using legacy DAQ firmware.")]
    public class LegacyUclaMiniscope : Source<IplImage>
    {
        /// <summary>
        /// Specifies the available frame rate options for the legacy UCLA Miniscope.
        /// </summary>
        public enum FrameRateLegacy
        {
            /// <summary>5 frames per second.</summary>
            Fps5 = 0x11,
            /// <summary>10 frames per second.</summary>
            Fps10 = 0x12,
            /// <summary>15 frames per second.</summary>
            Fps15 = 0x13,
            /// <summary>20 frames per second.</summary>
            Fps20 = 0x14,
            /// <summary>30 frames per second.</summary>
            Fps30 = 0x15,
            /// <summary>60 frames per second.</summary>
            Fps60 = 0x16
        };

        //[TypeConverter(typeof(MiniscopeIndexConverter))]
        /// <summary>
        /// Gets or sets the index of the camera from which to acquire images.
        /// </summary>
        [Description("The index of the camera from which to acquire images.")]
        public int Index { get; set; } = 0;

        /// <summary>
        /// Gets or sets a value indicating whether to activate the hardware frame pulse output.
        /// </summary>
        [Description("Indicates whether to activate the hardware frame pulse output.")]
        public bool RecordingFramePulse { get; set; } = false;

        /// <summary>
        /// Gets or sets the relative LED brightness.
        /// </summary>
        [Range(0, 255)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Relative LED Brightness.")]
        public double LedBrightness { get; set; } = 0;

        /// <summary>
        /// Gets or sets the relative exposure time.
        /// </summary>
        [Range(1, 255)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Relative exposure time.")]
        public double Exposure { get; set; } = 255;

        /// <summary>
        /// Gets or sets the sensor gain.
        /// </summary>
        [Range(16, 64)]
        [Precision(0, 2)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("The sensor gain.")]
        public double SensorGain { get; set; } = 16;

        const int RecordStart = 0x01;
        const int RecordEnd = 0x02;

        /// <summary>
        /// Gets or sets frame rate in Hz.
        /// </summary>
        [Description("Frames per second.")]
        public FrameRateLegacy FramesPerSecond { get; set; } = FrameRateLegacy.Fps30;

        readonly IObservable<IplImage> source;
        readonly object captureLock = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="LegacyUclaMiniscope"/> class.
        /// </summary>
        public LegacyUclaMiniscope()
        {
            source = Observable.Create<IplImage>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(() =>
                {
                    lock (captureLock)
                    {
                        var lastRecordingFramePulse = false;
                        var lastLEDBrightness = LedBrightness;
                        var lastExposure = Exposure;
                        var lastSensorGain = SensorGain;
                        using (var capture = Capture.CreateCameraCapture(Index))
                        {
                            try
                            {
                                capture.SetProperty(CaptureProperty.Saturation, (double)FramesPerSecond);
                                capture.SetProperty(CaptureProperty.Hue, LedBrightness);
                                capture.SetProperty(CaptureProperty.Gain, SensorGain);
                                capture.SetProperty(CaptureProperty.Brightness, Exposure);
                                capture.SetProperty(CaptureProperty.Saturation, RecordEnd);
                                while (!cancellationToken.IsCancellationRequested)
                                {
                                    // Runtime settable properties
                                    if (LedBrightness != lastLEDBrightness)
                                    {
                                        capture.SetProperty(CaptureProperty.Hue, LedBrightness);
                                        lastLEDBrightness = LedBrightness;
                                    }
                                    if (SensorGain != lastSensorGain)
                                    {
                                        capture.SetProperty(CaptureProperty.Gain, SensorGain);
                                        lastSensorGain = SensorGain;
                                    }
                                    if (Exposure != lastExposure)
                                    {
                                        capture.SetProperty(CaptureProperty.Brightness, Exposure);
                                        lastExposure = Exposure;
                                    }
                                    if (RecordingFramePulse != lastRecordingFramePulse)
                                    {
                                        capture.SetProperty(CaptureProperty.Saturation, RecordingFramePulse ? RecordStart : RecordEnd);
                                        lastRecordingFramePulse = RecordingFramePulse;
                                    }

                                    var image = capture.QueryFrame();

                                    if (image == null)
                                    {
                                        observer.OnCompleted();
                                        break;
                                    }
                                    else observer.OnNext(image.Clone());
                                }
                            }
                            finally
                            {
                                capture.Close();
                            }

                        }
                    }
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            })
            .PublishReconnectable()
            .RefCount();
        }

        /// <summary>
        /// Returns the image sequence produced by the connected Miniscope.
        /// </summary>
        /// <returns>A sequence of <see cref="IplImage"/> frames.</returns>
        public override IObservable<IplImage> Generate()
        {
            return source;
        }
    }
}
