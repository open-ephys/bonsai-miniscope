using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using Bonsai;
using Hexa.NET.ImGui;
using OpenCV.Net;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Renders the ImGui controls used to configure the saturation overlay.
/// </summary>
[Combinator]
[Description("Renders the ImGui controls used to configure the saturation overlay.")]
public class SaturationSettings
{
    /// <summary>
    /// Renders the saturation settings controls and returns an updated <see cref="SaturationSettingsDto"/> alongside each source value.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>A sequence of values paired with the saturation settings updated from the rendered controls.</returns>
    public unsafe IObservable<Tuple<TSource, SaturationSettingsDto>> Process<TSource>(IObservable<Tuple<TSource, SaturationSettingsDto>> source)
    {
        return Observable.Create<Tuple<TSource, SaturationSettingsDto>>(observer =>
        {
            var sourceObserver = Observer.Create<Tuple<TSource, SaturationSettingsDto>>(value =>
            {
                var dto = value.Item2;
                var threshold = dto.Threshold;
                var color = new Vector4((float)dto.Color.Val2 / 255, (float)dto.Color.Val1 / 255, (float)dto.Color.Val0 / 255, (float)dto.Color.Val3 / 255);

                ImGui.Text("Saturation");

                ImGui.BeginChild("##saturation_group", new Vector2(-1, 70), ImGuiChildFlags.Borders);

                ImGui.Text("Threshold: ");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-1f);

                double thresholdMin = 0, thresholdMax = 255;
                ImGui.SliderScalar("##saturation_threshold", ImGuiDataType.Double, &threshold, &thresholdMin, &thresholdMax, "%.1f");

                ImGui.Text("Color: ");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-1f);

                ImGui.ColorEdit4("##saturation_color", ref color, ImGuiColorEditFlags.Uint8 | ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoOptions);

                ImGui.EndChild();

                observer.OnNext(Tuple.Create(value.Item1, new SaturationSettingsDto(threshold, new Scalar(color.Z * 255, color.Y * 255, color.X * 255, color.W * 255))));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
