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
/// Renders the image tabs and signal tabs inside a single child region that fills the available content area.
/// </summary>
[Combinator]
[Description("Renders the image and signal plot panels inside a single child region.")]
public class DataPanel
{
    /// <summary>
    /// Gets or sets the texture displayed in the raw image tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public ImTextureRef MiniscopeImage { get; set; }

    /// <summary>
    /// Gets or sets the texture displayed in the saturation tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public ImTextureRef SaturationImage { get; set; }

    /// <summary>
    /// Gets or sets the texture displayed in the dF/F tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public ImTextureRef dFFImage { get; set; }

    /// <summary>
    /// Gets or sets the height, in pixels, of the source images used to calculate the display size.
    /// </summary>
    public int ImageHeight { get; set; } = 100;

    /// <summary>
    /// Gets or sets the width, in pixels, of the source images used to calculate the display size.
    /// </summary>
    public int ImageWidth { get; set; } = 100;

    /// <summary>
    /// Gets or sets the fraction of the available height allocated to the image pane. The signal pane
    /// receives the remaining fraction. Must be between 0 and 1.
    /// </summary>
    public float ImageHeightFraction { get; set; } = 0.6f;

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
    /// Gets or sets the pixel intensity histogram plotted in the histogram tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public ScalarHistogram ImageHistogram { get; set; }

    readonly Vector2 fillWidth = new(-1, 0);
    readonly ImPlotFlags plotFlags = ImPlotFlags.NoMenus | ImPlotFlags.NoInputs;
    readonly string[] digitalInLabels = new string[] { MiniscopeDaqDigitalIn.DigitalIn0.ToString(), MiniscopeDaqDigitalIn.DigitalIn1.ToString() };
    readonly string[] histogramAxisTickLabels = new string[] { "0%", "20%", "40%", "60%", "80%", "100%" };

    /// <summary>
    /// Renders the data panel for each source value and forwards the value unchanged.
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
                    float totalHeight = ImGui.GetContentRegionAvail().Y;
                    float fraction = Math.Max(0f, Math.Min(1f, ImageHeightFraction));
                    float tabBarHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
                    float imageChildHeight = totalHeight * fraction - tabBarHeight;
                    float signalChildHeight = totalHeight * (1f - fraction) - tabBarHeight - ImGui.GetStyle().ItemSpacing.Y;

                    ImGui.BeginChild("##Data", new Vector2(ImGui.GetContentRegionAvail().X - SettingsPanel.GetCurrentWidth, -1f));

                    ImGui.BeginChild("##image_pane", new Vector2(-1, imageChildHeight), ImGuiChildFlags.None);

                    var displaySize = CalculateDisplaySize(
                        ImGui.GetContentRegionAvail(),
                        new Vector2(ImageWidth, ImageHeight));

                    if (ImGui.BeginTabBar("##ImageTabBar", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton | ImGuiTabBarFlags.DrawSelectedOverline))
                    {
                        if (ImGui.BeginTabItem("Image##Image"))
                        {
                            PlotImage(displaySize, MiniscopeImage);
                            ImGui.EndTabItem();
                        }

                        if (ImGui.BeginTabItem("Saturation##Saturation"))
                        {
                            PlotImage(displaySize, SaturationImage);
                            ImGui.EndTabItem();
                        }

                        if (ImGui.BeginTabItem("dF/F##dFF"))
                        {
                            PlotImage(displaySize, dFFImage);
                            ImGui.EndTabItem();
                        }

                        ImGui.EndTabBar();
                    }

                    ImGui.EndChild();

                    ImGui.BeginChild("##signal_pane", new Vector2(-1, signalChildHeight), ImGuiChildFlags.None);

                    if (ImGui.BeginTabBar("##SignalTabBar"))
                    {
                        if (ImGui.BeginTabItem("Time Series"))
                        {
                            ImPlotAxisFlags flagsX = ImPlotAxisFlags.AutoFit;
                            ImPlotAxisFlags flagsY = ImPlotAxisFlags.AutoFit;

                            if (QuaternionSeries == null && DigitalInSeries == null)
                            {
                                ImGui.Text("No data to display");
                            }
                            else if (ImPlot.BeginPlot("##series", fillWidth, plotFlags))
                            {
                                ImPlot.SetupAxes("Sample", "Value", flagsX, flagsY);
                                ImPlot.SetupAxisLimits(ImAxis.Y1, -0.1, 1.1, ImPlotCond.Always);

                                if (QuaternionSeries != null)
                                {
                                    for (int i = 0; i < QuaternionSeries.Series.Length; i++)
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

                            if (ImPlot.BeginPlot("##histogram", fillWidth, plotFlags))
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

                    ImGui.EndChild();

                    ImGui.EndChild();

                    observer.OnNext(value);
                },
                observer.OnError,
                observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }

    static void PlotImage(Vector2 displaySize, ImTextureRef image)
    {
        if (!image.TexID.IsNull)
            ImGui.Image(image, displaySize);
        else
            ImGui.Text("No image data found.");
    }

    static Vector2 CalculateDisplaySize(Vector2 availableRegion, Vector2 imageSize)
    {
        if (imageSize.X == 0 && imageSize.Y == 0)
            return Vector2.Zero;

        float displayWidth = availableRegion.X;
        float displayHeight = displayWidth * imageSize.Y / imageSize.X;
        if (displayHeight > availableRegion.Y)
        {
            displayHeight = availableRegion.Y;
            displayWidth = displayHeight * imageSize.X / imageSize.Y;
        }

        return new Vector2(displayWidth, displayHeight);
    }
}
