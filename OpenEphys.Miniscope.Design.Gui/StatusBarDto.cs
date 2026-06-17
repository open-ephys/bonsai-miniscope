using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.Miniscope.Design.Gui;

/// <summary>
/// Represents the state displayed and edited by the status bar.
/// </summary>
/// <param name="CameraIndex">The index of the camera to connect to.</param>
/// <param name="IsConnected">Whether the miniscope is currently connected.</param>
/// <param name="StatusMessage">A message describing the current connection status.</param>
/// <param name="RecordingError">A message describing a recording error, if one has occurred.</param>
public record StatusBarDto(int CameraIndex, bool IsConnected, string StatusMessage, string RecordingError);

/// <summary>
/// Combines individual status bar values into a single <see cref="StatusBarDto"/>.
/// </summary>
[Description("Combines individual status bar values into a single object.")]
public class CreateStatusBarDto : Transform<Tuple<int, bool, string, string>, StatusBarDto>
{
    /// <summary>
    /// Creates a <see cref="StatusBarDto"/> from a sequence of tuples containing the status bar values.
    /// </summary>
    /// <param name="source">A sequence of tuples containing the camera index, connection state, status message, and recording error values.</param>
    /// <returns>A sequence of <see cref="StatusBarDto"/> objects.</returns>
    public override IObservable<StatusBarDto> Process(IObservable<Tuple<int, bool, string, string>> source)
    {
        return source.Select(value => new StatusBarDto(value.Item1, value.Item2, value.Item3, value.Item4));
    }

    /// <summary>
    /// Creates a <see cref="StatusBarDto"/> by combining the latest values from each individual status bar sequence.
    /// </summary>
    /// <param name="cameraIndex">The index of the camera to connect to.</param>
    /// <param name="isConnected">Whether the miniscope is currently connected.</param>
    /// <param name="statusMessage">A message describing the current connection status.</param>
    /// <param name="recordingError">A message describing a recording error, if one has occurred.</param>
    /// <returns>A sequence of <see cref="StatusBarDto"/> objects.</returns>
    public IObservable<StatusBarDto> Process(
        IObservable<int> cameraIndex,
        IObservable<bool> isConnected,
        IObservable<string> statusMessage,
        IObservable<string> recordingError)
    {
        return Observable.CombineLatest(
            cameraIndex,
            isConnected,
            statusMessage,
            recordingError,
            (index, connected, status, error) => new StatusBarDto(index, connected, status, error));
    }
}
