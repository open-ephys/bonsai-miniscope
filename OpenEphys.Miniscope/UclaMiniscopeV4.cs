using System;
using System.Buffers;
using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Bonsai;
using OpenCV.Net;

namespace OpenEphys.Miniscope
{
    /// <summary>
    /// Produces a data sequence from a UCLA Miniscope V4, including fluorescence images,
    /// head-orientation data, and digital I/O state.
    /// </summary>
    [Description("Produces a data sequence from a UCLA Miniscope V4.")]
    public class UclaMiniscopeV4 : Source<UclaMiniscopeV4Frame>
    {
        // Frame size
        const int Width = 608;
        const int Height = 608;

        //how many seconds without frames before closing the stream
        const int FrameTimeoutSeconds = 2;

        // 1 quaternion = 2^14 bits
        const float QuatConvFactor = 1.0f / (1 << 14);

        // how many invlid quaternions do we allow before failing
        const float InvalidQuaternionLimit = 1;

        /// <summary>
        /// Gets or sets the index of the camera from which to acquire images.
        /// </summary>
        [TypeConverter(typeof(MiniscopeV4IndexTypeConverter))]
        [Description("The index of the camera from which to acquire images.")]
        public int Index { get; set; } = 0;

        /// <summary>
        /// Gets or sets the excitation LED brightness as a percent of maximum.
        /// </summary>
        [Precision(1, 0.1)]
        [Range(0, 100)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Excitation LED brightness (percent of max).")]
        public double LedBrightness { get; set; } = 0;

        /// <summary>
        /// Gets or sets the electro-wetting lens focal plane adjustment as a percent of range around nominal.
        /// </summary>
        [Precision(1, 0.1)]
        [Range(-100, 100)]
        [Editor(DesignTypes.SliderEditor, typeof(UITypeEditor))]
        [Description("Electro-wetting lens focal plane adjustment (percent of range around nominal).")]
        public double Focus { get; set; } = 0;

        /// <summary>
        /// Gets or sets the image sensor gain setting.
        /// </summary>
        [TypeConverter(typeof(GainV4TypeConverter))]
        [Description("Image sensor gain setting.")]
        public GainV4 SensorGain { get; set; } = GainV4.Low;

        /// <summary>
        /// Gets or sets the frame rate in Hz.
        /// </summary>
        [TypeConverter(typeof(FrameRateV4TypeConverter))]
        [Description("Frames captured per second.")]
        public FrameRateV4 FramesPerSecond { get; set; } = FrameRateV4.Fps30;


        /// <summary>
        /// Gets the firmware version reported by the connected DAQ.
        /// </summary>
        [Description("Firmware version")]
        [XmlIgnore]
        public Version FirmwareVersion { get; private set; } = new Version(0,0,0);

        //// TODO: Does not work with DAQ for some reason
        //[Description("Only turn on excitation LED during camera exposures.")]
        //public bool InterleaveLed { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether to turn off the LED when the trigger input is low.
        /// </summary>
        /// <remarks>
        /// Note that this pin is low by default. Therefore, if it is not driven and this option is set to
        /// <see langword="true"/>, the LED will not turn on.
        /// </remarks>
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
            try
            {
                controls.EnableFrameTTLs(false);
                controls.I2C.QueueCommand(32, 1, 255);
                controls.I2C.QueueCommand(88, 0, 114, 255);
                controls.I2C.CommitCommands();
            }
            catch (InvalidOperationException)
            {
                // NB : Is there is a timeout due to a usb disconnection, the
                // media capture interface can "work" silently, or fail
                // in any case, we are doing a cleanup here. If it fails
                // (usually because there is no device anymore)
                // we just continue with the cleanup
            }
        }

        readonly ArrayPool<byte> framePool = ArrayPool<byte>.Create(maxArrayLength: 1000 * 1000 * 2, maxArraysPerBucket: 60);

        private static unsafe UclaMiniscopeV4Frame CreateFrameFromRaw(MiniscopeV4RawFrame frame)
        {
            fixed (byte* ptr = frame.DataArray)
            {
                IntPtr dataPtr = new(ptr);
                ExtractMetadata(dataPtr, out var frameInfo, out var quat);
                using (var image = new IplImage(new Size(frame.PixelWidth, frame.PixelHeight), IplDepth.U8, 2, dataPtr))
                {
                    var rgbImage = new IplImage(image.Size, IplDepth.U8, 3);
                    CV.CvtColor(image, rgbImage, ColorConversion.Yuv2BgrYuy2);
                    bool trigger = (frameInfo.State & 0x01) != 0;
                    bool aux = (frameInfo.State & 0x02) != 0;
                    return new UclaMiniscopeV4Frame(rgbImage, quat, (int)frameInfo.FrameCount,trigger, aux);
                }
            }
        }

        /// <summary>
        /// Returns the data sequence produced by the connected Miniscope V4.
        /// </summary>
        /// <returns>A sequence of <see cref="UclaMiniscopeV4Frame"/> values.</returns>
        public override IObservable<UclaMiniscopeV4Frame> Generate()
        {
            return Observable.Create<UclaMiniscopeV4Frame>(async (observer, cancellationToken) =>
            {
                var channelOptions = new BoundedChannelOptions(120)
                {
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true
                };

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
                            int invalidQuaternions = 0;
                            try
                            {
                                await foreach (MiniscopeV4RawFrame frame in frameChannel.Reader.ReadAllAsync(frameCancellationToken))
                                {
                                    // NB : This try block ensures that the frames read by the frameChannel are all
                                    // returned to the pool if something goes wrong in CreateFrameFromRaw
                                    // actual exceptions are thrown to the outer try..catch
                                    try
                                    {
                                        var processedFrame = CreateFrameFromRaw(frame);
                                        if (processedFrame.Quaternion.W == 0 && processedFrame.Quaternion.X == 0
                                        && processedFrame.Quaternion.Y == 0 && processedFrame.Quaternion.Z == 0)
                                        {
                                            if (++invalidQuaternions > InvalidQuaternionLimit)
                                            {
                                                throw new InvalidOperationException("Invalid quaternion value");
                                            }
                                        }

                                        frameObserver.OnNext(processedFrame);
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
                                // NB : Do a non-cancellable asynchronous flush
                                // This will continue until the channel is marked as completed
                                // ensuring that no frames can be accidentally added after the flush
                                await foreach (MiniscopeV4RawFrame pendingFrame in frameChannel.Reader.ReadAllAsync())
                                {
                                    if (pendingFrame.DataArray != null)
                                    {
                                        framePool.Return(pendingFrame.DataArray);
                                    }
                                }
                            }
                        })
                        .Timeout(TimeSpan.FromSeconds(FrameTimeoutSeconds))
                        .Catch((TimeoutException e) => {
                            return Observable.Throw<UclaMiniscopeV4Frame>(new TimeoutException("Frame timeout"));
                        })
                        .Publish();

                        // Configure device
                        IssueStartCommands(capture.DaqControls);

                        // Prepare hardware controls and connection

                        // NB : We decouple control activation from the main event, but still use frame production as
                        // a throttle of sorts, and to update led brightness depending on frame triger value
                        // (to take advantage of batch i2c command transmission, all controls need to be updated on the same block)
                        // We also send a dummy frame to the controls, to set the settings such as FPS before we receive the first frame
                        var controlsObservable = 
                            Observable.Return(new UclaMiniscopeV4Frame(null, new Quaternion(), 0, false, false))
                            .Concat(frameObservable)
                            .Catch(Observable.Empty<UclaMiniscopeV4Frame>()) // NB : ignore exceptions on the control subscriptions. They will be catched downstream by Bonsai
                            .ObserveOn(TaskPoolScheduler.Default)
                            .Publish()
                            .RefCount();
                        
                        // Brightness
                        subscriptionList.Add(controlsObservable
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
                        subscriptionList.Add(
                            controlsObservable.Select(_ => SensorGain).DistinctUntilChanged().Subscribe(val =>
                            {
                                capture.DaqControls.I2C.QueueCommand(32, 5, 0, 204, 0, (byte)val);
                            }));

                        // Focus
                        subscriptionList.Add(controlsObservable
                            .Select(_ => Focus).DistinctUntilChanged().Subscribe(val =>
                            {
                                var scaled = val * 1.27;
                                capture.DaqControls.I2C.QueueCommand(238, 8, (byte)(127 + scaled), 2);
                            }));

                        // FPS
                        subscriptionList.Add(controlsObservable
                            .Select(_ => FramesPerSecond).DistinctUntilChanged().Subscribe(val =>
                            {
                                byte v0 = (byte)((int)val & 0x00000FF);
                                byte v1 = (byte)(((int)val & 0x000FF00) >> 8);
                                capture.DaqControls.I2C.QueueCommand(32, 5, 0, 201, v0, v1);
                            }));

                        subscriptionList.Add(controlsObservable.Subscribe(_ => capture.DaqControls.I2C.CommitCommands()));
                        subscriptionList.Add(frameObservable.Subscribe(observer));


                        // Start acquisition
                        await capture.Start(Width, Height, cancellationToken);

                        // Now start frame distribution, with the timeout starting here
                        subscriptionList.Add(frameObservable.Connect());

                        await Task.Delay(-1, cancellationToken);

                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        subscriptionList.Dispose();

                        // NB : Close the channel
                        // This will make the producer stop adding frames to it
                        // and the consumer can flush them quickly
                        frameChannel.Writer.TryComplete();

                        IssueStopCommands(capture.DaqControls);
                    }
                }
            })
            .PublishReconnectable()
            .RefCount();
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


    /// <summary>
    /// Specifies the image sensor gain options for the UCLA Miniscope V4.
    /// </summary>
    public enum GainV4
    {
        /// <summary>Low sensor gain.</summary>
        Low = 225,
        /// <summary>Medium sensor gain.</summary>
        Medium = 228,
        /// <summary>High sensor gain.</summary>
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

    /// <summary>
    /// Specifies the available frame rate options for the UCLA Miniscope V4.
    /// </summary>
    public enum FrameRateV4
    {
        /// <summary>10 frames per second.</summary>
        Fps10 = 39 & 0x000000FF | 16 << 8,
        /// <summary>15 frames per second.</summary>
        Fps15 = 26 & 0x000000FF | 11 << 8,
        /// <summary>20 frames per second.</summary>
        Fps20 = 19 & 0x000000FF | 136 << 8,
        /// <summary>25 frames per second.</summary>
        Fps25 = 15 & 0x000000FF | 160 << 8,
        /// <summary>30 frames per second.</summary>
        Fps30 = 12 & 0x000000FF | 228 << 8,
    };

    /// <summary>
    /// Holds the raw byte buffer and metadata for a single frame received from the Miniscope V4 DAQ.
    /// </summary>
    public struct MiniscopeV4RawFrame
    {
        /// <summary>
        /// Gets or sets the byte array containing the raw frame pixel data.
        /// </summary>
        public byte[] DataArray;

        /// <summary>
        /// Gets or sets the number of valid bytes in <see cref="DataArray"/>.
        /// </summary>
        public uint DataLength;

        /// <summary>
        /// Gets or sets the pixel width of the frame.
        /// </summary>
        public int PixelWidth;

        /// <summary>
        /// Gets or sets the pixel height of the frame.
        /// </summary>
        public int PixelHeight;
    }

    class FrameRateV4TypeConverter : EnumConverter
    {
        internal FrameRateV4TypeConverter()
            : base(typeof(FrameRateV4))
        {
        }

         static string ToDisplayString(FrameRateV4 fps) => fps switch
         {
                FrameRateV4.Fps10 => "10 Hz",
                FrameRateV4.Fps15 => "15 Hz",
                FrameRateV4.Fps20 => "20 Hz",
                FrameRateV4.Fps25 => "25 Hz",
                FrameRateV4.Fps30 => "30 Hz",
                _ => fps.ToString()
         };

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override bool CanConvertFrom(ITypeDescriptorContext ctx, Type t) => t == typeof(string);

        /// <inheritdoc/>
        public override object ConvertFrom(ITypeDescriptorContext ctx, CultureInfo culture, object value)
            => Enum.GetValues(typeof(FrameRateV4)).Cast<FrameRateV4>().First(r => ToDisplayString(r) == (string)value);

        /// <inheritdoc/>
        public override object ConvertTo(ITypeDescriptorContext ctx, CultureInfo culture, object value, Type destType)
            => ToDisplayString((FrameRateV4)value);
    }
}
