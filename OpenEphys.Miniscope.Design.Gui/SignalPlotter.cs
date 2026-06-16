using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Xml.Serialization;
using Bonsai;
using Bonsai.ImGui.Visualizers;
using Bonsai.Vision;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Renders the ImGui tabs used to display Miniscope orientation, digital input, and pixel intensity histogram time series.
/// </summary>
[Combinator]
[Description("Renders the ImGui tabs used to display Miniscope orientation, digital input, and pixel intensity histogram time series.")]
public class SignalPlotter
{
    /// <summary>
    /// Gets or sets the rolling series of quaternion orientation values plotted in the time series tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public RollingPlotPointSeries<Quaternion> QuaternionSeries { get; set; }

    /// <summary>
    /// Gets or sets the rolling series of digital input values plotted in the time series tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public RollingPlotPointSeries<Tuple<bool, bool>> DigitalInSeries { get; set; }

    /// <summary>
    /// Gets or sets the rolling series of frame rate values plotted in the time series tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public RollingPlotPointSeries<double> FrameRateSeries { get; set; }

    /// <summary>
    /// Gets or sets the pixel intensity histogram plotted in the histogram tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public ScalarHistogram ImageHistogram { get; set; }

    readonly Vector2 size = new(-1, 0);
    readonly ImPlotFlags plotFlags = ImPlotFlags.NoMenus | ImPlotFlags.NoInputs;

    readonly string[] digitalInLabels = new string[] { MiniscopeDaqDigitalIn.DigitalIn0.ToString(), MiniscopeDaqDigitalIn.DigitalIn1.ToString() };
    readonly string[] histogramAxisTickLabels = new string[] { "0%", "20%", "40%", "60%", "80%", "100%" };

    /// <summary>
    /// Renders the time series and histogram tabs for each source value and forwards the value unchanged.
    /// </summary>
    /// <param name="source">The sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public unsafe IObservable<TSource> Process<TSource>(IObservable<TSource> source)
    {
        return Observable.Create<TSource>(observer =>
        {
            var sourceObserver = Observer.Create<TSource>(
                value =>
                {
                    if (ImGui.BeginTabBar("##TabBar"))
                    {
                        if (ImGui.BeginTabItem("Time Series"))
                        {
                            ImPlotAxisFlags flagsX = ImPlotAxisFlags.AutoFit;
                            ImPlotAxisFlags flagsY = ImPlotAxisFlags.AutoFit;

                            if (QuaternionSeries == null && DigitalInSeries == null)
                            {
                                ImGui.Text("No data to display");
                            }
                            else if (ImPlot.BeginPlot("##series", size, plotFlags))
                            {
                                ImPlot.SetupAxes("Sample", "Value", flagsX, flagsY);
                                ImPlot.SetupAxisLimits(ImAxis.Y1, -0.1, 1.1, ImPlotCond.Always);

                                if (QuaternionSeries != null)
                                {
                                    for (var i = 0; i < QuaternionSeries.Series.Length; i++)
                                    {
                                        var line = QuaternionSeries.Series[i];
                                        ImPlot.PlotLineG(line.Name, line.Getter, null, QuaternionSeries.Count);
                                    }
                                }

                                if (DigitalInSeries != null)
                                {
                                    for (int i = 0; i < DigitalInSeries.Series.Length; i++)
                                    {
                                        var line = DigitalInSeries.Series[i];
                                        ImPlot.PlotStairsG(digitalInLabels[i], line.Getter, null, DigitalInSeries.Count);
                                    }
                                }

                                ImPlot.EndPlot();
                            }

                            ImGui.EndTabItem();
                        }

                        if (ImGui.BeginTabItem("Histogram"))
                        {
                            ImPlotAxisFlags flagsX = ImPlotAxisFlags.NoLabel;
                            ImPlotAxisFlags flagsY = ImPlotAxisFlags.AutoFit | ImPlotAxisFlags.NoTickLabels;

                            var histogram = ImageHistogram.Val0;

                            histogram.Bins.GetRawData(out var binPtr);

                            if (ImPlot.BeginPlot("##histogram", size, plotFlags))
                            {
                                double minValue = 0, maxValue = byte.MaxValue;
                                int numLabels = histogramAxisTickLabels.Length;

                                ImPlot.SetupAxes("Pixel Value [%]", "", flagsX, flagsY);
                                ImPlot.SetupAxisLimits(ImAxis.X1, minValue, maxValue, ImPlotCond.Always);
                                ImPlot.SetupAxisTicks(ImAxis.X1, minValue, maxValue, numLabels, histogramAxisTickLabels, false);

                                ImPlot.PlotBars("##pixel_intensity", (float*)binPtr.ToPointer(), histogram.Bins.GetDimSize(0), 2.0f);

                                ImPlot.EndPlot();
                            }

                            ImGui.EndTabItem();
                        }

                        ImGui.EndTabBar();
                    }

                    observer.OnNext(value);
                },
                observer.OnError,
                observer.OnCompleted);
            return source.SubscribeSafe(sourceObserver);
        });
    }
}
