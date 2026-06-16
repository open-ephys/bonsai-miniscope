using System;
using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using Bonsai;
using Bonsai.IO;
using Hexa.NET.ImGui;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Renders the ImGui controls used to configure and trigger file saving for Miniscope recordings.
/// </summary>
[Combinator]
[Description("Renders the ImGui controls used to configure and trigger file saving for Miniscope recordings.")]
public class FileSettings
{
    /// <summary>
    /// Gets or sets the average frame rate, in seconds, used to display the acquisition frame rate.
    /// </summary>
    [XmlIgnore]
    public double AverageFrameRate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a recording is currently in progress.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public bool RecordingStatus { get; set; }

    enum Codec
    {
        Y800
    }

    static readonly string[] CodecValues = Enum.GetNames(typeof(Codec));
    static readonly string[] PathSuffixValues = Enum.GetNames(typeof(PathSuffix));

    /// <summary>
    /// Renders the file settings controls and returns an updated <see cref="FileSettingsDto"/> alongside each source value.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>A sequence of values paired with the file settings updated from the rendered controls.</returns>
    public IObservable<Tuple<TSource, FileSettingsDto>> Process<TSource>(IObservable<Tuple<TSource, FileSettingsDto>> source)
    {
        PathSuffix suffix;

        Codec codec;

        bool recordButton;
        bool recordOnTriggerButton;
        bool useRecordDuration;

        int recordingDurationSeconds;

        DateTime acquisitionStart = DateTime.Now;
        Nullable<DateTime> recordingStart = null;
        var childSize = new Vector2(-1f, 195);

        return Observable.Create<Tuple<TSource, FileSettingsDto>>(observer =>
        {
            const nuint bufSize = 1024;
            string fileName = string.Empty;

            Task<string> saveDialogTask = null;

            var sourceObserver = Observer.Create<Tuple<TSource, FileSettingsDto>>(value =>
            {
                var dto = value.Item2;
                if (Enum.TryParse<Codec>(dto.VideoCodec, out var parsedCodec))
                {
                    codec = parsedCodec;
                }
                else
                {
                    codec = Codec.Y800;
                }
                recordButton = dto.RecordButton;
                recordOnTriggerButton = dto.RecordOnTriggerButton;
                fileName = dto.FileName;
                suffix = dto.Suffix;
                recordingDurationSeconds = dto.RecordingDuration;
                useRecordDuration = dto.UseRecordDuration;

                ImGui.Text("File");

                ImGui.BeginChild("##save_group", childSize, ImGuiChildFlags.Borders);

                ImGui.Text("File Name Template");
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("Choose the location and format to save all files.");
                    ImGui.Text("Video files will have '.avi' added the file format, and CSV files will have '.csv' added.");
                    ImGui.Text("If Suffix is set, the selected suffix will be added after the format and before the extension.");
                    ImGui.EndTooltip();
                }

                const string buttonLabel = "...";
                float buttonWidth = ImGui.CalcTextSize(buttonLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
                float inputWidth = ImGui.GetContentRegionAvail().X - buttonWidth - ImGui.GetStyle().ItemSpacing.X;

                ImGui.SetNextItemWidth(inputWidth);
                ImGui.InputText("##filename", ref fileName, bufSize);
                ImGui.SameLine();
                if (ImGui.Button($"{buttonLabel}##choose_filename_button", new Vector2(buttonWidth, 0)))
                {
                    if (saveDialogTask == null || saveDialogTask.IsCompleted)
                    {
                        saveDialogTask = Task.Run(() =>
                        {
                            string result = string.Empty;

                            Thread t = new(() =>
                            {
                                SaveFileDialog saveFileDialog = new()
                                {
                                    InitialDirectory = Path.GetFullPath(fileName),
                                    Filter = "All Files|*.*",
                                    Title = "Choose where to save Miniscope data.",
                                    AddExtension = false,
                                    CheckFileExists = false,
                                    CheckPathExists = false,
                                    FileName = Path.GetFileName(fileName)
                                };

                                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                                {
                                    result = saveFileDialog.FileName;
                                }
                            });

                            t.SetApartmentState(ApartmentState.STA);
                            t.Start();
                            t.Join();

                            return result;
                        });
                    }
                }

                if (saveDialogTask != null && saveDialogTask.IsCompleted)
                {
                    var result = saveDialogTask.Result;
                    if (!string.IsNullOrEmpty(result))
                    {
                        fileName = result;
                    }
                    saveDialogTask = null;
                }

                if (ImGui.BeginTable("##writer_parameters", 2))
                {
                    ImGui.TableNextColumn();

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Suffix:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(-1f);

                    int currentPathSuffix = (int)suffix;
                    if (ImGui.Combo("##path_suffix", ref currentPathSuffix, PathSuffixValues, PathSuffixValues.Length))
                    {
                        suffix = (PathSuffix)currentPathSuffix;
                    }

                    ImGui.TableNextColumn();

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Video Codec: ");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(-1f);

                    int codecIndex = (int)codec;
                    if (ImGui.Combo("##codecs", ref codecIndex, CodecValues, CodecValues.Length))
                    {
                        codec = (Codec)codecIndex;
                    }

                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();

                    ImGui.Text("Recording Duration [s]:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(-1f);
                    ImGui.InputInt("##recording_duration", ref recordingDurationSeconds, ImGuiInputTextFlags.AutoSelectAll);

                    ImGui.TableNextColumn();

                    ImGui.Checkbox("Use Recording Duration##use_record_duration", ref useRecordDuration);

                    ImGui.EndTable();
                }

                if (ImGui.BeginTable("##buttons", 2))
                {
                    ImGui.TableNextColumn();

                    bool buttonActive = false;
                    if (recordButton)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
                        buttonActive = true;
                    }

                    Vector2 buttonSize = new(-1f, ImGui.GetFrameHeight() * 2);

                    if (ImGui.Button("Record##record_button", buttonSize))
                    {
                        recordButton = !recordButton;
                        if (recordButton)
                            recordOnTriggerButton = false;
                    }

                    if (buttonActive)
                    {
                        ImGui.PopStyleColor();
                    }

                    ImGui.TableNextColumn();

                    buttonActive = false;
                    if (recordOnTriggerButton)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
                        buttonActive = true;
                    }

                    if (ImGui.Button("Record on Trigger##record_on_trigger_button", buttonSize))
                    {
                        recordOnTriggerButton = !recordOnTriggerButton;
                        if (recordOnTriggerButton)
                            recordButton = false;
                    }

                    if (buttonActive)
                    {
                        ImGui.PopStyleColor();
                    }

                    ImGui.EndTable();
                }

                ImGui.Separator();

                if (ImGui.BeginTable("##status_bar", 3))
                {
                    ImGui.TableNextColumn();
                    ImGui.Text($"Frame Rate: {AverageFrameRate:F1} FPS");

                    ImGui.TableNextColumn();
                    ImGui.Text($"Acquiring: {(DateTime.Now - acquisitionStart).TotalSeconds:F1} s");
                    if (RecordingStatus)
                    {
                        if (recordingStart == null)
                        {
                            recordingStart = DateTime.Now;
                        }

                        ImGui.TableNextColumn();
                        ImGui.Text($"Recording: {(DateTime.Now - recordingStart.Value).TotalSeconds:F1} s");
                    }
                    else if (recordingStart != null)
                    {
                        recordingStart = null;
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();

                observer.OnNext(Tuple.Create(value.Item1, new FileSettingsDto(recordButton, recordOnTriggerButton, codec.ToString(), fileName, suffix, recordingDurationSeconds, useRecordDuration)));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
