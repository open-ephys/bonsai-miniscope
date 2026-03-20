using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bonsai.Dag;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.UI.Xaml.Controls;

namespace OpenEphys.Miniscope
{

    internal class MediaCaptureUvcControls : IUvcProcessingUnit, IUvcExtensionUnit
    {
        readonly VideoDeviceController videoDeviceController;
        readonly uint extensionUnitNodeId;
        readonly Guid ExtensionUnitGuid = new Guid("b33905c0-5500-4fee-9058-2c3efd330bb9");
        readonly Guid ProcessingUnitguid = new Guid("C6E13360-30AC-11D0-A18C-00A0C9118956");
        const uint KSPROPERTY_TYPE_TOPOLOGY = 0x10000000;
        const uint KSPROPERTY_TYPE_GET = 0x00000001;
        const uint KSPROPERTY_TYPE_SET = 0x00000002;

        public bool HasExtensionUnit { get; }

        public Version FwVersion { get; }
        
        public MediaCaptureUvcControls(VideoDeviceController controller)
        {
            videoDeviceController = controller;
            HasExtensionUnit = false;
            FwVersion = new Version();
            for (uint i = 1; i < 16; i++)
            {
                try
                {
                    var versionBytes = ExtensionUnitRead(1, 3, i);
                    FwVersion = new Version((int)versionBytes[0], (int)versionBytes[1], (int) versionBytes[2]);
                    HasExtensionUnit = true;
                    extensionUnitNodeId = i;
                    break;
                }
                catch (Exception) { }
            }

        }

        byte[] CreateExtendedPropertyId(Guid uid, uint control, uint flags, uint node)
        {
            byte[] extendedPropertyId = new byte[64];
            unsafe
            {
                fixed (byte* p = extendedPropertyId)
                {
                    *(Guid*)(p) = uid;
                    *(uint*)(p + 16) = control;
                    *(uint*)(p + 20) = flags;
                    *(uint*)(p + 24) = node;
                }
            }
            return extendedPropertyId;
        }

        public byte[] ExtensionUnitRead(uint control, uint dataSize)
        {
            return ExtensionUnitRead(control, dataSize, extensionUnitNodeId);
        }

        byte[] ExtensionUnitRead(uint control, uint dataSize, uint unitId)
        {
            byte[] extendedPropertyId = CreateExtendedPropertyId(
                ExtensionUnitGuid, control, KSPROPERTY_TYPE_GET | KSPROPERTY_TYPE_TOPOLOGY, unitId);

            var result = videoDeviceController.GetDevicePropertyByExtendedId(extendedPropertyId, dataSize);
            if (result.Status == VideoDeviceControllerGetDevicePropertyStatus.Success)
            {
                if (result.Value is byte[] resultData)
                {
                    return resultData;
                }
                else
                {
                    throw new System.InvalidOperationException("Invalid buffer");
                }
            }
            else
            {
                throw new System.InvalidOperationException($"Error reading control: {result.Status}");
            }
        }


        public void ExtensionUnitWrite(uint control, byte[] data)
        {
            byte[] extendedPropertyId = CreateExtendedPropertyId(
                ExtensionUnitGuid, control, KSPROPERTY_TYPE_SET | KSPROPERTY_TYPE_TOPOLOGY, extensionUnitNodeId);

            var result = videoDeviceController.SetDevicePropertyByExtendedId(extendedPropertyId, data);
            if (result != VideoDeviceControllerSetDevicePropertyStatus.Success)
            {
                throw new System.InvalidOperationException($"Error writing control: {result}");
            }
        }

        public ushort ProcessingUnitRead(UvcVideoControls control)
        {
            byte[] extendedPropertyId = CreateExtendedPropertyId(
                ProcessingUnitguid, (uint)control, KSPROPERTY_TYPE_GET, 0);
            var result = videoDeviceController.GetDevicePropertyByExtendedId(extendedPropertyId, 64);
            if (result.Status == VideoDeviceControllerGetDevicePropertyStatus.Success)
            {
                if (result.Value is byte[] resultData)
                {
                    return BitConverter.ToUInt16(resultData,24);
                }
                else
                {
                    throw new System.InvalidOperationException("Invalid buffer");
                }
            }
            else
            {
                throw new System.InvalidOperationException($"Error reading control: {result.Status}");
            }
        }

        public void ProcessingUnitWrite(UvcVideoControls control, ushort data)
        {
            byte[] extendedPropertyId = CreateExtendedPropertyId(
                ProcessingUnitguid, (uint)control, KSPROPERTY_TYPE_SET, 0);
            extendedPropertyId[24] = (byte)data;
            extendedPropertyId[25] = (byte)(data >> 8) ;
            var result = videoDeviceController.SetDevicePropertyByExtendedId(extendedPropertyId, extendedPropertyId);
            if (result != VideoDeviceControllerSetDevicePropertyStatus.Success)
            {
                throw new System.InvalidOperationException($"Error writing control: {result}");
            }
        }

    }
}
