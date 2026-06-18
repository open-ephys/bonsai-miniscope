using System;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
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
using OpenCV.Net;

namespace OpenEphys.Miniscope.Design.GUI;

/// <summary>
/// Renders all settings panels in a collapsible sidebar and returns an updated <see cref="SettingsPanelDto"/>.
/// </summary>
[Combinator]
[Description("Renders all settings panels in a collapsible right-hand sidebar.")]
public class SettingsPanel
{
    const float ExpandedWidth = 375f;
    const float CollapsedWidth = 36f;

    static float GetCurrentWidth(float availableX)
    {
        if (settingsOpen)
        {
            if (ExpandedWidth <= availableX)
                return ExpandedWidth;

            else
            {
                settingsOpen = false;
                return availableX;
            }
        }

        return CollapsedWidth;
    }

    static readonly string[] SensorGainValues = Enum.GetNames(typeof(GainV4)); // TODO: This is sorted by backing value => High | Low | Medium.
    static readonly string[] FrameRateValues = Enum.GetNames(typeof(FrameRateV4)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    static readonly string[] DigitalInValues = Enum.GetNames(typeof(MiniscopeDaqDigitalIn));
    static readonly string[] CodecValues = new string[] { "Y800" };
    static readonly string[] PathSuffixValues = Enum.GetNames(typeof(PathSuffix));

    /// <summary>
    /// Gets or sets the average frame rate, in Hz, used to display the acquisition frame rate.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public double AverageFrameRate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a recording is currently in progress.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public bool RecordingStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether acquisition is currently in progress.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public bool AcquisitionStatus { get; set; }

    static bool settingsOpen = true;

    /// <summary>
    /// Renders the settings sidebar and returns an updated <see cref="SettingsPanelDto"/> alongside each source value.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>A sequence of values paired with the settings updated from the rendered controls.</returns>
    public unsafe IObservable<Tuple<TSource, SettingsPanelDto>> Process<TSource>(
        IObservable<Tuple<TSource, SettingsPanelDto>> source)
    {
        return Observable.Create<Tuple<TSource, SettingsPanelDto>>(observer =>
        {
            const nuint bufSize = 1024;
            string fileName = string.Empty;
            Task<string> saveDialogTask = null;
            DateTime? acquisitionStart = null;
            DateTime? recordingStart = null;

            var portNames = SerialPort.GetPortNames();

            var sourceObserver = Observer.Create<Tuple<TSource, SettingsPanelDto>>(value =>
            {
                var dto = value.Item2;

                double ledBrightness = dto.Miniscope.LedBrightness;
                double focus = dto.Miniscope.Focus;
                GainV4 sensorGain = dto.Miniscope.SensorGain;
                FrameRateV4 frameRate = dto.Miniscope.FrameRate;
                MiniscopeDaqDigitalIn ledRespectsDigitalIn = dto.Miniscope.LedRespectsDigitalIn;

                bool recordButton = dto.File.RecordButton;
                bool recordOnTriggerButton = dto.File.RecordOnTriggerButton;
                fileName = dto.File.FileName;
                PathSuffix suffix = dto.File.Suffix;
                int recordingDurationSeconds = dto.File.RecordingDuration;
                bool useRecordDuration = dto.File.UseRecordDuration;
                string videoCodec = dto.File.VideoCodec;
                int codecIndex = Array.IndexOf(CodecValues, videoCodec);
                if (codecIndex < 0) codecIndex = 0;

                double satThreshold = dto.Saturation.Threshold;
                var satColor = new Vector4(
                    (float)dto.Saturation.Color.Val2 / 255,
                    (float)dto.Saturation.Color.Val1 / 255,
                    (float)dto.Saturation.Color.Val0 / 255,
                    (float)dto.Saturation.Color.Val3 / 255);

                int backgroundFrames = dto.Dff.BackgroundFrames;
                double backgroundThreshold = dto.Dff.BackgroundThreshold;
                int sigma = dto.Dff.Sigma;

                string portName = dto.Commutator.PortName;
                bool commutatorConnected = dto.Commutator.IsConnected;
                bool commutatorEnable = dto.Commutator.Enable;
                bool commutatorEnableLed = dto.Commutator.EnableLed;
                string commutatorStatus = dto.Commutator.StatusMessage;

                float availableX = ImGui.GetContentRegionAvail().X;
                float panelWidth = GetCurrentWidth(availableX);

                ImGui.BeginChild("##settings_pane", new Vector2(panelWidth - ImGui.GetStyle().ChildBorderSize, -1), ImGuiChildFlags.Borders);

                if (!settingsOpen)
                {
                    if (ImGui.ArrowButton("##settings_open", ImGuiDir.Right))
                    {
                        settingsOpen = true;
                    }
                }
                else
                {
                    if (ImGui.ArrowButton("##settings_close", ImGuiDir.Left))
                        settingsOpen = false;

                    ImGui.SameLine();
                    ImGui.Text("Settings");
                    ImGui.Separator();

                    ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                    if (ImGui.CollapsingHeader("Miniscope##miniscope_header"))
                    {
                        ImGui.BeginChild("##miniscope_group", new Vector2(-1, 125), ImGuiChildFlags.Borders);

                        ImGui.AlignTextToFramePadding();
                        ImGui.Text("Focus: ");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1f);
                        double focusMin = -100, focusMax = 100;
                        ImGui.SliderScalar("##focus", ImGuiDataType.Double, &focus, &focusMin, &focusMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);

                        ImGui.AlignTextToFramePadding();
                        ImGui.Text("LED Brightness: ");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1f);
                        double brightnessMin = 0, brightnessMax = 100;
                        ImGui.SliderScalar("##ledbrightness", ImGuiDataType.Double, &ledBrightness, &brightnessMin, &brightnessMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);

                        if (ImGui.BeginTable("##row2", 2))
                        {
                            ImGui.TableNextColumn();
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("Frame Rate: ");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-1f);
                            int frameRateIndex = Array.IndexOf(FrameRateValues, frameRate.ToString());
                            if (ImGui.Combo("##framerate", ref frameRateIndex, FrameRateValues, FrameRateValues.Length))
                            {
                                if (Enum.TryParse<FrameRateV4>(FrameRateValues[frameRateIndex], out var result))
                                    frameRate = result;
                            }

                            ImGui.TableNextColumn();
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("Sensor Gain: ");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-1f);
                            int sensorGainIndex = Array.IndexOf(SensorGainValues, sensorGain.ToString());
                            if (ImGui.Combo("##sensorgain", ref sensorGainIndex, SensorGainValues, SensorGainValues.Length))
                            {
                                if (Enum.TryParse<GainV4>(SensorGainValues[sensorGainIndex], out var result))
                                    sensorGain = result;
                            }

                            ImGui.TableNextColumn();
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("LED Trigger: ");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-1f);
                            int digitalInIndex = Array.IndexOf(DigitalInValues, ledRespectsDigitalIn.ToString());
                            if (ImGui.Combo("##ledrespectdigitalin", ref digitalInIndex, DigitalInValues, DigitalInValues.Length))
                            {
                                if (Enum.TryParse<MiniscopeDaqDigitalIn>(DigitalInValues[digitalInIndex], out var result))
                                    ledRespectsDigitalIn = result;
                            }

                            ImGui.EndTable();
                        }

                        ImGui.EndChild();
                    }

                    ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                    if (ImGui.CollapsingHeader("File##file_header"))
                    {
                        ImGui.BeginChild("##save_group", new Vector2(-1, 245), ImGuiChildFlags.Borders);

                        ImGui.Text("File Name Template");
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("Choose the location and format to save all files.");
                            ImGui.Text("Video files will have '.avi' added the file format, and CSV files will have '.csv' added.");
                            ImGui.Text("If Suffix is set, the selected suffix will be added after the format and before the extension.");
                            ImGui.EndTooltip();
                        }

                        const string browseLabel = "...";
                        const string openLabel = "Open";
                        float browseWidth = ImGui.CalcTextSize(browseLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
                        float openWidth = ImGui.CalcTextSize(openLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
                        float inputWidth = ImGui.GetContentRegionAvail().X - browseWidth - openWidth - ImGui.GetStyle().ItemSpacing.X * 2f;

                        ImGui.SetNextItemWidth(inputWidth);
                        ImGui.InputText("##filename", ref fileName, bufSize);
                        ImGui.SameLine();
                        if (ImGui.Button($"{browseLabel}##choose_filename_button", new Vector2(browseWidth, 0)))
                        {
                            if (saveDialogTask == null || saveDialogTask.IsCompleted)
                            {
                                saveDialogTask = Task.Run(() =>
                                {
                                    string result = string.Empty;
                                    Thread t = new(() =>
                                    {
                                        SaveFileDialog dlg = new()
                                        {
                                            InitialDirectory = Path.GetFullPath(string.IsNullOrEmpty(fileName) ? "./" : fileName),
                                            Filter = "All Files|*.*",
                                            Title = "Choose where to save Miniscope data.",
                                            AddExtension = false,
                                            CheckFileExists = false,
                                            CheckPathExists = false,
                                            FileName = Path.GetFileName(fileName)
                                        };
                                        if (dlg.ShowDialog() == DialogResult.OK)
                                            result = dlg.FileName;
                                    });
                                    t.SetApartmentState(ApartmentState.STA);
                                    t.Start();
                                    t.Join();
                                    return result;
                                });
                            }
                        }

                        ImGui.SameLine();
                        if (ImGui.Button($"{openLabel}##open_folder_button", new Vector2(openWidth, 0)))
                        {
                            var dir = Path.GetDirectoryName(Path.GetFullPath(string.IsNullOrEmpty(fileName) ? "./" : fileName));
                            if (Directory.Exists(dir))
                                System.Diagnostics.Process.Start("explorer.exe", dir);
                        }

                        if (saveDialogTask != null && saveDialogTask.IsCompleted)
                        {
                            var result = saveDialogTask.Result;
                            if (!string.IsNullOrEmpty(result))
                                fileName = result;
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
                                suffix = (PathSuffix)currentPathSuffix;

                            ImGui.TableNextColumn();
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("Video Codec: ");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-1f);

                            if (ImGui.Combo("##codecs", ref codecIndex, CodecValues, CodecValues.Length))
                                videoCodec = CodecValues[codecIndex];

                            ImGui.EndTable();
                        }

                        ImGui.AlignTextToFramePadding();
                        ImGui.Text("Recording Duration [s]:");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.InputInt("##recording_duration", ref recordingDurationSeconds, ImGuiInputTextFlags.AutoSelectAll))
                        {
                            recordingDurationSeconds = Math.Max(0, recordingDurationSeconds);
                        }

                        ImGui.Checkbox("Use Recording Duration##use_record_duration", ref useRecordDuration);

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
                                ImGui.PopStyleColor();

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
                                ImGui.PopStyleColor();

                            ImGui.EndTable();
                        }

                        ImGui.Separator();

                        if (ImGui.BeginTable("##status_bar", 3))
                        {
                            ImGui.TableNextColumn();
                            ImGui.Text("Frame Rate:");

                            ImGui.TableNextColumn();
                            if (AcquisitionStatus)
                            {
                                ImGui.Text("Acquiring:");
                            }

                            ImGui.TableNextColumn();

                            if (RecordingStatus)
                            {
                                ImGui.Text("Recording:");
                            }

                            ImGui.TableNextColumn();
                            ImGui.Text($"{AverageFrameRate:F1} FPS");

                            ImGui.TableNextColumn();
                            if (AcquisitionStatus)
                            {
                                acquisitionStart ??= DateTime.Now;
                                ImGui.Text($"{(DateTime.Now - acquisitionStart.Value).TotalSeconds:F1} s");
                            }
                            else if (acquisitionStart != null)
                            {
                                acquisitionStart = null;
                            }

                            ImGui.TableNextColumn();
                            if (RecordingStatus)
                            {
                                recordingStart ??= DateTime.Now;
                                ImGui.Text($"{(DateTime.Now - recordingStart.Value).TotalSeconds:F1} s");
                            }
                            else if (recordingStart != null)
                            {
                                recordingStart = null;
                            }

                            ImGui.EndTable();
                        }

                        ImGui.EndChild();
                    }

                    if (ImGui.CollapsingHeader("Saturation##saturation_header"))
                    {
                        ImGui.BeginChild("##saturation_group", new Vector2(-1, 70), ImGuiChildFlags.Borders);

                        ImGui.Text("Threshold: ");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1f);

                        double thresholdMin = 0, thresholdMax = 255;
                        ImGui.SliderScalar("##saturation_threshold", ImGuiDataType.Double, &satThreshold, &thresholdMin, &thresholdMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);

                        ImGui.Text("Color: ");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1f);

                        if (ImGui.ColorEdit4("##saturation_color", ref satColor, ImGuiColorEditFlags.Uint8 | ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoOptions))
                        {
                            satColor.X = Math.Max(0f, Math.Min(1f, satColor.X));
                            satColor.Y = Math.Max(0f, Math.Min(1f, satColor.Y));
                            satColor.Z = Math.Max(0f, Math.Min(1f, satColor.Z));
                        }

                        ImGui.EndChild();
                    }

                    if (ImGui.CollapsingHeader("dF/F##dff_header"))
                    {
                        ImGui.BeginChild("##dff_group", new Vector2(-1, 110), ImGuiChildFlags.Borders);

                        ImGui.Text("Background Frames: ");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1f);

                        int backgroundFramesMin = 2, backgroundFramesMax = 1000;
                        if (ImGui.InputInt("##background_frames", ref backgroundFrames))
                            backgroundFrames = Math.Max(backgroundFramesMin, Math.Min(backgroundFramesMax, backgroundFrames));

                        ImGui.Text("Background Threshold: ");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1f);
                        double bgThreshMin = 0, bgThreshMax = 255;
                        ImGui.SliderScalar("##background_threshold", ImGuiDataType.Double, &backgroundThreshold, &bgThreshMin, &bgThreshMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);

                        ImGui.Text("Sigma: ");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1f);
                        if (ImGui.InputInt("##sigma", ref sigma))
                            sigma = Math.Max(0, sigma);

                        ImGui.EndChild();
                    }

                    if (ImGui.CollapsingHeader("Commutator##commutator_header"))
                    {
                        ImGui.BeginChild("##commutator_child", new Vector2(-1, 120), ImGuiChildFlags.Borders);

                        ImGui.Text("COM Port: ");
                        ImGui.SameLine();

                        var style = ImGui.GetStyle();
                        float refreshButtonWidth = ImGui.CalcTextSize("Refresh").X + style.FramePadding.X * 2;
                        float connectButtonWidth = ImGui.CalcTextSize("Disconnect").X + style.FramePadding.X * 2;
                        float comboWidth = ImGui.GetContentRegionAvail().X - refreshButtonWidth - connectButtonWidth - style.ItemSpacing.X * 2;
                        ImGui.SetNextItemWidth(comboWidth);

                        if (commutatorConnected)
                            ImGui.BeginDisabled();

                        int portIndex = Array.IndexOf(portNames, portName);
                        if (portIndex < 0) portIndex = 0;
                        if (ImGui.Combo("##comport", ref portIndex, portNames, portNames.Length) && portNames.Length > 0)
                        {
                            portName = portNames[portIndex];
                            commutatorStatus = string.Empty;
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Refresh##comrefresh"))
                        {
                            portNames = SerialPort.GetPortNames();
                            commutatorStatus = string.Empty;
                        }

                        if (commutatorConnected)
                            ImGui.EndDisabled();

                        ImGui.SameLine();
                        if (ImGui.Button(commutatorConnected ? "Disconnect##combutton" : "Connect##combutton"))
                        {
                            commutatorConnected = !commutatorConnected;
                            commutatorStatus = string.Empty;
                        }

                        if (!commutatorConnected)
                            ImGui.BeginDisabled();

                        ImGui.Checkbox("Enable##commutator_enable", ref commutatorEnable);
                        ImGui.SameLine();
                        ImGui.Checkbox("Enable LED##commutator_led", ref commutatorEnableLed);

                        if (!commutatorConnected)
                            ImGui.EndDisabled();

                        ImGui.Separator();

                        bool hasCommutatorError = !string.IsNullOrEmpty(commutatorStatus);
                        var statusColor = hasCommutatorError
                            ? new Vector4(0.9f, 0.3f, 0.3f, 1f)
                            : commutatorConnected
                                ? new Vector4(0.2f, 0.8f, 0.2f, 1f)
                                : new Vector4(0.6f, 0.6f, 0.6f, 1f);
                        ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
                        var displayStatus = hasCommutatorError
                            ? $"Status: {commutatorStatus}"
                            : commutatorConnected ? "Status: Connected" : "Status: Disconnected";
                        ImGui.Text(displayStatus);
                        ImGui.PopStyleColor();

                        ImGui.EndChild();
                    }
                }

                ImGui.EndChild();

                var updatedDto = new SettingsPanelDto(
                    new MiniscopeSettingsDto(ledBrightness, focus, sensorGain, frameRate, ledRespectsDigitalIn),
                    new FileSettingsDto(recordButton, recordOnTriggerButton, videoCodec, fileName, suffix, recordingDurationSeconds, useRecordDuration),
                    new SaturationSettingsDto(satThreshold, new Scalar(satColor.Z * 255, satColor.Y * 255, satColor.X * 255, satColor.W * 255)),
                    new DffSettingsDto(backgroundFrames, backgroundThreshold, sigma),
                    new CommutatorSettingsDto(portName, commutatorConnected, commutatorEnable, commutatorEnableLed, commutatorStatus));

                observer.OnNext(Tuple.Create(value.Item1, updatedDto));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
