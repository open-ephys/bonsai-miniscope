using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Represents the combined settings state for all panels in the GUI.
/// </summary>
/// <param name="Miniscope">The Miniscope acquisition settings.</param>
/// <param name="File">The file saving settings.</param>
/// <param name="Saturation">The saturation overlay settings.</param>
/// <param name="Dff">The dF/F calculation settings.</param>
/// <param name="Commutator">The commutator connection and settings.</param>
public record SettingsPanelDto(
    MiniscopeSettingsDto Miniscope,
    FileSettingsDto File,
    SaturationSettingsDto Saturation,
    DffSettingsDto Dff,
    CommutatorSettingsDto Commutator);

/// <summary>
/// Combines individual settings DTOs into a single <see cref="SettingsPanelDto"/>.
/// </summary>
[Description("Combines all settings DTOs into a single object for use with the SettingsPanel combinator.")]
public class CreateSettingsPanelDto : Combinator<
    Tuple<MiniscopeSettingsDto, FileSettingsDto, SaturationSettingsDto, DffSettingsDto, CommutatorSettingsDto>,
    SettingsPanelDto>
{
    /// <summary>
    /// Creates a <see cref="SettingsPanelDto"/> by combining the latest value from each settings DTO sequence.
    /// </summary>
    /// <param name="source">A sequence of tuples containing one DTO per settings section.</param>
    /// <returns>A sequence of <see cref="SettingsPanelDto"/> objects.</returns>
    public override IObservable<SettingsPanelDto> Process(
        IObservable<Tuple<MiniscopeSettingsDto, FileSettingsDto, SaturationSettingsDto, DffSettingsDto, CommutatorSettingsDto>> source)
    {
        return source.Select(value => new SettingsPanelDto(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5));
    }

    /// <summary>
    /// Creates a <see cref="SettingsPanelDto"/> by combining the latest value from each settings DTO sequence.
    /// </summary>
    public IObservable<SettingsPanelDto> Process(
        IObservable<MiniscopeSettingsDto> miniscope,
        IObservable<FileSettingsDto> file,
        IObservable<SaturationSettingsDto> saturation,
        IObservable<DffSettingsDto> dff,
        IObservable<CommutatorSettingsDto> commutator)
    {
        return Observable.CombineLatest(
            miniscope,
            file,
            saturation,
            dff,
            commutator,
            (m, f, s, d, c) => new SettingsPanelDto(m, f, s, d, c));
    }
}
