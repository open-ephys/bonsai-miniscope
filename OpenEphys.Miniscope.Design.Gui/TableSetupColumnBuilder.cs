using Bonsai.ImGui;
using Hexa.NET.ImGui;
using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Represents an operator that sets up a column for the current table and submits a column header cell.
/// </summary>
[Description("Sets up a column for the current table and submits a column header cell.")]
public class TableSetupColumnBuilder : ControlBuilder
{
    /// <summary>
    /// Gets or sets the label displayed in the column header.
    /// </summary>
    [Description("The label displayed in the column header.")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the column configuration flags.
    /// </summary>
    [Description("The column configuration flags.")]
    public ImGuiTableColumnFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the initial width or weight of the column. Use 0 for the default.
    /// </summary>
    [Description("The initial width or weight of the column. Use 0 for the default.")]
    public float InitWidth { get; set; }

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
                        ImGui.TableSetupColumn(Label ?? string.Empty, Flags, InitWidth);
                        observer.OnNext(value);
                    }
                },
                observer.OnError,
                observer.OnCompleted);
            return source.SubscribeSafe(sourceObserver);
        });
    }
}
