using System.Collections.Generic;
using Bonsai.ImGui;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Defines a visualizer that initializes an ImGui-based visualizer for Miniscope data.
/// </summary>
public class ImGuiMiniscopeVisualizer : ImGuiMashupVisualizer
{
    /// <inheritdoc/>
    protected override IEnumerable<IExtensionFactory> GetExtensions()
    {
        yield return ImGuiMiniscopeExtension.Factory;
    }
}
