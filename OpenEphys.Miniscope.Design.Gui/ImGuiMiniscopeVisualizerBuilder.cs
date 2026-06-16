using Bonsai;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Defines a visualizer builder that constructs an ImGui-based visualizer for Miniscope data, utilizing the <see cref="ImGuiMiniscopeVisualizer"/> as its visualizer implementation.
/// </summary>
[TypeVisualizer(typeof(ImGuiMiniscopeVisualizer))]
public class ImGuiMiniscopeVisualizerBuilder : ImGuiMashupVisualizerBuilder
{
}
