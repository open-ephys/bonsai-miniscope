using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Bonsai;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    [Description("Produces a data sequence from a UCLA MiniCAM behavioral monitoring camera.")]
    public class UclaMiniCam : Source<UclaMiniCamFrame>
    {
        // NB: Needs a unique name, even though its a class member, for de/serialization without issues
        public enum GainMiniCam
        {
            Low = 8,
            Medium = 96,
            High = 2144,
            Maximum = 6240,
        };

        // Frame size
        const int Width = 1024;
        const int Height = 768;

        // Percent -> register scale factor for setting illumination LED brightness
        // TODO: Actual value is 0.31. However, if the DAQ is USB powered, using this value
        // causes link instabilities even with a short, high-quality, nominal-gauge SMA cable.
        const double LedBrightnessScaleFactor = 0.26;

        [Editor("OpenEphys.Miniscope.Design.UclaMiniCamIndexEditor, OpenEphys.Miniscope.Design", typeof(UITypeEditor))]
        [Description("The index of the camera from which to acquire images.")]
        public int Index { get; set; } = 0;

        [Range(0, 100)]
        [Precision(1, 0.1)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("LED brightness (percent of max).")]
        public double LedBrightness { get; set; } = 0;

        [Description("The image sensor gain.")]
        public GainMiniCam SensorGain { get; set; } = GainMiniCam.Low;

        [Description("Frames captured per second.")]
        [Range(5, 47)]
        [Editor(DesignTypes.NumericUpDownEditor, typeof(UITypeEditor))]
        public int FramesPerSecond { get; set; } = 30;

        // State
        readonly IObservable<UclaMiniCamFrame> source;
        readonly object captureLock = new object();
        AbusedUvcRegisters originalState;

        // NB: Camera register (ab)uses
        // CaptureProperty.Saturation   -> Start acquisition
        // CaptureProperty.Gamma        -> Inverted state of trigger input (3.3 -> Gamma = 0, 0V -> Gamma != 0)
        // CaptureProperty.Contrast     -> DAQ Frame number

        static internal AbusedUvcRegisters IssueStartCommands(Capture capture)
        {
            // I2C Addresses in various formats
            // ---------------------------------------------
            // 8-bit            7-bit           Description
            // ---------------------------------------------
            // 192 (0xc0)       96 (0x60)       Deserializer
            // 176 (0xb0)       88 (0x58)       Serializer
            // 186 (0xba)       93 (0x5d)       MT9P031 Camera
            // 108 (0x6c)       54 (0x36)       LM3509 LED driver

            var cgs = Helpers.ReadConfigurationRegisters(capture);

            // Magik configuration sequence (configures SERDES and chip default states)
            Helpers.SendConfig(capture, Helpers.CreateCommand(192, 7, 176)); // Provide deserializer with serializer address
            Helpers.SendConfig(capture, Helpers.CreateCommand(192, 34, 2)); // Speed up i2c bus timer to 50us max
            Helpers.SendConfig(capture, Helpers.CreateCommand(192, 32, 10)); // Decrease BCC timeout, units in 2 ms
            Helpers.SendConfig(capture, Helpers.CreateCommand(176, 15, 2)); // Speed up I2c bus timer to 50u Max
            Helpers.SendConfig(capture, Helpers.CreateCommand(176, 30, 10)); // Decrease BCC timeout, units in 2 ms
            Helpers.SendConfig(capture, Helpers.CreateCommand(192, 8, 186, 108)); // Set aliases for MT9P031 and LM3509
            Helpers.SendConfig(capture, Helpers.CreateCommand(192, 16, 186, 108)); // Set aliases for MT9P031 and LM3509
            Helpers.SendConfig(capture, Helpers.CreateCommand(186, 3, 5, 255)); // Set height to 1535 rows
            Helpers.SendConfig(capture, Helpers.CreateCommand(186, 4, 7, 255)); // Set width to 2047 columns
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

            return cgs;
        }

        static internal void IssueStopCommands(Capture capture, AbusedUvcRegisters originalState)
        {
            Helpers.SendConfig(capture, Helpers.CreateCommand(32, 1, 255));
            Helpers.SendConfig(capture, Helpers.CreateCommand(88, 0, 114, 255));
            Helpers.WriteConfigurationRegisters(capture, originalState);
        }

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
                                originalState = IssueStartCommands(capture);

                                while (!cancellationToken.IsCancellationRequested)
                                {
                                    // Get trigger input state
                                    var gate = capture.GetProperty(CaptureProperty.Gamma) != 0;

                                    if (LedBrightness != lastLedBrightness || !initialized)
                                    {
                                        var scaled = LedBrightnessScaleFactor * LedBrightness;
                                        Helpers.SendConfig(capture, Helpers.CreateCommand(108, 160, (byte)scaled));
                                        lastLedBrightness = LedBrightness;
                                    }

                                    if (FramesPerSecond != lastFps || !initialized)
                                    {
                                        // This was found empirically.
                                        var reg = 37000 / FramesPerSecond;
                                        if (reg < 0) reg = 0;

                                        byte vl = (byte)(reg & 0x00000FF);
                                        byte vh = (byte)((reg & 0x000FF00) >> 8);
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
                                IssueStopCommands(capture, originalState);
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
