using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenEphys.Miniscope
{
    internal interface IUvcExtensionUnit
    {
        public byte[] ExtensionUnitRead(uint control, uint dataSize);
        public void ExtensionUnitWrite(uint control, byte[] data);
    }
}
