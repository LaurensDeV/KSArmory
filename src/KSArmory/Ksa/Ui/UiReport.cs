using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSArmory.Sim;

namespace KSArmory;

/// <summary>
/// Reporting a bug or sending an idea, from inside the game.
///
/// <para>One window for both. They differ by a label and whether the log is attached by default,
/// and two windows would mean two places to look for the thing you half-wrote.</para>
/// </summary>
internal partial class Ui
{
    private readonly ReportDraft _report = new();
    private readonly FeedbackClient _feedback = new();

    private bool _reportOpen;

    // Read once when the window opens rather than every frame: it is a file on disk, and the size
    // is only there to say what attaching it will cost.
    private long _logBytes;

    // The two buttons that open it, kind already chosen.
    private void DrawReportButtons()
    {
        if (ImGui.Button("Report bug")) OpenReport(ReportKind.Bug);

        ImGui.SameLine();

        if (ImGui.Button("Feedback")) OpenReport(ReportKind.Idea);
    }

    private void OpenReport(ReportKind kind)
    {
        // Only reset what the kind decides. Someone who typed a paragraph, then realised it was an
        // idea rather than a bug, should not lose the paragraph to the switch.
        _report.Kind = kind;
        _report.AttachLog = kind == ReportKind.Bug;

        if (!_reportOpen) _feedback.Clear();

        _reportOpen = true;
        _logBytes = LogSize();
    }

    private void DrawReportWindow()
    {
        if (!_reportOpen) return;

        // ###id so the heading can say which kind it is without the window losing its place.
        string title = _report.Kind == ReportKind.Bug ? "Report a bug" : "Send feedback";

        if (ImGui.Begin($"{title}###KSArmoryReport", ref _reportOpen))
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

        ImGui.End();
    }

    private void DrawKindSwitch()
    {
        // Two buttons rather than a tick box or a dropdown: which of two things this is, with both
        // named and the current one lit.
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

        // Says what leaves the machine. The endpoint takes the last 12,000 characters and scrubs
        // home directory paths out of them, and neither is obvious from here.
        long sending = Math.Min(_logBytes, ReportDraft.MaxLog);
        ImGui.TextDisabled($"  last {sending / 1024f:F0} KB of {_logBytes / 1024f:F0} KB, paths removed");
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
            // Said rather than a dead button: a control that does nothing when clicked, with no
            // reason given, reads as broken.
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

            // Shared read: the mod has this file open for appending, and so may a tail running
            // beside it.
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

    // Four bytes per character, because the limits are in characters and UTF-8 is not one byte
    // each. A player writing in Cyrillic must not be cut off at half the length.
    private const int SummaryBytes = (ReportDraft.MaxSummary * 4) + 1;
    private const int DetailBytes = (ReportDraft.MaxDetail * 4) + 1;

    // A text field backed by a byte buffer, which is the shape this ImGui binding takes. Unlike
    // Ui.TextField it commits on every keystroke rather than on Enter: Enter belongs to the
    // multiline field, and a summary that only took effect when submitted would leave Send greyed
    // out while the box visibly has text in it.
    private static bool Field(string label, ref string value, int capacity, float2? multiline = null)
    {
        Span<byte> buffer = capacity <= 1024 ? stackalloc byte[capacity] : new byte[capacity];

        int written = System.Text.Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
        buffer[Math.Min(written, capacity - 1)] = 0;

        bool changed;
        if (multiline is null)
        {
            changed = ImGui.InputText(label, buffer, ImGuiInputTextFlags.None, null, default);
        }
        else
        {
            float2? size = multiline;
            changed = ImGui.InputTextMultiline(label, buffer, ref size, ImGuiInputTextFlags.None,
                                               null, default);
        }

        if (!changed) return false;

        int end = buffer.IndexOf((byte)0);
        value = System.Text.Encoding.UTF8.GetString(buffer[..(end < 0 ? buffer.Length : end)]);
        return true;
    }
}
