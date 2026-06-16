using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive.Linq;
using Bonsai;
using OpenCV.Net;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Represents the saturation overlay settings displayed and edited by the GUI.
/// </summary>
/// <param name="Threshold">The minimum pixel intensity considered saturated.</param>
/// <param name="Color">The color used to highlight saturated pixels.</param>
public record SaturationSettingsDto(double Threshold, Scalar Color);

/// <summary>
/// Combines individual saturation setting values into a single <see cref="SaturationSettingsDto"/>.
/// </summary>
[Description("Combines individual saturation setting values into a single object.")]
public class CreateSaturationSettingsDto : Transform<Tuple<double, Scalar>, SaturationSettingsDto>
{
    /// <summary>
    /// Creates a <see cref="SaturationSettingsDto"/> from a sequence of tuples containing the saturation settings.
    /// </summary>
    /// <param name="source">A sequence of tuples containing the threshold and color values.</param>
    /// <returns>A sequence of <see cref="SaturationSettingsDto"/> objects.</returns>
    public override IObservable<SaturationSettingsDto> Process(IObservable<Tuple<double, Scalar>> source)
    {
        return source.Select(value => new SaturationSettingsDto(value.Item1, value.Item2));
    }

    /// <summary>
    /// Creates a <see cref="SaturationSettingsDto"/> by combining the latest values from each individual saturation setting sequence.
    /// </summary>
    /// <param name="threshold">The minimum pixel intensity considered saturated.</param>
    /// <param name="color">The color used to highlight saturated pixels.</param>
    /// <returns>A sequence of <see cref="SaturationSettingsDto"/> objects.</returns>
    public IObservable<SaturationSettingsDto> Process(IObservable<double> threshold, IObservable<Scalar> color)
    {
        return Observable.CombineLatest(
            threshold,
            color,
            (threshold, color) => new SaturationSettingsDto(threshold, color));
    }
}
