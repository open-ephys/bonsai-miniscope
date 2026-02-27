using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Markup;

namespace OpenEphys.Miniscope
{
    internal class ExtendedI2COverUVC : II2COverUVC
    {
        readonly Queue<byte[]> commandQueue = new Queue<byte[]>();
        byte[] currentCmd = new byte[25];
        readonly IUvcExtensionUnit controls;
        readonly object dataLock = new object();

        public ExtendedI2COverUVC(IUvcExtensionUnit controls)
        {
            this.controls = controls;
        }
        public void CommitCommands()
        {
            lock (dataLock)
            {
                if (currentCmd[0] != 0)
                {
                    commandQueue.Enqueue(currentCmd);
                    currentCmd = new byte[25];
                }
            }
            while (commandQueue.Count > 0)
            {
                var buffer = commandQueue.Dequeue();
                controls.ExtensionUnitWrite(2, buffer);
            }
        }

        public void QueueCommand(byte address, params byte[] data)
        {
            if (data.Length > 5) throw new ArgumentOutOfRangeException("Command must be maximum 5 bytes");
            lock (dataLock)
            {
                int index = currentCmd[0];
                currentCmd[0]++;

                if (data.Length == 5)
                {
                    currentCmd[index * 6 + 1] = (byte)((int)address | 0x01);
                    for (int i = 0; i < data.Length; i++)
                    {
                        currentCmd[index * 6 + 2 + i] = data[i];
                    }
                }
                else
                {
                    currentCmd[index * 6 + 1] = address;
                    currentCmd[index * 6 + 2] = (byte)(data.Length + 1);
                    for (int i = 0; i < data.Length; i++)
                    {
                        currentCmd[index * 6 + 3 + i] = data[i];
                    }
                }

                if (index == 3)
                {
                    commandQueue.Enqueue(currentCmd);
                    currentCmd = new byte[25];
                }
            }
        }

    }
}
