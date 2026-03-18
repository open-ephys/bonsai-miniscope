using System;
using System.Buffers;
using System.ComponentModel;
using System.Drawing.Design;
using System.IO;
using System.Numerics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Bonsai;
using OpenCV.Net;
using System.Collections;
using System.Collections.Generic;
using Bonsai.Reactive;
using Bonsai.Expressions;

namespace OpenEphys.Miniscope
{
    [Description("Produces a data sequence from a UCLA Miniscope V4.")]
    public class UclaMiniscopeV4 : Source<UclaMiniscopeV4Frame>
    {
        // Frame size
        const int Width = 608;
        const int Height = 608;

        // 1 quaternion = 2^14 bits
        const float QuatConvFactor = 1.0f / (1 << 14);

        [TypeConverter(typeof(MiniscopeV4IndexTypeConverter))]
        [Description("The index of the camera from which to acquire images.")]
        public int Index { get; set; } = 0;

        [Precision(1, 0.1)]
        [Range(0, 100)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Excitation LED brightness (percent of max).")]
        public double LedBrightness { get; set; } = 0;

        [Precision(1, 0.1)]
        [Range(-100, 100)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Electro-wetting lens focal plane adjustment (percent of range around nominal).")]
        public double Focus { get; set; } = 0;

        [TypeConverter(typeof(GainV4TypeConverter))]
        [Description("Image sensor gain setting.")]
        public GainV4 SensorGain { get; set; } = GainV4.Low;

        [TypeConverter(typeof(FrameRateV4TypeConverter))]
        [Description("Frames captured per second.")]
        public FrameRateV4 FramesPerSecond { get; set; } = FrameRateV4.Fps30;

        [Description("Firmware version")]
        [XmlIgnore]
        public Version FirmwareVersion { get; private set; } = new Version(0,0,0);

        // TODO: Does not work with DAQ for some reason
        //[Description("Only turn on excitation LED during camera exposures.")]
        //public bool InterleaveLed { get; set; } = false;

        [Description("Turn off the LED when the trigger input is low. " +
            "Note that this pin is low by default. Therefore, if it is not driven and " +
            "this option is set to true, the LED will not turn on.")]
        public bool LedRespectsTrigger { get; set; } = false;


        static internal void IssueStartCommands(IMiniscopeDaqControls controls)
        {
            // 8-bit            7-bit           Description
            // ---------------------------------------------
            // 192 (0xc0)       96 (0x60)       Deserializer
            // 176 (0xb0)       88 (0x58)       Serializer
            // 160 (0xa0)       80 (0x50)       TPL0102 Digital potentiometer
            // 80 (0x50)        40 (0x28)       BNO055
            // 254 (0xfe)       127 (0x7F)      ??
            // 238 (0xee)       119 (0x77)      MAX14574 EWL driver
            // 32 (0x20)        16 (0x10)       ATTINY MCU

            controls.I2C.QueueCommand(192, 31, 16); // I2C: 0x60
            controls.I2C.QueueCommand(176, 5, 32); // I2C:0x58
            controls.I2C.QueueCommand(192, 34, 2);
            controls.I2C.QueueCommand(192, 32, 10);
            controls.I2C.QueueCommand(192, 7, 176);
            controls.I2C.QueueCommand(176, 15, 2);
            controls.I2C.QueueCommand(176, 30, 10);
            controls.I2C.QueueCommand(192, 8, 32, 238, 160, 80);
            controls.I2C.QueueCommand(192, 16, 32, 238, 88, 80);
            controls.I2C.QueueCommand(80, 65, 6, 7); // BNO Axis mapping and sign
            controls.I2C.QueueCommand(80, 61, 12); // BNO operation mode is NDOF
            controls.I2C.QueueCommand(254, 0); // 0x7F
            controls.I2C.QueueCommand(238, 3, 3); // 0x77
            controls.I2C.CommitCommands();

            controls.EnableFrameTTLs(true);
        }

        static internal void IssueStopCommands(IMiniscopeDaqControls controls)
        {
            controls.EnableFrameTTLs(false);
            controls.I2C.QueueCommand(32, 1, 255);
            controls.I2C.QueueCommand(88, 0, 114, 255);
            controls.I2C.CommitCommands();
        }


        readonly ArrayPool<byte> framePool = ArrayPool<byte>.Create(maxArrayLength: 1000 * 1000 * 2, maxArraysPerBucket: 60);

        private static unsafe UclaMiniscopeV4Frame CreateFrameFromRaw(MiniscopeV4RawFrame frame, MiniscopeV4MediaCapture capture)
        {
            fixed (byte* ptr = frame.DataArray)
            {
                IntPtr dataPtr = new IntPtr(ptr);
                FrameInfo frameInfo;
                Quaternion quat;
                ExtractMetadata(dataPtr, out frameInfo, out quat);
                using (var image = new IplImage(new OpenCV.Net.Size(frame.PixelWidth, frame.PixelHeight), IplDepth.U8, 2, dataPtr))
                {
                    var rgbImage = new IplImage(image.Size, IplDepth.U8, 3);
                    CV.CvtColor(image, rgbImage, ColorConversion.Yuv2BgrYuy2);
                    bool trigger = (frameInfo.State & 0x01) != 0;
                    bool aux = (frameInfo.State & 0x02) != 0;
                    return new UclaMiniscopeV4Frame(rgbImage, quat, (int)frameInfo.FrameCount,trigger, aux);
                }
            }
        }


        public override IObservable<UclaMiniscopeV4Frame> Generate()
        {
            return Observable.Create<UclaMiniscopeV4Frame>(async (observer, cancellationToken) =>
            {
                var channelOptions = new BoundedChannelOptions(120);
                channelOptions.AllowSynchronousContinuations = false;
                channelOptions.FullMode = BoundedChannelFullMode.DropOldest;
                channelOptions.SingleReader = true;
                channelOptions.SingleWriter = true;
                var frameChannel = Channel.CreateBounded<MiniscopeV4RawFrame>(channelOptions, (frame) => framePool.Return(frame.DataArray));
                using (var capture = await MiniscopeV4MediaCapture.Create(Index, frameChannel, framePool))
                {
                    FirmwareVersion = capture.FwVersion;
                    var subscriptionList = new CompositeDisposable();
                    try
                    {
                        // Create frame collection task embedded into an observable
                        var frameObservable = Observable.Create<UclaMiniscopeV4Frame>(async (frameObserver, frameCancellationToken) =>
                        {
                            try
                            {
                                await foreach (MiniscopeV4RawFrame frame in frameChannel.Reader.ReadAllAsync(frameCancellationToken))
                                {
                                    // NB : This try block ensures that the frames read by the frameChannel are all
                                    // returned to the pool if something goes wrong in CreateFrameFromRaw
                                    // actual exceptions are thrown to the outer try..catch
                                    try
                                    {
                                        frameObserver.OnNext(CreateFrameFromRaw(frame, capture));
                                    }
                                    finally
                                    {
                                        framePool.Return(frame.DataArray);
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                frameObserver.OnCompleted();
                            }
                            catch (Exception ex)
                            {
                                frameObserver.OnError(ex);
                            }
                            finally
                            {
                                while (frameChannel.Reader.TryRead(out MiniscopeV4RawFrame pendingFrame))
                                {
                                    if (pendingFrame.DataArray != null)
                                    {
                                        framePool.Return(pendingFrame.DataArray);
                                    }
                                }
                            }
                        }).Publish().RefCount();

                        // Configure device
                        IssueStartCommands(capture.DaqControls);

                        // Prepare hardware controls and connection

                        // NB: This line allows to throttle the uvc control signals, so even if two buffered frames arrived
                        // to quickly, we limit in time (right now to ~2 frames time). This affects manual controls, so
                        // having this limit is not noticeable
                        // We also send a dummy frame to the controls, to set the settings such as FPS before we receive the first frame
                        var throttledFrame = Observable.Return(new UclaMiniscopeV4Frame(null, new Quaternion(), 0, false, false))
                        .Concat(frameObservable).Sample(new TimeSpan(0, 0, 0, 0, 67)).Publish().RefCount();
                        
                        //Brightness
                        subscriptionList.Add(throttledFrame
                            .Select(c => new { Gate = c.Trigger, RespectsTrigger = LedRespectsTrigger, Brightness = LedBrightness })
                            .DistinctUntilChanged().Select(val =>
                            {
                                if (val.RespectsTrigger && !val.Gate) return (byte)255;
                                return (byte)(255 - 2.55 * val.Brightness);
                            }).DistinctUntilChanged().Subscribe(val =>
                            {
                                capture.DaqControls.I2C.QueueCommand(32, 1, val);
                                capture.DaqControls.I2C.QueueCommand(88, 0, 114, val);
                            }));

                        // SensorGain

                        subscriptionList.Add(throttledFrame
                            .Select(c => SensorGain).DistinctUntilChanged().Subscribe(val =>
                            {
                                capture.DaqControls.I2C.QueueCommand(32, 5, 0, 204, 0, (byte)val);
                            }));

                        // Focus
                        subscriptionList.Add(throttledFrame
                            .Select(c => Focus).DistinctUntilChanged().Subscribe(val =>
                            {
                                var scaled = val * 1.27;
                                capture.DaqControls.I2C.QueueCommand(238, 8, (byte)(127 + scaled), 2);
                            }));

                        // FPS
                        subscriptionList.Add(throttledFrame
                            .Select(c => FramesPerSecond).DistinctUntilChanged().Subscribe(val =>
                            {
                                byte v0 = (byte)((int)val & 0x00000FF);
                                byte v1 = (byte)(((int)val & 0x000FF00) >> 8);
                                capture.DaqControls.I2C.QueueCommand(32, 5, 0, 201, v0, v1);
                            }));

                        subscriptionList.Add(throttledFrame.Subscribe(_ => capture.DaqControls.I2C.CommitCommands()));
                        subscriptionList.Add(frameObservable.Subscribe(observer));


                        // Start acquisition

                        await capture.Start(Width, Height, cancellationToken);
                        await Task.Delay(-1, cancellationToken);

                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        subscriptionList.Dispose();
                        IssueStopCommands(capture.DaqControls);
                    }
                }

            }).PublishReconnectable().RefCount();

        }

                      

        struct FrameInfo
        {
            public uint FrameCount;
            public uint FrameTime;
            public uint State;
        }
        private static uint ExtractWord(ulong v)
        {
            return (uint)(((v >> 8) & 0x000000FF) |
                          ((v >> 16) & 0x0000FF00) |
                          ((v >> 24) & 0x00FF0000) |
                          ((v >> 32) & 0xFF000000));
        }
        static unsafe void ExtractMetadata(IntPtr buffer, out FrameInfo info, out Quaternion quat)
        {
            ulong* src = (ulong*)buffer;

            uint w0 = ExtractWord(src[0]); // FrameCount
            uint w1 = ExtractWord(src[1]); // FrameTime
            uint w2 = ExtractWord(src[2]); // State
            uint w3 = ExtractWord(src[3]); // Quaternion: W, X
            uint w4 = ExtractWord(src[4]); // Quaternion: Y, Z

            info = new FrameInfo
            {
                FrameCount = w0,
                FrameTime = w1,
                State = w2
            };


            short rawW = (short)(w3 & 0xFFFF);
            short rawX = (short)(w3 >> 16);
            short rawY = (short)(w4 & 0xFFFF);
            short rawZ = (short)(w4 >> 16);

            float qX = rawX * QuatConvFactor;
            float qY = rawY * QuatConvFactor;
            float qZ = rawZ * QuatConvFactor;
            float qW = rawW * QuatConvFactor;

            quat = new Quaternion(qX, qY, qZ, qW);
        }
    }
    // NB: Needs a unique name, even though its a class member, for de/serialization without issues
    public enum GainV4
    {
        Low = 225,
        Medium = 228,
        High = 36,
    }

    class GainV4TypeConverter : EnumConverter
    {
        internal GainV4TypeConverter()
            : base(typeof(GainV4))
        {
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(new[]
            {
                GainV4.Low,
                GainV4.Medium,
                GainV4.High,
            });
        }
    }

    // NB: Needs a unique name, even though its a class member, for de/serialization without issues
    public enum FrameRateV4
    {
        Fps10 = 39 & 0x000000FF | 16 << 8,
        Fps15 = 26 & 0x000000FF | 11 << 8,
        Fps20 = 19 & 0x000000FF | 136 << 8,
        Fps25 = 15 & 0x000000FF | 160 << 8,
        Fps30 = 12 & 0x000000FF | 228 << 8,
    };

    public struct MiniscopeV4RawFrame
    {
        public byte[] DataArray;
        public uint DataLength;
        public int PixelWidth;
        public int PixelHeight;
    }

    class FrameRateV4TypeConverter : EnumConverter
    {
        internal FrameRateV4TypeConverter()
            : base(typeof(FrameRateV4))
        {
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(new[]
            {
                FrameRateV4.Fps10,
                FrameRateV4.Fps15,
                FrameRateV4.Fps20,
                FrameRateV4.Fps25,
                FrameRateV4.Fps30,
            });
        }
    }
}
