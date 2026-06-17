using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using Bonsai;
using Hexa.NET.ImGui;

namespace OpenEphys.Miniscope.Design.Gui;

/// <summary>
/// Renders the ImGui status bar controls at the top of the GUI.
/// </summary>
[Combinator]
[Description("Renders the ImGui status bar controls at the top of the GUI.")]
public class StatusBar
{
    /// <summary>
    /// Renders the status bar controls and returns an updated <see cref="StatusBarDto"/> alongside each source value.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>A sequence of values paired with the status bar state updated from the rendered controls.</returns>
    public IObservable<Tuple<TSource, StatusBarDto>> Process<TSource>(IObservable<Tuple<TSource, StatusBarDto>> source)
    {
        return Observable.Create<Tuple<TSource, StatusBarDto>>(observer =>
        {
            var sourceObserver = Observer.Create<Tuple<TSource, StatusBarDto>>(value =>
            {
                var dto = value.Item2;
                var cameraIndex = dto.CameraIndex;
                var isConnected = dto.IsConnected;
                var statusMessage = dto.StatusMessage;
                var recordingError = dto.RecordingError;

                if (!string.IsNullOrEmpty(statusMessage))
                {
                    isConnected = false; // NB: Force disconnection when there is an error message
                }

                var style = ImGui.GetStyle();
                var connectButtonWidth = ImGui.CalcTextSize("Disconnect").X + style.FramePadding.X * 2;
                var indexLabelWidth = ImGui.CalcTextSize("Index: ").X;
                var indexInputWidth = 60f;

                ImGui.Text("Index: ");
                ImGui.SameLine();

                if (isConnected)
                    ImGui.BeginDisabled();

                ImGui.SetNextItemWidth(indexInputWidth);
                if (ImGui.InputInt("##statusbar_index", ref cameraIndex, 0, 0))
                {
                    statusMessage = string.Empty;
                }

                if (isConnected)
                    ImGui.EndDisabled();

                ImGui.SameLine();
                if (ImGui.Button(isConnected ? "Disconnect##statusbar_btn" : "Connect##statusbar_btn"))
                {
                    isConnected = !isConnected;
                    statusMessage = string.Empty;
                }

                ImGui.SameLine();

                var hasError = !string.IsNullOrEmpty(statusMessage);
                var statusColor = hasError
                    ? new Vector4(0.9f, 0.3f, 0.3f, 1f)
                    : isConnected
                        ? new Vector4(0.2f, 0.8f, 0.2f, 1f)
                        : new Vector4(0.6f, 0.6f, 0.6f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
                var displayStatus = hasError
                    ? statusMessage
                    : isConnected ? "Connected" : "Disconnected";
                ImGui.Text(displayStatus);
                ImGui.PopStyleColor();

                if (!string.IsNullOrEmpty(recordingError))
                {
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.3f, 0.3f, 1f));
                    ImGui.Text($"| Save error: {recordingError}");
                    ImGui.PopStyleColor();
                }

                ImGui.Separator();

                observer.OnNext(Tuple.Create(value.Item1, new StatusBarDto(cameraIndex, isConnected, statusMessage, recordingError)));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
