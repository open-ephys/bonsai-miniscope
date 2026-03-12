using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using System.Buffers;

namespace OpenEphys.Miniscope
{
    internal class MiniscopeV4MediaCapture : IDisposable
    {
        public IMiniscopeDaqControls DaqControls { get; }
        public IUvcProcessingUnit ProcessingUnit { get; }
        public Version FwVersion { get; }

        private MiniscopeV4MediaCapture() { }

        readonly ChannelWriter<MiniscopeV4RawFrame> frameChannel;
        readonly ArrayPool<byte> framePool;
        readonly MediaCapture capture;
        readonly VideoDeviceController videoController;
        MediaFrameReader frameReader = null;
        CancellationToken cancellationToken = default;
        MediaFrameSource frameSource = null;

        private MiniscopeV4MediaCapture(ChannelWriter<MiniscopeV4RawFrame> channel, ArrayPool<byte> framePool, MediaCapture capture)
        {
            this.frameChannel = channel;
            this.framePool = framePool;
            this.capture = capture;
            this.videoController = capture.VideoDeviceController;
            var uvcControls = new MediaCaptureUvcControls(videoController);
            if (uvcControls.HasExtensionUnit)
            {
                FwVersion = uvcControls.FwVersion;
                DaqControls = new ExtendedMiniscopeDaqControls(uvcControls);
                //I2CInterface = new LegacyI2COverUVC(uvcControls);
            }
            else
            {
                FwVersion = new Version(1,0,0);
                DaqControls = new LegacyMiniscopeDaqControls(uvcControls);
            }
            ProcessingUnit = uvcControls;
            
        }

        public async Task Start(int Width, int Height, CancellationToken cancellationToken)
        {
           if (frameReader != null) return;
            capture.Failed += OnMediaCaptureFailed;

            this.cancellationToken = cancellationToken;

            frameSource = capture.FrameSources.First().Value;
            var format = frameSource.SupportedFormats.FirstOrDefault(f =>
                             f.Subtype.Equals("YUY2", StringComparison.OrdinalIgnoreCase) &&
                             f.VideoFormat.Width == Width &&
                            f.VideoFormat.Height == Height);

            if (format != null)
            {
                await frameSource.SetFormatAsync(format);
            }

            frameReader = await capture.CreateFrameReaderAsync(frameSource, "YUY2");
            frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Buffered;

            cancellationToken.ThrowIfCancellationRequested();
            frameReader.FrameArrived += OnFrameReceived;
            var status = await frameReader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                throw new SystemException($"Could not start frame acquisition: {status}");
            }
        }


        [ComImport]
        [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        partial interface IMemoryBufferByteAccess
        {
            void GetBuffer(out IntPtr buffer, out uint capacity);
        }
        void OnFrameReceived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                using (var frame = sender.TryAcquireLatestFrame())
                {
                    if (frame?.VideoMediaFrame != null)
                    {
                        using (var bitmap = frame.VideoMediaFrame.SoftwareBitmap)
                        using (var buffer = bitmap.LockBuffer(Windows.Graphics.Imaging.BitmapBufferAccessMode.Read))
                        using (var reference = buffer.CreateReference())
                        {
                            var byteAccess = (IMemoryBufferByteAccess)reference;
                            byteAccess.GetBuffer(out IntPtr dataPtr, out uint size);
                            var dataArray = framePool.Rent((int)size);
                            unsafe
                            {
                                fixed (byte* ptr = dataArray)
                                {
                                    Buffer.MemoryCopy(dataPtr.ToPointer(), ptr, dataArray.Length, size);
                                }
                            }
                            var rawFrame = new MiniscopeV4RawFrame
                            {
                                DataArray = dataArray,
                                DataLength = size,
                                PixelHeight = bitmap.PixelHeight,
                                PixelWidth = bitmap.PixelWidth
                            };
                            if (!frameChannel.TryWrite(rawFrame))
                            {
                                // NB :  if cannot be written, return the buffer
                                framePool.Return(rawFrame.DataArray);
                            }

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                frameChannel.TryComplete(ex);
            }
        }

        void OnMediaCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
        {
            if (cancellationToken.IsCancellationRequested) return;
            frameChannel.TryComplete(new IOException("Error acquiring frames"));
        }


        public void Dispose()
        {
            frameChannel.TryComplete();
            if (frameReader != null)
            {
                frameReader.FrameArrived -= OnFrameReceived;
                frameReader.Dispose();
            }
            capture.Failed -= OnMediaCaptureFailed;
            capture.Dispose();
        }

        public static async Task<MiniscopeV4MediaCapture> Create(int deviceIndex, ChannelWriter<MiniscopeV4RawFrame> channel, ArrayPool<byte> framePool)
        {
            var deviceList = await GetConnectedMiniscopes();
            var deviceId = deviceList[deviceIndex];
            var capture = new MediaCapture();
            var captureSettings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = deviceId,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl
            };
            await capture.InitializeAsync(captureSettings);
            return new MiniscopeV4MediaCapture(channel, framePool, capture);


        }

        public static async Task<IReadOnlyList<string>> GetConnectedMiniscopes()
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            return devices.Where(d => d.Name.StartsWith("MINISCOPE")).Select(d => d.Id).ToList();
        }

        static IReadOnlyList<string> cachedDeviceList = null;
        static DateTime lastScan = DateTime.MinValue;
        const double ScanTimeToLiveSeconds = 5.0;

        public static IReadOnlyList<string> GetConnectedMiniscopedCached()
        {
            // First scan or scan too old
            if (cachedDeviceList == null || (DateTime.Now - lastScan).TotalSeconds > ScanTimeToLiveSeconds)
            {
                // NB : Since WinRT calls are async and we need a sync method for
                // the name converter, this is
                // the recommended way to wrap it into a synchronous method
                cachedDeviceList = Task.Run(() => GetConnectedMiniscopes()).GetAwaiter().GetResult();
                lastScan = DateTime.Now;
            }
            return cachedDeviceList;
        }
    }
}
