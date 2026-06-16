using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Bonsai.ImGui;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>   
/// Represents an extension context for ImGui that initializes an ImGui context and links it to the provided ImGui context.
/// </summary>
public class ImGuiMiniscopeExtension : IExtensionContext
{
    readonly ImPlotContextPtr plotContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImGuiMiniscopeExtension"/> class.
    /// </summary>
    /// <param name="guiContext">The handle to the ImGui context.</param>
    public ImGuiMiniscopeExtension(ImGuiContextPtr guiContext)
    {
        ImPlot.SetImGuiContext(guiContext);
        plotContext = ImPlot.CreateContext();
        ImPlot.SetCurrentContext(plotContext);
        ImPlot.StyleColorsDark(ImPlot.GetStyle());

        var io = ImGui.GetIO();
        var fontBytes = GetFontAsByteArray();
        var fontPtr = Marshal.AllocHGlobal(fontBytes.Length);
        Marshal.Copy(fontBytes, 0, fontPtr, fontBytes.Length);

        unsafe
        {
            // NB: The ImGui API handles the lifetime of the font data, so we do not need to free the allocated memory after adding the font.
            io.Fonts.AddFontFromMemoryTTF((byte*)fontPtr, fontBytes.Length, 16f);
        }

        // NB: All colors set here will be overridden by ImGuiMashupVisualizer.Load --> imGuiControl.Render --> ImGui.StyleColors*()

        ImGui.GetStyle().TabBarBorderSize = 0f;

        ImGui.GetStyle().ChildRounding = 4f;
        ImGui.GetStyle().ChildBorderSize = 1.5f;
    }

    /// <summary>
    /// Releases all resources held by the <see cref="ImGuiMiniscopeExtension"/>.
    /// </summary>
    public void Dispose()
    {
        ImPlot.SetCurrentContext(null);
        ImPlot.DestroyContext(plotContext);
    }

    /// <inheritdoc/>
    public void MakeCurrent(ImGuiContextPtr guiContext)
    {
        ImPlot.SetCurrentContext(plotContext);
        ImPlot.SetImGuiContext(guiContext);
    }

    static byte[] GetFontAsByteArray()
    {
        var assembly = Assembly.GetExecutingAssembly();

        using Stream stream = assembly.GetManifestResourceStream("OpenEphys.Miniscope.Design.Gui.Fonts.Inter.ttf")!;
        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Returns the default factory used to create new ImGui context handles.
    /// </summary>
    public static IExtensionFactory Factory => ImGuiMiniscopeExtensionFactory.Default;

    class ImGuiMiniscopeExtensionFactory : IExtensionFactory
    {
        internal static readonly ImGuiMiniscopeExtensionFactory Default = new();

        public IExtensionContext CreateContext(ImGuiContextPtr guiContext) => new ImGuiMiniscopeExtension(guiContext);
    }
}
