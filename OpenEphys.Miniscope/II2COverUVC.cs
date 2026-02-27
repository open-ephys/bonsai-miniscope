using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenEphys.Miniscope
{
    internal interface II2COverUVC
    {
        void QueueCommand(byte address, params byte[] data);
        void CommitCommands();
    }
}
