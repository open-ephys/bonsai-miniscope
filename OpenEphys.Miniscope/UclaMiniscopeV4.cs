using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Numerics;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Bonsai;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    [Description("Produces a data sequence from a UCLA Miniscope V4.")]
    public class UclaMiniscopeV4 : Source<UclaMiniscopeV4Frame>
    {
        // NB: Needs a unique name, even though its a class member, for de/serilizaiton without issues
        public enum GainV4
        {
            Low = 225,
            Medium = 228,
            High = 36,
        };

        // NB: Needs a unique name, even though its a class member, for de/serilizaiton without issues
        public enum FrameRateV4
        {
            Fps10 = 39 & 0x000000FF | 16 << 8,
            Fps15 = 26 & 0x000000FF | 11 << 8,
            Fps20 = 19 & 0x000000FF | 136 << 8,
            Fps25 = 15 & 0x000000FF | 160 << 8,
            Fps30 = 12 & 0x000000FF | 228 << 8,
        };

        // Frame size
        const int Width = 608;
        const int Height = 608;

        // 1 quaternion = 2^14 bits
        const float QuatConvFactor = 1.0f / (1 << 14);

        [TypeConverter(typeof(MiniscopeIndexConverter))]
        [Description("The index of the camera from which to acquire images.")]
        public int Index { get; set; } = 0;

        [Range(0, 100)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Excitation LED brightness (percent of max).")]
        public double LedBrightness { get; set; } = 0;

        [Range(-100, 100)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Electro-wetting lens focal plane adjustment (percent of range around nominal).")]
        public double Focus { get; set; } = 0;

        [Description("Image sensor gain setting.")]
        public GainV4 SensorGain { get; set; } = GainV4.Low;

        [Description("Frames captured per second.")]
        public FrameRateV4 FramesPerSecond { get; set; } = FrameRateV4.Fps30;

        // TODO: Does not work with DAQ for some reason
        //[Description("Only turn on excitation LED during camera exposures.")]
        //public bool InterleaveLed { get; set; } = false;

        [Description("Turn off the LED when the trigger input is low. " +
            "Note that this pin is low by default. Therefore, if it is not driven and " +
            "this option is set to true, the LED will not turn on.")]
        public bool LedRespectsTrigger { get; set; } = false;

        // State
        readonly IObservable<UclaMiniscopeV4Frame> source;
        readonly object captureLock = new object();


        // NB: Camera regiser (ab)uses
        // CaptureProperty.Saturation   -> Quaternion W and start acquisition
        // CaptureProperty.Hue          -> Quaternion X
        // CaptureProperty.Gain         -> Quaternion Y
        // CaptureProperty.Brightness   -> Quaternion Z
        // CaptureProperty.Gamma        -> Inverted state of Trigger Input (3.3 -> Gamma = 0, 0V -> Gamma != 0)
        // CaptureProperty.Contrast     -> DAQ Frame number

        public UclaMiniscopeV4()
        {
            source = Observable.Create<UclaMiniscopeV4Frame>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(() =>
                {
                    lock (captureLock)
                    {
                        bool initialized = false;
                        var lastLedBrightness = LedBrightness;
                        var lastEWL = Focus;
                        var lastFps = FramesPerSecond;
                        var lastSensorGain = SensorGain;
                        // var lastInterleaveLed = InterleaveLed;

                        using (var capture = Capture.CreateCameraCapture(Index))
                        {
                            try
                            {
                                // Magik configuration sequence (configures SERDES and chip default states)
                                // 8-bit            7-bit           Description
                                // ---------------------------------------------
                                // 192 (0xc0)       96 (0x60)       Deserializer
                                // 176 (0xb0)       88 (0x58)       Serializer
                                // 160 (0xa0)       80 (0x50)       TPL0102 Digital potentiometer
                                // 80 (0x50)        40 (0x28)       BNO055
                                // 254 (0xfe)       127 (0x7F)      ??
                                // 238 (0xee)       119 (0x77)      MAX14574 EWL driver
                                // 32 (0x20)        16 (0x10)       ATTINY MCU

                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 31, 16)); // I2C: 0x60
                                Helpers.SendConfig(capture, Helpers.CreateCommand(176, 5, 32)); // I2C:0x58
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 34, 2));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 32, 10));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 7, 176));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(176, 15, 2));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(176, 30, 10));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 8, 32, 238, 160, 80));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(192, 16, 32, 238, 88, 80));
                                Helpers.SendConfig(capture, Helpers.CreateCommand(80, 65, 6, 7)); // BNO Axis mapping and sign
                                Helpers.SendConfig(capture, Helpers.CreateCommand(80, 61, 12)); // BNO operation mode is NDOF
                                Helpers.SendConfig(capture, Helpers.CreateCommand(254, 0)); // 0x7F
                                Helpers.SendConfig(capture, Helpers.CreateCommand(238, 3, 3)); // 0x77

                                // Set frame size
                                capture.SetProperty(CaptureProperty.FrameWidth, Width);
                                capture.SetProperty(CaptureProperty.FrameHeight, Height);

                                // Start the camera
                                capture.SetProperty(CaptureProperty.Saturation, 1);

                                while (!cancellationToken.IsCancellationRequested)
                                {
                                    // Get trigger input state
                                    var gate = capture.GetProperty(CaptureProperty.Gamma) != 0;

                                    if (LedRespectsTrigger)
                                    {
                                        if (!gate && lastLedBrightness != 0)
                                        {
                                            Helpers.SendConfig(capture, Helpers.CreateCommand(32, 1, 255));
                                            Helpers.SendConfig(capture, Helpers.CreateCommand(88, 0, 114, 255));
                                            lastLedBrightness = 0;
                                        }
                                        else if (gate && LedBrightness != lastLedBrightness || !initialized)
                                        {
                                            var scaled = 2.55 * LedBrightness;
                                            Helpers.SendConfig(capture, Helpers.CreateCommand(32, 1, (byte)(255 - scaled)));
                                            Helpers.SendConfig(capture, Helpers.CreateCommand(88, 0, 114, (byte)(255 - scaled)));
                                            lastLedBrightness = LedBrightness;
                                        }
                                    }
                                    else
                                    {
                                        if (LedBrightness != lastLedBrightness || !initialized)
                                        {
                                            var scaled = 2.55 * LedBrightness;
                                            Helpers.SendConfig(capture, Helpers.CreateCommand(32, 1, (byte)(255 - scaled)));
                                            Helpers.SendConfig(capture, Helpers.CreateCommand(88, 0, 114, (byte)(255 - scaled)));
                                            lastLedBrightness = LedBrightness;
                                        }
                                    }

                                    if (Focus != lastEWL || !initialized)
                                    {
                                        var scaled = Focus * 1.27;
                                        Helpers.SendConfig(capture, Helpers.CreateCommand(238, 8, (byte)(127 + scaled), 2));
                                        lastEWL = Focus;
                                    }

                                    if (FramesPerSecond != lastFps || !initialized)
                                    {
                                        byte v0 = (byte)((int)FramesPerSecond & 0x00000FF);
                                        byte v1 = (byte)(((int)FramesPerSecond & 0x000FF00) >> 8);
                                        Helpers.SendConfig(capture, Helpers.CreateCommand(32, 5, 0, 201, v0, v1));
                                        lastFps = FramesPerSecond;
                                    }

                                    if (SensorGain != lastSensorGain || !initialized)
                                    {
                                        Helpers.SendConfig(capture, Helpers.CreateCommand(32, 5, 0, 204, 0, (byte)SensorGain));
                                        lastSensorGain = SensorGain;
                                    }

                                    //if (InterleaveLed != lastInterleaveLed || !initialized)
                                    //{
                                    //    Helpers.SendConfig(capture, Helpers.CreateCommand(32, 4, (byte)(InterleaveLed ? 0x00 : 0x03)));
                                    //    lastInterleaveLed = InterleaveLed;
                                    //}

                                    initialized = true;

                                    // Capture frame
                                    var image = capture.QueryFrame();

                                    // Get latest hardware frame count
                                    var frameNumber = (int)capture.GetProperty(CaptureProperty.Contrast);

                                    // Get BNO data
                                    var q = new Quaternion
                                    {
                                        W = QuatConvFactor * (float)capture.GetProperty(CaptureProperty.Saturation),
                                        X = QuatConvFactor * (float)capture.GetProperty(CaptureProperty.Hue),
                                        Y = QuatConvFactor * (float)capture.GetProperty(CaptureProperty.Gain),
                                        Z = QuatConvFactor * (float)capture.GetProperty(CaptureProperty.Brightness)
                                    };

                                    if (image == null)
                                    {
                                        observer.OnCompleted();
                                        break;
                                    }
                                    else
                                    {
                                        observer.OnNext(new UclaMiniscopeV4Frame(image.Clone(), q, frameNumber, gate));
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

        public override IObservable<UclaMiniscopeV4Frame> Generate()
        {
            return source;
        }
    }
}
