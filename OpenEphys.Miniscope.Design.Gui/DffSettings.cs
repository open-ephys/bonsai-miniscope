using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using Bonsai;
using Hexa.NET.ImGui;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Renders the ImGui controls used to configure the dF/F (delta-F over F) calculation.
/// </summary>
[Combinator]
[Description("Renders the ImGui controls used to configure the dF/F (delta-F over F) calculation.")]
public class DffSettings
{
    /// <summary>
    /// Renders the dF/F settings controls and returns an updated <see cref="DffSettingsDto"/> alongside each source value.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>A sequence of values paired with the dF/F settings updated from the rendered controls.</returns>
    public unsafe IObservable<Tuple<TSource, DffSettingsDto>> Process<TSource>(IObservable<Tuple<TSource, DffSettingsDto>> source)
    {
        return Observable.Create<Tuple<TSource, DffSettingsDto>>(observer =>
        {
            var sourceObserver = Observer.Create<Tuple<TSource, DffSettingsDto>>(value =>
            {
                var dto = value.Item2;
                var backgroundFrames = dto.BackgroundFrames;
                var backgroundThreshold = dto.BackgroundThreshold;
                var sigma = dto.Sigma;

                ImGui.Text("dF/F");

                ImGui.BeginChild("##dff_group", new Vector2(-1, 110), ImGuiChildFlags.Borders);

                ImGui.Text("Background Frames: ");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-1f);

                int backgroundFramesMin = 2, backgroundFramesMax = 1000; // NB: These values are pulled from the Range attribute on DeltaFOverF.BackgroundFrames.
                if (ImGui.InputInt("##background_frames", ref backgroundFrames))
                {
                    backgroundFrames = Math.Max(backgroundFramesMin, Math.Min(backgroundFramesMax, backgroundFrames));
                }

                ImGui.Text("Background Threshold: ");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-1f);

                double backgroundThresholdMin = 0, backgroundThresholdMax = 255; // NB: These values are pulled from the Range attribute on DeltaFOverF.BackgroundThreshold.
                ImGui.SliderScalar("##background_threshold", ImGuiDataType.Double, &backgroundThreshold, &backgroundThresholdMin, &backgroundThresholdMax, "%.1f");

                ImGui.Text("Sigma: ");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-1f);

                if (ImGui.InputInt("##sigma", ref sigma))
                {
                    sigma = Math.Max(0, sigma);
                }

                ImGui.EndChild();

                observer.OnNext(Tuple.Create(value.Item1, new DffSettingsDto(backgroundFrames, backgroundThreshold, sigma)));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
