using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Bonsai;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    [Description("Produces a image sequence acquired from a UCLA Miniscope V3.")]
    public class UclaMiniscopeV3 : Source<IplImage>
    {
        // NB: Needs a unique name, even though its a class member, for de/serilizaiton without issues
        public enum GainV3
        {
            Low = 225,
            Medium = 228,
            High = 36
        };

        // NB: Needs a unique name, even though its a class member, for de/serilizaiton without issues
        public enum FrameRateV3
        {
            Fps10,
            Fps30,
            Fps60
        };

        // Frame size
        const int Width = 752;
        const int Height = 480;

        [TypeConverter(typeof(MiniscopeIndexConverter))]
        [Description("The index of the camera from which to acquire images.")]
        public int Index { get; set; } = 0;

        [Range(0, 100)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Excitation LED brightness (percent of max).")]
        public double LedBrightness { get; set; } = 0;

        [Description("The image sensor gain.")]
        public GainV3 SensorGain { get; set; } = GainV3.Low;

        [Description("Frames captured per second.")]
        public FrameRateV3 FramesPerSecond { get; set; } = FrameRateV3.Fps30;

        // State
        readonly IObservable<IplImage> source;
        readonly object captureLock = new object();

        public UclaMiniscopeV3()
        {
            source = Observable.Create<IplImage>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(() =>
                {
                    lock (captureLock)
                    {
                        bool initialized = false;
                        var lastLedBrightness = LedBrightness;
                        var lastFps = FramesPerSecond;
                        var lastSensorGain = SensorGain;

                        using (var capture = Capture.CreateCameraCapture(Index))
                        {
                            try
                            {
                                // Magik configuration sequence (configures SERDES and chip default states)
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 31, 16));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(176, 5, 32));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 34, 2));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 32, 10));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 7, 176));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(176, 15, 2));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(176, 30, 10));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 8, 184, 152));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 16, 184, 152));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 12, 0, 1));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 175, 0, 0));

                                // Set frame size
                                capture.SetProperty(CaptureProperty.FrameWidth, Width);
                                capture.SetProperty(CaptureProperty.FrameHeight, Height);

                                // Start the camera
                                capture.SetProperty(CaptureProperty.Saturation, 1);

                                while (!cancellationToken.IsCancellationRequested)
                                {
                                    // Runtime settable properties
                                    if (LedBrightness != lastLedBrightness || !initialized)
                                    {
                                        var scaled = 40.80 * LedBrightness;
                                        Helpers.SendConfig(capture, Helpers.CreateCommand(152, (byte)((int)scaled >> 8), (byte)((int)scaled & 0xFF)));
                                        lastLedBrightness = LedBrightness;
                                    }
                                    if (FramesPerSecond != lastFps || !initialized)
                                    {
                                        switch (FramesPerSecond)
                                        {
                                            case FrameRateV3.Fps10:
                                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 5, 2, 238, 4, 226));
                                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 11, 6, 184));
                                                break;
                                            case FrameRateV3.Fps30:
                                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 5, 0, 94, 2, 33));
                                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 11, 3, 232));
                                                break;
                                            case FrameRateV3.Fps60:
                                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 5, 0, 93, 0, 33));
                                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 11, 1, 244));
                                                break;
                                        }
                                        lastFps = FramesPerSecond;
                                    }
                                    if (SensorGain != lastSensorGain || !initialized)
                                    {
                                        switch (SensorGain)
                                        {
                                            case GainV3.Low:
                                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 53, 0, 16));
                                                break;
                                            case GainV3.Medium:
                                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 53, 0, 32));
                                                break;
                                            case GainV3.High:
                                                Helpers.SendConfig(capture, Helpers.CreateCommand(184, 53, 0, 64));
                                                break;
                                        }
                                        lastSensorGain = SensorGain;
                                    }

                                    initialized = true;

                                    // Capture frame
                                    var image = capture.QueryFrame();

                                    if (image == null)
                                    {
                                        observer.OnCompleted();
                                        break;
                                    }
                                    else
                                    {
                                        observer.OnNext(image.Clone());
                                    }
                                }
                            }
                            finally
                            {
                                Helpers.SendConfig(capture, Helpers.CreateCommand(152, 0, 0));
                                capture.SetProperty(CaptureProperty.Saturation, 0);
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
