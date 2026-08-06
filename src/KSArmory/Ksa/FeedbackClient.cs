using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using KSArmory.Sim;

namespace KSArmory;

/// <summary>
/// Sends a report to the endpoint that files it as a GitHub issue.
///
/// <para>The mod holds no credential. A token shipped inside a DLL is extractable in seconds and
/// would be the maintainer's, so the service holds it and this posts unauthenticated.</para>
///
/// <para>Nothing here blocks the frame. The send runs on its own task and this exposes only what
/// the panel needs to draw: whether one is in flight, and what the last one came back with.</para>
/// </summary>
internal sealed class FeedbackClient
{
    public const string Endpoint = "https://api.ksarmory.com/feedback";

    // Generous, because the endpoint scores the text with a local model before answering, and a
    // cold container is slower than a warm one.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };

    /// <summary>True while a report is in flight, so the panel can refuse a second one.</summary>
    public bool Sending { get; private set; }

    /// <summary>What the last attempt came back with, for the panel to show.</summary>
    public string? Status { get; private set; }

    /// <summary>Whether <see cref="Status"/> reports success, for colouring it.</summary>
    public bool Sent { get; private set; }

    /// <summary>Where the filed issue ended up, when the endpoint said.</summary>
    public string? IssueUrl { get; private set; }

    public void Clear()
    {
        Status = null;
        IssueUrl = null;
        Sent = false;
    }

    /// <summary>
    /// Posts a report and records the outcome. Returns immediately.
    /// </summary>
    /// <param name="log">
    /// The mod's log, already tailed to the endpoint's limit, or null to send none.
    /// </param>
    public void Send(ReportDraft draft, string? log)
    {
        if (Sending) return;

        Sending = true;
        Status = "sending...";
        Sent = false;
        IssueUrl = null;

        var wire = new Wire(
            ReportDraft.Wire(draft.Kind),
            draft.Summary.Trim(),
            draft.Detail.Trim(),
            string.IsNullOrWhiteSpace(log) ? null : log,
            Build.Version,
            Build.KsaRunning,
            Platform());

        _ = Task.Run(async () =>
        {
            try
            {
                using HttpResponseMessage response = await Http.PostAsJsonAsync(Endpoint, wire);
                Finish(StatusOf(response), await response.Content.ReadAsStringAsync());
            }
            catch (TaskCanceledException)
            {
                Fail("timed out - the endpoint did not answer");
            }
            catch (Exception e)
            {
                // The message rather than the type: "no such host" tells the player they are
                // offline, where "HttpRequestException" tells them nothing.
                Fail($"could not reach the endpoint: {e.Message}");
            }
            finally
            {
                Sending = false;
            }
        });
    }

    // Read by reflection, and that is not paranoia: calling the property directly threw
    // MissingMethodException for get_StatusCode in game, against a System.Net.Http that plainly
    // declares it. Whatever the runtime resolves the type to, reflection asks the object it
    // actually has. The location is logged once so the cause can eventually be named.
    private static int StatusOf(HttpResponseMessage response)
    {
        Type type = response.GetType();

        if (!_reportedAssembly)
        {
            _reportedAssembly = true;
            Log.Info($"http type {type.FullName} from {type.Assembly.Location} "
                     + $"({type.Assembly.GetName().Version})");
        }

        object? value = type.GetProperty("StatusCode")?.GetValue(response);
        return value is null ? 0 : Convert.ToInt32(value);
    }

    private static bool _reportedAssembly;

    private void Finish(int status, string body)
    {
        // Every one of these is a thing the endpoint deliberately says. Reporting them as "failed"
        // would hide the only part the player can act on.
        switch (status)
        {
            case 202:
                Sent = true;
                IssueUrl = Url(body);
                Status = IssueUrl is null ? "thank you - report received" : "thank you - report filed";
                Log.Info($"report accepted{(IssueUrl is null ? "" : $": {IssueUrl}")}");
                return;

            case 426:
                Fail("this version is too old to report against - please update the mod first");
                return;

            case 422:
                // The endpoint refuses for more than one reason and says which. Assuming the
                // worst of them told a player asking for more guns that their feedback read as
                // abusive, because the language check answers 422 as well.
                Fail(Reason(body) ?? "that report was not accepted - please rewrite it");
                return;

            case 429:
                Fail("too many reports from here - try again later");
                return;

            case 400:
                Fail(string.IsNullOrWhiteSpace(body) ? "the report was rejected" : Trim(body));
                return;

            default:
                Fail($"the endpoint answered {status}");
                return;
        }
    }

    private void Fail(string why)
    {
        Sent = false;
        Status = why;
        Log.Warn($"report not sent: {why}");
    }

    // The refusal reason the endpoint sent, as {"error": "..."}.
    private static string? Reason(string body)
    {
        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out System.Text.Json.JsonElement error)
                       ? error.GetString()
                       : null;
        }
        catch
        {
            return null;
        }
    }

    // The endpoint answers with {"url": "..."} on success, and with plain text if something in
    // front of it answered instead.
    private static string? Url(string body)
    {
        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("url", out System.Text.Json.JsonElement url)
                       ? url.GetString()
                       : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Trim(string body) => body.Length <= 160 ? body : body[..160];

    private static string Platform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macos";

        return "unknown";
    }

    private sealed record Wire(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("log")] string? Log,
        [property: JsonPropertyName("modVersion")] string ModVersion,
        [property: JsonPropertyName("ksaVersion")] string? KsaVersion,
        [property: JsonPropertyName("platform")] string Platform);
}
