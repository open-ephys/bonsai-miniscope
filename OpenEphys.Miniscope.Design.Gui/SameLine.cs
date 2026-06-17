using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;
using Hexa.NET.ImGui;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Calls <see cref="ImGui.SameLine()"/> so that the next rendered item is placed on the same line as the previous one.
/// </summary>
[Combinator]
[Description("Calls ImGui.SameLine() so that the next rendered item is placed on the same line as the previous one.")]
public class SameLine
{
    /// <summary>
    /// Calls <see cref="ImGui.SameLine()"/> for each source value and forwards the value unchanged.
    /// </summary>
    /// <param name="source">The sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public IObservable<TSource> Process<TSource>(IObservable<TSource> source)
    {
        return source.Do(_ => ImGui.SameLine());
    }
}
