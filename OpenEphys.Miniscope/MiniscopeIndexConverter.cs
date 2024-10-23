using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using OpenCV.Net;

class MiniscopeIndexConverter : Int32Converter
{
    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
    {
        var text = value as string;
        if (!string.IsNullOrEmpty(text))
        {
            return int.Parse(text.Split(' ')[0], culture);
        }

        return base.ConvertFrom(context, culture, value);
    }

    // TODO: this is beyond heinous, avert your eyes 
    int[] GetMiniscopeIndices()
    {
        var cameras = new List<(int Index,int FrameCount)>();

        for (int i = 0; i < 10; i++)
        {
            using (var capture = Capture.CreateCameraCapture(i++))
            {
                if (capture.QueryFrame() != null)
                {
                    cameras.Add((i, (int)capture.GetProperty(CaptureProperty.Sharpness)));
                }
                capture.Close();
            }
        }

        // Check if the frame counter incremented because this
        // is miniscopes' version of a product ID number
        foreach (var i in cameras)
        {
            using (var capture = Capture.CreateCameraCapture(i.Index))
            {
                if (capture.GetProperty(CaptureProperty.Sharpness) <= i.FrameCount)
                {
                    cameras.Remove(i);
                    capture.Close();
                }
            }
        }
        return cameras.Select(x => x.Index).ToArray();
    }

    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
    {
        if (destinationType == typeof(string))
        {
            var index = (int)value;
            return string.Format(culture, "{0} ({1})", index, "MINISCOPE"); // deviceFilters[index].Name);
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
    {
        return true;
    }

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
    {
        return true;
    }

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
    {
        return new StandardValuesCollection(GetMiniscopeIndices());
    }
}
