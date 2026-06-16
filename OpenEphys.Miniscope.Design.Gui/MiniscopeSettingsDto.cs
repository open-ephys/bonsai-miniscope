using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Represents the Miniscope acquisition settings displayed and edited by the GUI.
/// </summary>
/// <param name="LedBrightness">The brightness of the excitation LED, as a percentage.</param>
/// <param name="Focus">The electrowetting lens focus value.</param>
/// <param name="SensorGain">The image sensor gain.</param>
/// <param name="FrameRate">The image sensor frame rate.</param>
/// <param name="LedRespectsDigitalIn">The digital input line that gates the excitation LED, if any.</param>
public record MiniscopeSettingsDto(double LedBrightness, double Focus, GainV4 SensorGain, FrameRateV4 FrameRate, MiniscopeDaqDigitalIn LedRespectsDigitalIn);

/// <summary>
/// Combines individual Miniscope setting values into a single <see cref="MiniscopeSettingsDto"/>.
/// </summary>
[Description("Combines individual Miniscope setting values into a single object.")]
public class CreateMiniscopeSettingsDto : Transform<Tuple<double, double, GainV4, FrameRateV4, MiniscopeDaqDigitalIn>, MiniscopeSettingsDto>
{
    /// <summary>
    /// Creates a <see cref="MiniscopeSettingsDto"/> from a sequence of tuples containing the Miniscope settings.
    /// </summary>
    /// <param name="source">A sequence of tuples containing the Miniscope settings.</param>
    /// <returns>A sequence of <see cref="MiniscopeSettingsDto"/> objects.</returns>
    public override IObservable<MiniscopeSettingsDto> Process(IObservable<Tuple<double, double, GainV4, FrameRateV4, MiniscopeDaqDigitalIn>> source)
    {
        return source.Select(value => new MiniscopeSettingsDto(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5));
    }

    /// <summary>
    /// Creates a <see cref="MiniscopeSettingsDto"/> by combining the latest values from each individual Miniscope setting sequence.
    /// </summary>
    /// <param name="ledBrightness">The brightness of the excitation LED, as a percentage.</param>
    /// <param name="focus">The electrowetting lens focus value.</param>
    /// <param name="sensorGain">The image sensor gain.</param>
    /// <param name="frameRate">The image sensor frame rate.</param>
    /// <param name="ledRespectsDigitalIn">The digital input line that gates the excitation LED, if any.</param>
    /// <returns>A sequence of <see cref="MiniscopeSettingsDto"/> objects.</returns>
    public IObservable<MiniscopeSettingsDto> Process(IObservable<double> ledBrightness, IObservable<double> focus, IObservable<GainV4> sensorGain, IObservable<FrameRateV4> frameRate, IObservable<MiniscopeDaqDigitalIn> ledRespectsDigitalIn)
    {
        return Observable.CombineLatest(
            ledBrightness,
            focus,
            sensorGain,
            frameRate,
            ledRespectsDigitalIn,
            (double ledBrightness, double focus, GainV4 sensorGain, FrameRateV4 frameRate, MiniscopeDaqDigitalIn ledRespectsDigitalIn) => new MiniscopeSettingsDto(ledBrightness, focus, sensorGain, frameRate, ledRespectsDigitalIn));
    }
}
