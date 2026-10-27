using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Reporting a bug or sending an idea, in one window: they differ by a label and the log's default.
/// </summary>
internal partial class Ui
{
    private readonly ReportDraft _report = new();
    private readonly FeedbackClient _feedback = new();

    private bool _reportOpen;

    // Read once when the window opens: it is a file on disk.
    private long _logBytes;

    // Pinned to the bottom, so the buttons do not move as the list above changes height.
    private void DrawReportFooter()
    {
        float footer = ImGui.GetFrameHeight() + Spacing;
        float slack = ImGui.GetContentRegionAvail().Y - footer;

        // Only ever pushes down; a short window scrolls rather than overlapping the list.
        if (slack > 0f) ImGui.Dummy(new float2(0f, slack));

        ImGui.Separator();

        // Both report buttons or neither: feedback left open on an unsupported build becomes the
        // way to file a bug. The reason is in the log.
        DrawPaneToggles();
        if (ReportDraft.GameIsSupported(Build.KsaBuild, Build.KsaRunning)) DrawReportButtons();
    }

    // Roughly the separator plus the spacing either side of it.
    private const float Spacing = 12f;

    private void DrawReportButtons()
    {
        float padding = ImGui.GetStyle().FramePadding.X * 2f;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float width = ImGui.CalcTextSize("Report bug").X + ImGui.CalcTextSize("Feedback").X
                      + (2f * padding) + spacing;

        ImGui.SameLine();
        float slack = ImGui.GetContentRegionAvail().X - width;
        if (slack > 0f) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + slack);

        if (ImGui.Button("Report bug")) OpenReport(ReportKind.Bug);

        ImGui.SameLine();

        if (ImGui.Button("Feedback")) OpenReport(ReportKind.Idea);
    }

    private void OpenReport(ReportKind kind)
    {
        // Only what the kind decides: switching kind keeps the text.
        _report.Kind = kind;
        _report.AttachLog = kind == ReportKind.Bug;

        // Or pressing them while the thank-you is up does nothing visible.
        _feedback.Clear();

        _reportOpen = true;
        _logBytes = LogSize();
    }

    private void DrawReportWindow()
    {
        // As well as at the buttons: the window outlives the click that opened it.
        if (!ReportDraft.GameIsSupported(Build.KsaBuild, Build.KsaRunning)) _reportOpen = false;

        if (!_reportOpen) return;

        // ###id so the title can change without the window moving.
        string title = _report.Kind == ReportKind.Bug ? "Report a bug" : "Send feedback";

        if (ImGui.Begin($"{title}###KSArmoryReport", ref _reportOpen))
        {
            if (_feedback.Sent) DrawThanks();
            else DrawReportForm();
        }

        ImGui.End();
    }

    private void DrawReportForm()
    {
        DrawKindSwitch();
        ImGui.Separator();

        ImGui.TextDisabled(_report.Kind == ReportKind.Bug
                               ? "What went wrong, and what you were doing at the time."
                               : "What you would like this to do.");

        ImGui.SetNextItemWidth(420f);
        Field("Summary", ref _report.Summary, SummaryBytes);

        Field("Detail", ref _report.Detail, DetailBytes, new float2(420f, 120f));

        DrawLogAttachment();
        ImGui.Separator();

        DrawSendButton();
        DrawReportStatus();
    }

    private void DrawThanks()
    {
        // Here, not at send: the thank-you shows the text until now.
        _report.Summary = string.Empty;
        _report.Detail = string.Empty;

        ImGui.TextColored(Green, _feedback.Status ?? "thank you - report filed");

        if (_feedback.IssueUrl is not null)
        {
            ImGui.TextDisabled("It is now an issue on GitHub:");
            ImGui.TextDisabled(_feedback.IssueUrl);
        }

        ImGui.Separator();
        ImGui.TextDisabled("Nothing else is needed. Sending it twice does not help.");

        if (ImGui.Button("Close")) _reportOpen = false;

        ImGui.SameLine();

        if (ImGui.Button("Write another")) _feedback.Clear();
    }

    private void DrawKindSwitch()
    {
        bool bug = _report.Kind == ReportKind.Bug;

        if (bug) ImGui.PushStyleColor(ImGuiCol.Button, Selected);
        if (ImGui.Button("Bug report")) OpenReport(ReportKind.Bug);
        if (bug) ImGui.PopStyleColor();

        ImGui.SameLine();

        if (!bug) ImGui.PushStyleColor(ImGuiCol.Button, Selected);
        if (ImGui.Button("Feedback##kind")) OpenReport(ReportKind.Idea);
        if (!bug) ImGui.PopStyleColor();
    }

    private void DrawLogAttachment()
    {
        if (_logBytes <= 0)
        {
            ImGui.TextDisabled("no log to attach");
            return;
        }

        ImGui.Checkbox("Attach the mod's log", ref _report.AttachLog);

        if (!_report.AttachLog) return;

        // "Published", not "sent": the endpoint replaces the user name in home-folder paths.
        long sending = Math.Min(_logBytes, ReportDraft.MaxLog);
        ImGui.TextDisabled($"  last {sending / 1024f:F0} KB of {_logBytes / 1024f:F0} KB, "
                           + "user names in paths hidden when published");
    }

    private void DrawSendButton()
    {
        string? problem = ReportDraft.Problem(_report.Summary, _report.Detail);

        if (_feedback.Sending)
        {
            ImGui.TextDisabled("sending...");
            return;
        }

        if (problem is not null)
        {
            ImGui.TextDisabled(problem);
            return;
        }

        if (ImGui.Button("Send")) _feedback.Send(_report, _report.AttachLog ? ReadLogTail() : null);

        ImGui.SameLine();

        if (ImGui.Button("Clear"))
        {
            _report.Summary = string.Empty;
            _report.Detail = string.Empty;
            _feedback.Clear();
        }
    }

    private void DrawReportStatus()
    {
        if (_feedback.Status is null) return;

        ImGui.Separator();
        ImGui.TextColored(_feedback.Sent ? Green : Amber, _feedback.Status);

        if (_feedback.IssueUrl is not null) ImGui.TextDisabled(_feedback.IssueUrl);
    }

    private static long LogSize()
    {
        try
        {
            string? path = Log.FilePath;
            return path is null ? 0 : new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string? ReadLogTail()
    {
        try
        {
            string? path = Log.FilePath;
            if (path is null) return null;

            // Shared: the mod has this file open for appending, and a tail may too.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            return ReportDraft.Tail(reader.ReadToEnd());
        }
        catch (Exception e)
        {
            Log.Warn($"could not read the log to attach it: {e.Message}");
            return null;
        }
    }

    private static readonly float4 Selected = new(0.20f, 0.45f, 0.25f, 1f);

    // Four bytes per character: the limits are in characters, and UTF-8 runs up to four.
    private const int SummaryBytes = (ReportDraft.MaxSummary * 4) + 1;
    private const int DetailBytes = (ReportDraft.MaxDetail * 4) + 1;

    // Unlike TextField, commits on every keystroke: Enter belongs to the multiline field, and Send
    // would stay greyed out while the box has text in it.
    private static bool Field(string label, ref string value, int capacity, float2? multiline = null)
    {
        Span<byte> buffer = capacity <= 1024 ? stackalloc byte[capacity] : new byte[capacity];

        int written = System.Text.Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
        buffer[Math.Min(written, capacity - 1)] = 0;

        bool changed;
        if (multiline is null)
        {
            changed = ImGui.InputText(label, buffer, ImGuiInputTextFlags.None);
        }
        else
        {
            float2? size = multiline;
            changed = ImGui.InputTextMultiline(label, buffer, in size, ImGuiInputTextFlags.None);
        }

        if (!changed) return false;

        int end = buffer.IndexOf((byte)0);
        value = System.Text.Encoding.UTF8.GetString(buffer[..(end < 0 ? buffer.Length : end)]);
        return true;
    }
}
