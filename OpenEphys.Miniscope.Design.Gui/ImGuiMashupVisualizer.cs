using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Forms;
using Bonsai.Design;
using Bonsai.Expressions;
using Bonsai.ImGui;
using Bonsai.ImGui.Design;
using Hexa.NET.ImGui;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Represents a mashup visualizer that hosts a Dear ImGui control and coordinates rendering between
/// the visualizer and any nested mashup visualizers.
/// </summary>
public abstract class ImGuiMashupVisualizer : MashupVisualizer
{
    ImGuiControl imGuiControl;

    internal ImGuiControl Control => imGuiControl;

    /// <summary>
    /// Gets or sets the target interval, in milliseconds, between visualizer updates.
    /// </summary>
    protected virtual int TargetInterval => 1000 / 50;

    /// <inheritdoc/>
    public override void Load(IServiceProvider provider)
    {
        if (provider.GetService(typeof(MashupVisualizer)) is ImGuiMashupVisualizer imGuiVisualizer &&
            imGuiVisualizer.Control is ImGuiControl mashupControl)
        {
            foreach (var extension in GetExtensions())
                mashupControl.Extensions.Add(extension);
        }
        else
        {
            var context = (ITypeVisualizerContext)provider.GetService(typeof(ITypeVisualizerContext));
            var visualizerBuilder = (ImGuiMashupVisualizerBuilder)ExpressionBuilder.GetVisualizerElement(context.Source).Builder;
            var windowName = visualizerBuilder.Name ?? visualizerBuilder.GetType().Name;

            imGuiControl = new ImGuiControl
            {
                Dock = DockStyle.Fill,
            };

            foreach (var extension in GetExtensions())
                imGuiControl.Extensions.Add(extension);

            imGuiControl.Render += (sender, e) =>
            {
                ImGui.StyleColorsDark();

                var viewport = ImGui.GetMainViewport();
                ImGui.SetNextWindowPos(viewport.WorkPos);
                ImGui.SetNextWindowSize(viewport.WorkSize);

                ImGui.Begin(windowName, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoTitleBar);
                RenderMashup(visualizerBuilder);
                ImGui.End();
            };

            var visualizerService = (IDialogTypeVisualizerService)provider.GetService(typeof(IDialogTypeVisualizerService));
            visualizerService?.AddControl(imGuiControl);
            base.Load(provider);
        }
    }

    void RenderMashup(ImGuiMashupVisualizerBuilder builder)
    {
        builder._Render.OnNext(Unit.Default);
        for (int i = 0; i < MashupSources.Count; i++)
        {
            var mashupSource = MashupSources[i];
            if (mashupSource.Source.Builder is ImGuiMashupVisualizerBuilder nestedBuilder &&
                mashupSource.Visualizer is ImGuiMashupVisualizer nestedMashup)
            {
                nestedMashup.RenderMashup(nestedBuilder);
            }
        }
    }

    /// <summary>
    /// Returns the extensions to initialize alongside the Dear ImGui context.
    /// </summary>
    /// <returns>
    /// A sequence of <see cref="IExtensionFactory"/> objects used to initialize the
    /// Dear ImGui context. Initialization follows the order of objects in this sequence.
    /// </returns>
    protected abstract IEnumerable<IExtensionFactory> GetExtensions();

    /// <inheritdoc/>
    public override IObservable<object> Visualize(IObservable<IObservable<object>> source, IServiceProvider provider)
    {
        if (provider.GetService(typeof(IDialogTypeVisualizerService)) is not Control visualizerControl)
        {
            return source;
        }

        return Observable.Using(
            () => new Timer(),
            timer =>
            {
                var onError = Observable.FromEventPattern<ErrorEventArgs>(
                    handler => imGuiControl.Error += handler,
                    handler => imGuiControl.Error -= handler)
                    .SelectMany(evt => Observable.Throw<EventPattern<EventArgs>>(
                        new InvalidOperationException(evt.EventArgs.Message)));
                timer.Interval = TargetInterval;
                var timerTick = Observable.FromEventPattern<EventHandler, EventArgs>(
                    handler => timer.Tick += handler,
                    handler => timer.Tick -= handler);
                timer.Start();
                return timerTick
                  .Do(_ => imGuiControl?.Invalidate())
                  .Merge(onError)
                  .Finally(timer.Stop);
            });
    }

    /// <inheritdoc/>
    public override void Show(object value)
    {
    }

    /// <inheritdoc/>
    public override void Unload()
    {
        base.Unload();
        imGuiControl?.Dispose();
        imGuiControl = null;
    }
}
