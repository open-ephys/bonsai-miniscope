using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using AForge.Video.DirectShow;

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

    int[] GetMiniscopeIndices()
    {
        var deviceFilters = new FilterInfoCollection(FilterCategory.VideoInputDevice);
        var indices = new List<int>();
        for (int i = 0; i < deviceFilters.Count; i++)
        {
            if (deviceFilters[i].Name.Contains("MINISCOPE"))
                indices.Add(i);
        }

        return indices.ToArray();
    }

    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
    {
        if (destinationType == typeof(string))
        {
            var index = (int)value;
            var deviceFilters = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (index >= 0 && index < deviceFilters.Count)
            {
                return string.Format(culture, "{0} ({1})", index, deviceFilters[index].Name);
            }
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
