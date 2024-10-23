using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Bonsai;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    [Description("Produces a video sequence acquired from a UCLA Miniscope V3 using legacy DAQ firmware.")]
    public class LegacyUclaMiniscope : Source<IplImage>
    {

        // NB: Needs a unique name, even though its a class member, for de/serilizaiton without issues
        public enum FrameRateLegacy
        {
            Fps5 = 0x11,
            Fps10 = 0x12,
            Fps15 = 0x13,
            Fps20 = 0x14,
            Fps30 = 0x15,
            Fps60 = 0x16
        };

        [TypeConverter(typeof(MiniscopeIndexConverter))]
        [Description("The index of the camera from which to acquire images.")]
        public int Index { get; set; } = 0;

        [Description("Indicates whether to activate the hardware frame pulse output.")]
        public bool RecordingFramePulse { get; set; } = false;

        [Range(0, 255)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Relative LED Brightness.")]
        public double LedBrightness { get; set; } = 0;

        [Range(1, 255)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Relative exposure time.")]
        public double Exposure { get; set; } = 255;

        [Range(16, 64)]
        [Precision(0, 2)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("The sensor gain.")]
        public double SensorGain { get; set; } = 16;

        const int RecordStart = 0x01;
        const int RecordEnd = 0x02;

        [Description("Frames per second.")]
        public FrameRateLegacy FramesPerSecond { get; set; } = FrameRateLegacy.Fps30;

        // State
        readonly IObservable<IplImage> source;
        readonly object captureLock = new object();

        // Functor
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
                        using (var capture = Capture.CreateCameraCapture(Index, CaptureDomain.DirectShow))
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

        public override IObservable<IplImage> Generate()
        {
            return source;
        }
    }
}
