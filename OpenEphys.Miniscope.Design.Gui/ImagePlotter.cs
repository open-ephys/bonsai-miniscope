using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Xml.Serialization;
using Bonsai;
using Hexa.NET.ImGui;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Renders the ImGui tabs used to display the raw Miniscope image, the saturation image, and the dF/F image.
/// </summary>
[Combinator]
[Description("Renders the ImGui tabs used to display the raw Miniscope image, the saturation image, and the dF/F image.")]
public class ImagePlotter
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
    /// Renders the image tabs for each source value and forwards the value unchanged.
    /// </summary>
    /// <param name="source">The sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public IObservable<TSource> Process<TSource>(IObservable<TSource> source)
    {
        return Observable.Create<TSource>(observer =>
        {
            var sourceObserver = Observer.Create<TSource>(
                value =>
                {
                    var displaySize = CalculateDisplaySize(ImGui.GetContentRegionAvail(), new Vector2(ImageWidth, ImageHeight));
                    PlotImages(displaySize, MiniscopeImage, SaturationImage, dFFImage);
                    observer.OnNext(value);
                },
                observer.OnError,
                observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }

    static void PlotImages(Vector2 displaySize, ImTextureRef miniscopeImage, ImTextureRef saturationImage, ImTextureRef dFFImage)
    {
        if (ImGui.BeginTabBar("##ImageTabBar", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton | ImGuiTabBarFlags.DrawSelectedOverline))
        {
            if (ImGui.BeginTabItem("Image##Image"))
            {
                PlotImage(displaySize, miniscopeImage);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Saturation##Saturation"))
            {
                PlotImage(displaySize, saturationImage);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("dF/F##dF/F"))
            {
                PlotImage(displaySize, dFFImage);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    static void PlotImage(Vector2 displaySize, ImTextureRef image)
    {
        if (!image.TexID.IsNull)
        {
            ImGui.Image(image, displaySize);
        }
        else
        {
            ImGui.Text("No image data found.");
        }
    }

    static Vector2 CalculateDisplaySize(Vector2 availableRegion, Vector2 imageSize)
    {
        if (imageSize.X == 0 && imageSize.Y == 0)
        {
            return new Vector2(0, 0);
        }

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
