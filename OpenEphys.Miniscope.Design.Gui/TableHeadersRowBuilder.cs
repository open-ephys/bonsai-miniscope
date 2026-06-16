using Bonsai.ImGui;
using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace OpenEphys.Miniscope.Design.GUI;
using ImGui = Hexa.NET.ImGui.ImGui;

/// <summary>
/// Represents an operator that submits all pending header rows for the current table.
/// </summary>
[Description("Submits all pending header rows for the current table.")]
public class TableHeadersRowBuilder : ControlBuilder
{
    /// <inheritdoc/>
    protected override IObservable<TSource> Generate<TSource>(IObservable<TSource> source)
    {
        return Observable.Create<TSource>(observer =>
        {
            var sourceObserver = System.Reactive.Observer.Create<TSource>(
                value =>
                {
                    if (Visible)
                    {
                        ImGui.TableHeadersRow();
                        observer.OnNext(value);
                    }
                },
                observer.OnError,
                observer.OnCompleted);
            return source.SubscribeSafe(sourceObserver);
        });
    }
}
