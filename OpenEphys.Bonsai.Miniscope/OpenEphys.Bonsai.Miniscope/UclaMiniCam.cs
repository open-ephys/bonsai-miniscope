using Bonsai;
using OpenCV.Net;
using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace OpenEphys.Bonsai.Miniscope
{
    [Description("Produces a data sequence from a UCLA MiniCAM behavioral monitoring camera.")]
    public class UclaMiniCam : Source<UclaMiniCamFrame>
    {
        // NB: Needs a unique name, even though its a class member, for de/serilizaiton without issues
        public enum GainMiniCam
        {
            Low = 8,
            Medium = 96,
            High = 2144,
            Maximum = 6240,
        };

        // NB: Needs a unique name, even though its a class member, for de/serilizaiton without issues
        public enum FrameRateMiniCam
        {
            Fps10 = 2048,
            Fps40 = 1536,
            Fps50 = 6240,
        };

        // Frame size
        const int Width = 1024;
        const int Height = 768;

        // Percent -> register scale factor for setting illumination LED brightness
        // TODO: Actual value is 0.31. However, if the DAQ is USB powered, using this value
        // causes link instabilities even with a short, high-quality, nomimal-gauge SMA cable.
        const double LedBrigthnessScaleFactor = 0.26;

        [Description("The index of the camera from which to acquire images.")]
        public int Index { get; set; } = 0;

        [Range(0, 26)] //
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("LED brightness (percent of max).")]
        public double LedBrightness { get; set; } = 0;

        [Description("The image sensor gain.")]
        public GainMiniCam SensorGain { get; set; } = GainMiniCam.Low;

        [Description("Frames captured per second.")]
        public FrameRateMiniCam FramesPerSecond { get; set; } = FrameRateMiniCam.Fps50;

        // State
        readonly IObservable<UclaMiniCamFrame> source;
        readonly object captureLock = new object();

        // NB: Camera regiser (ab)uses
        // CaptureProperty.Saturation   -> Start acqusition
        // CaptureProperty.Gamma        -> Inverted state of trigger input (3.3 -> Gamma = 0, 0V -> Gamma != 0)
        // CaptureProperty.Contrast     -> DAQ Frame number

        public UclaMiniCam()
        {
            source = Observable.Create<UclaMiniCamFrame>((observer, cancellationToken) =>
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
                                // I2C Addresses in various formats
                                // ---------------------------------------------
                                // 8-bit            7-bit           Description
                                // ---------------------------------------------
                                // 192 (0xc0)       96 (0x60)       Deserializer
                                // 176 (0xb0)       88 (0x58)       Serializer
                                // 186 (0xba)       93 (0x5d)       MT9P031 Camera
                                // 108 (0x6c)       54 (0x36)       LM3509 LED driver

                                // Magik configuration sequence (configures SERDES and chip default states)
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 7, 176)); // Provide deserializwer with serializer address
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 34, 2)); // Speed up i2c bus timer to 50us max
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 32, 10)); // Decrease BCC timeout, units in 2 ms
                                Helpers.SendConfig(capture, Helpers.CreateCommand(176, 15, 2)); // Speed up I2c bus timer to 50u Max
                                Helpers.SendConfig(capture, Helpers.CreateCommand(176, 30, 10)); // Decrease BCC timeout, units in 2 ms
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 8, 186, 108)); // Set aliases for MT9P031 and LM3509
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 16, 186, 108)); // Set aliasesor MT9P031 and LM3509
                                Helpers.SendConfig(capture, Helpers.CreateCommand(186, 3, 5, 255)); // Set height to 1535 rows
                                Helpers.SendConfig(capture, Helpers.CreateCommand(186, 4, 7, 255)); // Set width to 2047 colums
                                Helpers.SendConfig(capture, Helpers.CreateCommand(186, 34, 0, 17)); // 2x subsamp and binning 1
                                Helpers.SendConfig(capture, Helpers.CreateCommand(186, 35, 0, 17)); // 2x subsamp and binning 2
                                Helpers.SendConfig(capture, Helpers.CreateCommand(186, 32, 0, 96)); // Set column binning to summing instead of averaging
                                Helpers.SendConfig(capture, Helpers.CreateCommand(186, 62, 0, 192)); // Set register 0x3e to 0xc0 when sensor gain > 4 (TODO: conditional??)
                                Helpers.SendConfig(capture, Helpers.CreateCommand(186, 9, 2, 255)); // Change shutter width
                                Helpers.SendConfig(capture, Helpers.CreateCommand(108, 16, 215)); // LED Driver LM3509 general configuration

                                // Set frame size
                                capture.SetProperty(CaptureProperty.FrameWidth, Width);
                                capture.SetProperty(CaptureProperty.FrameHeight, Height);

                                // Start the camera
                                capture.SetProperty(CaptureProperty.Saturation, 1);

                                while (!cancellationToken.IsCancellationRequested)
                                {
                                    // Get trigger input state
                                    var gate = capture.GetProperty(CaptureProperty.Gamma) != 0;

                                    if (LedBrightness != lastLedBrightness || !initialized)
                                    {           
                                        var scaled = LedBrigthnessScaleFactor * LedBrightness;
                                        Helpers.SendConfig(capture, Helpers.CreateCommand(108, 160, (byte)scaled));
                                        lastLedBrightness = LedBrightness;
                                    }

                                    if (FramesPerSecond != lastFps || !initialized)
                                    {
                                        byte vl = (byte)((int)FramesPerSecond & 0x00000FF);
                                        byte vh = (byte)(((int)FramesPerSecond & 0x000FF00) >> 8);
                                        Helpers.SendConfig(capture, Helpers.CreateCommand(186, 9, vh, vl));
                                        lastFps = FramesPerSecond;
                                    }

                                    if (SensorGain != lastSensorGain || !initialized)
                                    {
                                        byte vl = (byte)((int)SensorGain & 0x00000FF);
                                        byte vh = (byte)(((int)SensorGain & 0x000FF00) >> 8);
                                        Helpers.SendConfig(capture, Helpers.CreateCommand(186, 53, vh, vl));
                                        lastSensorGain = SensorGain;
                                    }

                                    initialized = true;

                                    // Capture frame
                                    var image = capture.QueryFrame();

                                    // Get latest hardware frame count
                                    var frameNumber = (int)capture.GetProperty(CaptureProperty.Contrast);

                                    if (image == null)
                                    {
                                        observer.OnCompleted();
                                        break;
                                    }
                                    else
                                    {
                                        observer.OnNext(new UclaMiniCamFrame(image.Clone(), frameNumber, gate));
                                    }
                                }
                            }
                            finally
                            {
                                Helpers.SendConfig(capture, Helpers.CreateCommand(32, 1, 255));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(88, 0, 114, 255));
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

        public override IObservable<UclaMiniCamFrame> Generate()
        {
            return source;
        }
    }
}
