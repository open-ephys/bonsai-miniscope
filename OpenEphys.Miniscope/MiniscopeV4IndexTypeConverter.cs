using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenEphys.Miniscope
{
    internal class MiniscopeV4IndexTypeConverter : Int32Converter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        {
            return true;
        }


        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            var connectedDaqs = MiniscopeV4MediaCapture.GetConnectedMiniscopedCached();
            int count = connectedDaqs.Count;
            int[] indexes = new int[count];
            for (int i = 0; i < count; i++)
            {
                indexes[i] = i;
            }
            return new StandardValuesCollection(indexes);

        }
    }
}
