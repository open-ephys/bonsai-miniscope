using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenEphys.Miniscope
{
    enum UvcVideoControls : uint
    {
        Brightness = 0,
        Contrast,
        Hue,
        Saturation,
        Sharpness,
        Gamma,
        Color,
        WhiteBalance,
        BacklightCompensation,
        Gain
    }
    internal interface IUvcProcessingUnit
    {
        public ushort ProcessingUnitRead(UvcVideoControls control);
        public void ProcessingUnitWrite(UvcVideoControls control, ushort data);
    }
}
