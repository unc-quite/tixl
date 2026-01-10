#nullable enable
using System.IO;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Core.Utils;
using T3.Core.Animation;
using T3.Editor.UiModel.ProjectHandling;
using System.Threading.Tasks;

namespace T3.Editor.Gui.Windows.RenderExport;

internal sealed class RenderWindow : Window
{
    public RenderWindow()
    {
        Config.Title = "Render To File";
    }

    protected override void DrawContent()
    {
        FormInputs.AddVerticalSpace(15);
        DrawTimeSetup();
        
        // FormInputs.AddVerticalSpace(10);
        DrawInnerContent();
        
    }

    private void DrawInnerContent()
    {
        if (RenderProcess.State == RenderProcess.States.NoOutputWindow)
        {
            _lastHelpString = "No output view available";
            CustomComponents.HelpText(_lastHelpString);
            return;
        }

        if (RenderProcess.State == RenderProcess.States.NoValidOutputType)
        {
            _lastHelpString = RenderProcess.MainOutputType == null
                                  ? "The output view is empty"
                                  : "Select or pin a Symbol with Texture2D output in order to render to file";
            ImGui.Button("Start Render", new Vector2(-1, 0));
            CustomComponents.TooltipForLastItem("Only Symbols with a texture2D output can be rendered to file");
            ImGui.EndDisabled();
            CustomComponents.HelpText(_lastHelpString);
            return;
        }

        _lastHelpString = "Ready to render.";

        FormInputs.AddVerticalSpace();
        FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.RenderMode, "Render Mode");

        FormInputs.AddVerticalSpace();

        if (RenderSettings.RenderMode == RenderSettings.RenderModes.Video)
            DrawVideoSettings(RenderProcess.MainOutputRenderedSize);
        else
            DrawImageSequenceSettings();

        FormInputs.AddVerticalSpace(10);
        
        // Final Summary Card
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.12f, 0.12f, 0.12f, 0.6f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        
        ImGui.BeginChild("Summary", new Vector2(-1, 85 * T3Ui.UiScaleFactor), false, ImGuiWindowFlags.NoScrollbar);
        DrawRenderSummary();
        ImGui.EndChild();
        
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        FormInputs.AddVerticalSpace(10);
        DrawRenderingControls();
        DrawOverwriteDialog();

        CustomComponents.HelpText(RenderProcess.IsExporting ? RenderProcess.LastHelpString : _lastHelpString);
    }

    private static void DrawTimeSetup()
    {
        FormInputs.SetIndentToParameters();

        // Range row
        FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.TimeRange, "Range");
        RenderTiming.ApplyTimeRange(RenderSettings.TimeRange, RenderSettings);
        
        // Scale row (now under Range)
        var oldRef = RenderSettings.Reference;
        if (FormInputs.AddSegmentedButtonWithLabel(ref RenderSettings.Reference, "Scale"))
        {
            RenderSettings.StartInBars =
                (float)RenderTiming.ConvertReferenceTime(RenderSettings.StartInBars, oldRef, RenderSettings.Reference, RenderSettings.Fps);
            RenderSettings.EndInBars = (float)RenderTiming.ConvertReferenceTime(RenderSettings.EndInBars, oldRef, RenderSettings.Reference, RenderSettings.Fps);
        }

        FormInputs.AddVerticalSpace(5);

        // Start and End on separate rows (standard style)
        var changed = FormInputs.AddFloat($"Start ({RenderSettings.Reference})", ref RenderSettings.StartInBars, 0, float.MaxValue, 0.1f, true);
        changed |= FormInputs.AddFloat($"End ({RenderSettings.Reference})", ref RenderSettings.EndInBars, 0, float.MaxValue, 0.1f, true);
        
        if (changed)
            RenderSettings.TimeRange = RenderSettings.TimeRanges.Custom;

        FormInputs.AddVerticalSpace(5);

        // FPS row
        if (FormInputs.AddFloat("FPS", ref RenderSettings.Fps, 1, 120, 0.1f, true))
        {
            RenderSettings.StartInBars = (float)RenderTiming.ConvertFps(RenderSettings.StartInBars, _lastValidFps, RenderSettings.Fps);
            RenderSettings.EndInBars = (float)RenderTiming.ConvertFps(RenderSettings.EndInBars, _lastValidFps, RenderSettings.Fps);
            _lastValidFps = RenderSettings.Fps;
        }

        // Resolution row
        FormInputs.DrawInputLabel("Resolution");
        var resSize = FormInputs.GetAvailableInputSize(null, false, true);
        DrawResolutionPopoverCompact(resSize.X); 
        
        FormInputs.AddVerticalSpace(10);

        RenderSettings.FrameCount = RenderTiming.ComputeFrameCount(RenderSettings);

        FormInputs.AddVerticalSpace(5);
        
        // Motion Blur Samples
        if (FormInputs.AddInt("Motion Blur", ref RenderSettings.OverrideMotionBlurSamples, -1, 50, 1,
                              "Number of motion blur samples. Set to -1 to disable. Requires [RenderWithMotionBlur] operator."))
        {
            RenderSettings.OverrideMotionBlurSamples = Math.Clamp(RenderSettings.OverrideMotionBlurSamples, -1, 50);
        }

        // Show hint when motion blur is disabled
        if (RenderSettings.OverrideMotionBlurSamples == -1)
        {
            FormInputs.AddHint("Motion blur disabled. (Use samples > 0 and [RenderWithMotionBlur])");
        }
    }

    private static void DrawResolutionPopoverCompact(float width)
    {
        var currentPct = (int)(RenderSettings.ResolutionFactor * 100);
        ImGui.SetNextItemWidth(width);
        
        if (ImGui.Button($"{currentPct}%##Res", new Vector2(width, 0)))
        {
            ImGui.OpenPopup("ResolutionPopover");
        }
        CustomComponents.TooltipForLastItem("Scale resolution of rendered frames.");

        if (ImGui.BeginPopup("ResolutionPopover"))
        {
            if (ImGui.Selectable("25%", currentPct == 25)) RenderSettings.ResolutionFactor = 0.25f;
            if (ImGui.Selectable("50%", currentPct == 50)) RenderSettings.ResolutionFactor = 0.5f;
            if (ImGui.Selectable("100%", currentPct == 100)) RenderSettings.ResolutionFactor = 1.0f;
            if (ImGui.Selectable("200%", currentPct == 200)) RenderSettings.ResolutionFactor = 2.0f;

            CustomComponents.SeparatorLine();
            ImGui.TextUnformatted("Custom:");
            var customPct = RenderSettings.ResolutionFactor * 100f;
            if (ImGui.InputFloat("##CustomRes", ref customPct, 0, 0, "%.0f%%"))
            {
                customPct = Math.Clamp(customPct, 1f, 400f);
                RenderSettings.ResolutionFactor = customPct / 100f;
            }
            ImGui.EndPopup();
        }
    }

    private void DrawVideoSettings(Int2 size)
    {
        // Bitrate in Mbps
        var bitrateMbps = RenderSettings.Bitrate / 1_000_000f;
        if (FormInputs.AddFloat("Bitrate", ref bitrateMbps, 0.1f, 500f, 0.5f, true, true,
                                "Video bitrate in megabits per second."))
        {
            RenderSettings.Bitrate = (int)(bitrateMbps * 1_000_000f);
        }

        var startSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.StartInBars, RenderSettings.Reference, RenderSettings.Fps);
        var endSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.EndInBars, RenderSettings.Reference, RenderSettings.Fps);
        var duration = Math.Max(0, endSec - startSec);

        double bpp = size.Width <= 0 || size.Height <= 0 || RenderSettings.Fps <= 0
                         ? 0
                         : RenderSettings.Bitrate / (double)(size.Width * size.Height) / RenderSettings.Fps;

        var q = GetQualityLevelFromRate((float)bpp);
        FormInputs.AddHint($"{q.Title} quality (Est. {RenderSettings.Bitrate * duration / 1024 / 1024 / 8:0.#} MB)");
        CustomComponents.TooltipForLastItem(q.Description);

        // Path
        var currentPath = UserSettings.Config.RenderVideoFilePath ?? "./Render/render-v01.mp4";
        var directory = Path.GetDirectoryName(currentPath) ?? "./Render";
        var filename = Path.GetFileName(currentPath) ?? "render-v01.mp4";

        FormInputs.AddFilePicker("Folder", ref directory!, ".\\Render", null, "Save folder.", FileOperations.FilePickerTypes.Folder);

        if (FormInputs.AddStringInput("Filename", ref filename))
        {
            filename = (filename ?? string.Empty).Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) filename = filename.Replace(c, '_');
        }

        if (!filename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) filename += ".mp4";
        UserSettings.Config.RenderVideoFilePath = Path.Combine(directory, filename);

        if (RenderPaths.IsFilenameIncrementable())
        {
            FormInputs.AddCheckBox("Auto-increment version", ref RenderSettings.AutoIncrementVersionNumber);
        }

        FormInputs.AddCheckBox("Export Audio", ref RenderSettings.ExportAudio);
    }

    private static void DrawImageSequenceSettings()
    {
        FormInputs.AddEnumDropdown(ref RenderSettings.FileFormat, "Format");

        if (FormInputs.AddStringInput("Name", ref UserSettings.Config.RenderSequenceFileName))
        {
            UserSettings.Config.RenderSequenceFileName = (UserSettings.Config.RenderSequenceFileName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(UserSettings.Config.RenderSequenceFileName)) UserSettings.Config.RenderSequenceFileName = "output";
        }

        FormInputs.AddFilePicker("Folder", ref UserSettings.Config.RenderSequenceFilePath!, ".\\ImageSequence ", null, "Save folder.", FileOperations.FilePickerTypes.Folder);
    }

    private void DrawRenderSummary()
    {
        var size = RenderProcess.MainOutputOriginalSize;
        var scaledWidth = (int)(size.Width * RenderSettings.ResolutionFactor);
        var scaledHeight = (int)(size.Height * RenderSettings.ResolutionFactor);

        var startSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.StartInBars, RenderSettings.Reference, RenderSettings.Fps);
        var endSec = RenderTiming.ReferenceTimeToSeconds(RenderSettings.EndInBars, RenderSettings.Reference, RenderSettings.Fps);
        var duration = Math.Max(0, endSec - startSec);

        var outputPath = GetCachedTargetFilePath(RenderSettings.RenderMode);
        string format;
        if (RenderSettings.RenderMode == RenderSettings.RenderModes.Video)
        {
            format = "MP4 Video";
        }
        else
        {
            format = $"{RenderSettings.FileFormat} Sequence";
        }

        ImGui.Unindent(5);
        ImGui.Indent(5);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted($"{format} • {scaledWidth}×{scaledHeight} @ {RenderSettings.Fps:0}fps");
        ImGui.TextUnformatted($"{duration / 60:0}:{duration % 60:00.0}s ({RenderSettings.FrameCount} frames)");
        
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.TextWrapped($"-> {outputPath}");
        ImGui.PopFont();
        
        ImGui.PopStyleColor();
        ImGui.Unindent(5);
    }
    
    private static string _cachedTargetPath = string.Empty;
    private static double _lastPathUpdateTime = -1;

    private static string GetCachedTargetFilePath(RenderSettings.RenderModes mode)
    {
        var now = Playback.RunTimeInSecs;
        if (now - _lastPathUpdateTime < 0.2 && !string.IsNullOrEmpty(_cachedTargetPath))
            return _cachedTargetPath;

        _cachedTargetPath = RenderPaths.GetTargetFilePath(mode);
        _lastPathUpdateTime = now;
        return _cachedTargetPath;
    }

    private static void DrawRenderingControls()
    {
        if (!RenderProcess.IsExporting && !RenderProcess.IsToollRenderingSomething)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundActive.Fade(0.7f).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundActive.Rgba);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
            
            if (ImGui.Button("Start Render", new Vector2(-1, 36 * T3Ui.UiScaleFactor)))
            {
                var targetPath = GetCachedTargetFilePath(RenderSettings.RenderMode);
                if (RenderPaths.FileExists(targetPath))
                {
                    _showOverwriteModal = true;
                }
                else
                {
                    RenderProcess.TryStart(RenderSettings);
                }
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(2);
        }
        else if (RenderProcess.IsExporting)
        {
            var progress = (float)RenderProcess.Progress;
            var elapsed = Playback.RunTimeInSecs - RenderProcess.ExportStartedTimeLocal;

            string timeRemainingStr = "Calculating...";
            if (progress > 0.01)
            {
                var estimatedTotal = elapsed / progress;
                var remaining = estimatedTotal - elapsed;
                timeRemainingStr = StringUtils.HumanReadableDurationFromSeconds(remaining) + " remaining";
            }

            ImGui.ProgressBar(progress, new Vector2(-1, 16 * T3Ui.UiScaleFactor), $"{progress * 100:0}%");

            ImGui.PushFont(Fonts.FontSmall);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted(timeRemainingStr);
            ImGui.PopStyleColor();
            ImGui.PopFont();

            FormInputs.AddVerticalSpace(5);
            if (ImGui.Button("Cancel Render", new Vector2(-1, 24 * T3Ui.UiScaleFactor)))
            {
                RenderProcess.Cancel($"Render cancelled after {StringUtils.HumanReadableDurationFromSeconds(elapsed)}");
            }
        }
    }

    private static void DrawOverwriteDialog()
    {
        if (_showOverwriteModal)
        {
            ImGui.OpenPopup("Overwrite?");
            _showOverwriteModal = false;
        }

        if (ImGui.BeginPopupModal("Overwrite?", ref _dummyOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var targetPath = GetCachedTargetFilePath(RenderSettings.RenderMode);
            ImGui.TextUnformatted($"The file '{Path.GetFileName(targetPath)}' already exists.\nDo you want to overwrite it?");
            FormInputs.AddVerticalSpace(10);

            if (ImGui.Button("Overwrite", new Vector2(100, 0)))
            {
                RenderProcess.TryStart(RenderSettings);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private static bool _showOverwriteModal;
    private static bool _dummyOpen = true;



    // Helpers
    private RenderSettings.QualityLevel GetQualityLevelFromRate(float bitsPerPixelSecond)
    {
        RenderSettings.QualityLevel q = default;
        for (var i = _qualityLevels.Length - 1; i >= 0; i--)
        {
            q = _qualityLevels[i];
            if (q.MinBitsPerPixelSecond < bitsPerPixelSecond)
                break;
        }

        return q;
    }

    internal override List<Window> GetInstances() => [];

    private static string _lastHelpString = string.Empty;
    private static float _lastValidFps = RenderSettings.Fps;
    private static RenderSettings RenderSettings => RenderSettings.Current;

    private readonly RenderSettings.QualityLevel[] _qualityLevels =
        {
            new(0.01, "Poor", "Very low quality. Consider lower resolution."),
            new(0.02, "Low", "Probable strong artifacts"),
            new(0.05, "Medium", "Will exhibit artifacts in noisy regions"),
            new(0.08, "Okay", "Compromise between filesize and quality"),
            new(0.12, "Good", "Good quality. Probably sufficient for YouTube."),
            new(0.5, "Very good", "Excellent quality, but large."),
            new(1, "Reference", "Indistinguishable. Very large files."),
        };
}