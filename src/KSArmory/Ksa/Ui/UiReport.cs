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

    // Pinned to the bottom of the panel, not left to float after whatever was drawn above. The
    // system list and the debug tree both change height, so without this the buttons move every
    // time a craft is crewed or a section is folded.
    private void DrawReportFooter()
    {
        float footer = ImGui.GetFrameHeight() + Spacing;
        float slack = ImGui.GetContentRegionAvail().Y - footer;

        // Only ever pushes down. When the window is shorter than its contents the buttons sit
        // directly after them and the window scrolls, rather than being dragged up over the list.
        if (slack > 0f) ImGui.Dummy(new float2(0f, slack));

        ImGui.Separator();

        // Both or neither. Leaving feedback open on an unsupported build would just make it the
        // way to file a bug report. Nothing takes their place: the reason is in the log, which is
        // where an explanation belongs rather than in a panel the player reads every session.
        if (ReportDraft.GameIsSupported(Build.KsaBuild, Build.KsaRunning)) DrawReportButtons();
    }

    // Roughly the separator plus the spacing either side of it. Exact enough: being a pixel out
    // moves the buttons by a pixel, and being wrong the other way would clip them.
    private const float Spacing = 12f;

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

        // Always, so the panel buttons mean "write one" whatever is on screen. Without this,
        // pressing them while the thank-you is up leaves the thank-you up and they do nothing
        // visible.
        _feedback.Clear();

        _reportOpen = true;
        _logBytes = LogSize();
    }

    private void DrawReportWindow()
    {
        // Checked here as well as at the buttons: the window outlives the click that opened it,
        // and hiding only the buttons would leave a way to send from one already on screen.
        if (!ReportDraft.GameIsSupported(Build.KsaBuild, Build.KsaRunning)) _reportOpen = false;

        if (!_reportOpen) return;

        // ###id so the heading can say which kind it is without the window losing its place.
        string title = _report.Kind == ReportKind.Bug ? "Report a bug" : "Send feedback";

        if (ImGui.Begin($"{title}###KSArmoryReport", ref _reportOpen))
        {
            // Once it is filed there is nothing left to do, so the form goes rather than sitting
            // there inviting a second identical report from someone who is not sure it worked.
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
        // Cleared here rather than at send: the text is what the thank-you is about until it is
        // shown, and leaving it would offer the same report back for a second send.
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

        // Deliberate, and empty: someone with a second thing to report should not have to hunt
        // for the way back, and should not be handed the previous one to resend.
        if (ImGui.Button("Write another")) _feedback.Clear();
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
