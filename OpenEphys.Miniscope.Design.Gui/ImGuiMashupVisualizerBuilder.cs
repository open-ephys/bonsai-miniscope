using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Subjects;
using System.Xml.Serialization;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Represents a visualizer builder that exposes a render notification sequence used to coordinate
/// rendering between a mashup visualizer and its nested visualizers.
/// </summary>
[XmlType("OEImGuiMashupVisualizerBuilder")]
public class ImGuiMashupVisualizerBuilder : Bonsai.ImGui.Design.ImGuiMashupVisualizerBuilder
{
    internal readonly Subject<Unit> _Render = new();

    /// <inheritdoc/>
    public override Expression Build(IEnumerable<Expression> arguments)
    {
        return Expression.Convert(Expression.Constant(_Render), typeof(IObservable<Unit>));
    }
}
