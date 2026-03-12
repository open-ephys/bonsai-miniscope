using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenEphys.Miniscope
{
    internal class LegacyMiniscopeDaqControls : IMiniscopeDaqControls
    {
        private readonly II2COverUVC i2c;
        private readonly IUvcProcessingUnit processingUnit;
        public II2COverUVC I2C  => i2c;

        public void EnableFrameTTLs(bool enabled)
        {
            processingUnit.ProcessingUnitWrite(UvcVideoControls.Saturation, (ushort)(enabled ? 1 : 0));
        }

        public LegacyMiniscopeDaqControls(IUvcProcessingUnit processingUnit)
        {
            this.processingUnit = processingUnit;
            i2c = new LegacyI2COverUVC(processingUnit);
        }

        private class LegacyI2COverUVC : II2COverUVC
        {
            readonly Queue<ulong> commandQueue = new Queue<ulong>();
            readonly IUvcProcessingUnit controls;

            public LegacyI2COverUVC(IUvcProcessingUnit controls)
            {
                this.controls = controls;
            }
            public void CommitCommands()
            {
                while (commandQueue.Count > 0)
                {
                    var command = commandQueue.Dequeue();
                    controls.ProcessingUnitWrite(UvcVideoControls.Contrast, (ushort)command);
                    controls.ProcessingUnitWrite(UvcVideoControls.Gamma, (ushort)(command >> 16));
                    controls.ProcessingUnitWrite(UvcVideoControls.Sharpness, (ushort)(command >> 32));
                }
            }

            public void QueueCommand(byte address, params byte[] data)
            {
                if (data.Length > 5) throw new ArgumentOutOfRangeException("Command must be maximum 5 bytes");
                commandQueue.Enqueue(Helpers.CreateCommand(address, data));
            }
        }
    }
}
