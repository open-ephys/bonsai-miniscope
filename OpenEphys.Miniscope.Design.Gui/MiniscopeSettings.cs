using System;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using Bonsai;
using Hexa.NET.ImGui;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Renders the ImGui controls used to configure the Miniscope acquisition settings.
/// </summary>
[Combinator]
[Description("Renders the ImGui controls used to configure the Miniscope acquisition settings.")]
public class MiniscopeSettings
{
    static readonly string[] SensorGainValues = Enum.GetNames(typeof(GainV4)); // TODO: This is sorted by backing value => High | Low | Medium.
    static readonly string[] FrameRateValues = Enum.GetNames(typeof(FrameRateV4)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    static readonly string[] DigitalInValues = Enum.GetNames(typeof(MiniscopeDaqDigitalIn));

    /// <summary>
    /// Renders the Miniscope settings controls and returns an updated <see cref="MiniscopeSettingsDto"/> alongside each source value.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>A sequence of values paired with the Miniscope settings updated from the rendered controls.</returns>
    public unsafe IObservable<Tuple<TSource, MiniscopeSettingsDto>> Process<TSource>(IObservable<Tuple<TSource, MiniscopeSettingsDto>> source)
    {
        return Observable.Create<Tuple<TSource, MiniscopeSettingsDto>>(observer =>
        {
            var sourceObserver = Observer.Create<Tuple<TSource, MiniscopeSettingsDto>>(value =>
            {
                var dto = value.Item2;
                double ledBrightness = dto.LedBrightness;
                double focus = dto.Focus;
                GainV4 sensorGain = dto.SensorGain;
                FrameRateV4 frameRate = dto.FrameRate;
                var ledRespectsDigitalIn = dto.LedRespectsDigitalIn;

                ImGui.Text("Miniscope");

                ImGui.BeginChild("##miniscope_group", new Vector2(-1, 110), ImGuiChildFlags.Borders);

                if (ImGui.BeginTable("##row1", 2))
                {
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();

                    ImGui.Text("Focus: ");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(-1f);

                    double focusMin = -100, focusMax = 100;
                    ImGui.SliderScalar("##focus", ImGuiDataType.Double, &focus, &focusMin, &focusMax, "%.1f");

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();

                    ImGui.Text("Sensor Gain: ");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(-1f);

                    int sensorGainIndex = Array.IndexOf(SensorGainValues, sensorGain.ToString());
                    if (ImGui.Combo($"##sensorgain", ref sensorGainIndex, SensorGainValues, SensorGainValues.Length))
                    {
                        if (Enum.TryParse<GainV4>(SensorGainValues[sensorGainIndex], out var result))
                        {
                            sensorGain = result;
                        }
                    }

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();

                    ImGui.Text("Frame Rate: ");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(-1f);

                    int frameRateIndex = Array.IndexOf(FrameRateValues, frameRate.ToString());
                    if (ImGui.Combo("##framerate", ref frameRateIndex, FrameRateValues, FrameRateValues.Length))
                    {
                        if (Enum.TryParse<FrameRateV4>(FrameRateValues[frameRateIndex], out var result))
                        {
                            frameRate = result;
                        }
                    }

                    ImGui.EndTable();
                }

                if (ImGui.BeginTable("##row2", 2))
                {
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("LED Brightness: ");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(-1f);

                    double brightnessMin = 0, brightnessMax = 100;

                    ImGui.SliderScalar("##ledbrightness", ImGuiDataType.Double, &ledBrightness, &brightnessMin, &brightnessMax, "%.1f");

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();

                    ImGui.Text("LED Trigger: ");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(-1f);

                    int digitalInIndex = Array.IndexOf(DigitalInValues, ledRespectsDigitalIn.ToString());
                    if (ImGui.Combo("##ledrespectdigitalin", ref digitalInIndex, DigitalInValues, DigitalInValues.Length))
                    {
                        if (Enum.TryParse<MiniscopeDaqDigitalIn>(DigitalInValues[digitalInIndex], out var result))
                        {
                            ledRespectsDigitalIn = result;
                        }
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();

                observer.OnNext(Tuple.Create(value.Item1, new MiniscopeSettingsDto(ledBrightness, focus, sensorGain, frameRate, ledRespectsDigitalIn)));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
