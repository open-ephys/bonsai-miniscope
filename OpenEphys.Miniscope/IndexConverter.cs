﻿using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System;
using AForge.Video.DirectShow;

class IndexConverter : Int32Converter
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

    public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
    {
        var deviceFilters = new FilterInfoCollection(FilterCategory.VideoInputDevice);
        return new StandardValuesCollection(Enumerable.Range(0, deviceFilters.Count).ToArray());
    }
}
