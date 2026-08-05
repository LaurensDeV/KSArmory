using System.Globalization;
using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace KSArmory.Feedback;


/// <summary>
/// Takes a report from inside the game and files it as a GitHub issue.
///
/// <para>The whole reason this exists rather than the mod calling GitHub directly: a token shipped
/// inside a mod is extractable from the DLL in seconds, and it would be the maintainer's. Here it
/// stays on the server and the mod holds nothing.</para>
/// </summary>
public static class Program
{
    // A report is text a stranger wrote, rendered on a public page. Every limit below exists to
    // bound what that can do.
    private const int MaxSummary = 120;
    private const int MaxDetail = 4_000;
    private const int MaxLog = 12_000;
    private const int MaxField = 60;
    private const long MaxBodyBytes = 64 * 1024;

    // A ceiling on the whole service, not on one caller. Per-address rate limiting does nothing
    // against a flood from many addresses, and the worst case worth bounding is not "one person is
    // rude" but "the repository has ten thousand issues by morning".
    private const int MaxIssuesPerDay = 60;

    // How long the same report is treated as already filed. Long enough to absorb a retry loop or
    // a burst, short enough that a real recurring bug can be reported again tomorrow.
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(6);

    private static readonly Dictionary<string, DateTimeOffset> _recent = [];
    private static readonly Lock _gate = new();
    private static int _filedToday;
    private static DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Refusing an oversized body at the socket, before any of it is read into memory.
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxBodyBytes);

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Behind Caddy every request arrives from the proxy, so without this the rate limiter
            // partitions on one address and the per-client limit becomes a global one. The proxy
            // is on the same Docker network and its address is not known ahead of time, hence the
            // cleared lists rather than an allowlist.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                    }));
        });

        builder.Services.AddHttpClient("moderation", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/");

            // Short: this sits in the request path, and a slow moderator must not become a slow
            // endpoint. A timeout is treated the same as any other failure below.
            client.Timeout = TimeSpan.FromSeconds(6);
        });

        builder.Services.AddHttpClient("github", client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KSArmory-feedback");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        WebApplication app = builder.Build();

        // Loaded once, at start: a model read per request would cost more than the inference.
        Classifier? classifier = Classifier.TryLoad(
            builder.Configuration["CLASSIFIER_DIR"] ?? "/app/model",
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("classifier"));

        app.UseForwardedHeaders();
        app.UseRateLimiter();

        app.MapGet("/health", () => Results.Ok(new { ok = true }));

        app.MapPost("/feedback", async (
            Report report,
            IHttpClientFactory factory,
            IConfiguration config,
            ILoggerFactory loggers,
            CancellationToken cancellation) =>
        {
            ILogger log = loggers.CreateLogger("feedback");

            if (Validate(report) is { } complaint)
            {
                return Results.BadRequest(new { error = complaint });
            }

            // Ahead of the classifier, which is English-only: scoring Dutch with an English model
            // produces a number that means nothing, and acting on it would be worse than not
            // scoring at all. It is also what everyone triaging these can read.
            if (!Guard.LooksEnglish($"{report.Summary} {report.Detail}"))
            {
                return Results.Json(
                    new { error = "reports need to be in English" },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            // Optional, and only a speed bump: the mod ships it, so anyone can read it out of the
            // DLL. It costs a casual script the trouble of looking, which is most of them.
            string? expected = config["FEEDBACK_SECRET"];
            if (!string.IsNullOrWhiteSpace(expected)
                && !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(report.Secret ?? ""),
                    System.Text.Encoding.UTF8.GetBytes(expected)))
            {
                return Results.Unauthorized();
            }

            // A report against a version two releases old is usually a bug that is already fixed,
            // and the reporter cannot know that. Refusing with the number they need is the only
            // answer that helps them; accepting it silently helps nobody.
            string? minimum = config["MIN_MOD_VERSION"];
            if (!Guard.IsAtLeast(report.ModVersion, minimum))
            {
                return Results.Json(
                    new { error = "out of date", required = minimum, yours = report.ModVersion },
                    statusCode: StatusCodes.Status426UpgradeRequired);
            }

            // Only what a person wrote. The log is machine output, and sending it would be both
            // pointless and a much larger disclosure than the reporter intended.
            string written = $"{report.Summary}\n{report.Detail}";

            // Local first: no key, no quota, no third party, and nothing leaves the machine. The
            // hosted path is the fallback for a deployment without the model baked in.
            Verdict verdict = classifier is not null
                ? Judge(classifier, written, config, log)
                : await Moderate(written, factory, config, log, cancellation);

            if (verdict == Verdict.Refused)
            {
                // Said plainly rather than silently dropped: a false positive on a real bug report
                // is possible, and someone who is told can rephrase. A flooder learns nothing
                // useful from it either.
                return Results.Json(
                    new { error = "that reads as abusive; please rewrite it" },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            string fingerprint = Guard.Fingerprint(report.Summary, report.Detail);
            if (!TryReserve(fingerprint, out string refusal))
            {
                log.LogInformation("declined a report: {Reason}", refusal);

                // Not an error the caller can act on, and not worth telling a flooder which limit
                // it hit. The mod says the report was received either way.
                return Results.Accepted();
            }

            string? token = config["GITHUB_TOKEN"];
            string? repo = config["GITHUB_REPOSITORY"];
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(repo))
            {
                // Configuration, not the caller's problem. Say so in the log and nowhere else.
                log.LogError("GITHUB_TOKEN or GITHUB_REPOSITORY is not set");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            HttpClient github = factory.CreateClient("github");
            github.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var issue = new
            {
                title = Title(report),
                body = Body(report),
                labels = verdict == Verdict.Unchecked
                    ? new[] { "from-game", report.Kind == "idea" ? "enhancement" : "bug", "unmoderated" }
                    : new[] { "from-game", report.Kind == "idea" ? "enhancement" : "bug" },
            };

            HttpResponseMessage response = await github.PostAsJsonAsync(
                $"repos/{repo}/issues", issue, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                log.LogError("github rejected the issue: {Status}", response.StatusCode);
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            using JsonDocument created = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellation));

            string url = created.RootElement.TryGetProperty("html_url", out JsonElement u)
                             ? u.GetString() ?? ""
                             : "";

            return Results.Accepted(url, new { url });
        })
        .WithRequestTimeout(TimeSpan.FromSeconds(20));

        app.Run();
    }

    /// <summary>
    /// Scores text with the local model and turns the number into a verdict.
    ///
    /// <para>The threshold is configurable because it is a judgement, not a fact: 0.8 refuses
    /// abuse while letting through a report that calls the mod rubbish, which is a thing someone
    /// with a real bug might well write.</para>
    /// </summary>
    private static Verdict Judge(Classifier classifier, string text, IConfiguration config, ILogger log)
    {
        try
        {
            float threshold = float.TryParse(config["CLASSIFIER_THRESHOLD"], out float t) ? t : 0.8f;
            (string label, float score) = classifier.Worst(text);

            if (score < threshold) return Verdict.Allowed;

            log.LogInformation("refused a report: {Label} at {Score:F2}", label, score);
            return Verdict.Refused;
        }
        catch (Exception e)
        {
            // Same posture as an unreachable hosted moderator: file it, and label it so the gap
            // is visible rather than silent.
            log.LogWarning("the classifier failed: {Message}", e.Message);
            return Verdict.Unchecked;
        }
    }

    /// <summary>What the moderator said, or that it never answered.</summary>
    private enum Verdict
    {
        /// <summary>Checked and acceptable, or no moderator configured.</summary>
        Allowed,

        /// <summary>Checked and flagged.</summary>
        Refused,

        /// <summary>The moderator could not be reached. Filed, and labelled as such.</summary>
        Unchecked,
    }

    /// <summary>
    /// Asks OpenAI's moderation endpoint whether text is abusive.
    ///
    /// <para>Fails <b>soft</b>: an outage there must not stop bug reports arriving, so an
    /// unreachable moderator files the issue with an <c>unmoderated</c> label instead. Failing
    /// closed would make someone else's availability decide whether this works; failing silently
    /// open would let an outage quietly publish anything.</para>
    /// </summary>
    private static async Task<Verdict> Moderate(
        string text,
        IHttpClientFactory factory,
        IConfiguration config,
        ILogger log,
        CancellationToken cancellation)
    {
        string? key = config["MODERATION_API_KEY"];
        if (string.IsNullOrWhiteSpace(key)) return Verdict.Allowed;

        try
        {
            HttpClient client = factory.CreateClient("moderation");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

            HttpResponseMessage response = await client.PostAsJsonAsync(
                "v1/moderations",
                new { model = "omni-moderation-latest", input = text },
                cancellation);

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("moderation returned {Status}", response.StatusCode);
                return Verdict.Unchecked;
            }

            using JsonDocument body = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellation));

            bool flagged = body.RootElement
                .GetProperty("results")[0]
                .GetProperty("flagged")
                .GetBoolean();

            return flagged ? Verdict.Refused : Verdict.Allowed;
        }
        catch (Exception e)
        {
            log.LogWarning("moderation unreachable: {Message}", e.Message);
            return Verdict.Unchecked;
        }
    }

    /// <summary>
    /// Takes one of the day's issue slots, unless this report is a duplicate or the day is spent.
    /// </summary>
    private static bool TryReserve(string fingerprint, out string refusal)
    {
        lock (_gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
            if (today != _today)
            {
                _today = today;
                _filedToday = 0;
            }

            foreach (string stale in _recent.Where(e => now - e.Value > DuplicateWindow)
                                            .Select(e => e.Key).ToList())
            {
                _recent.Remove(stale);
            }

            if (_recent.ContainsKey(fingerprint))
            {
                refusal = "already filed recently";
                return false;
            }

            if (_filedToday >= MaxIssuesPerDay)
            {
                refusal = "the day's issue ceiling is spent";
                return false;
            }

            _recent[fingerprint] = now;
            _filedToday++;
            refusal = string.Empty;
            return true;
        }
    }

    /// <summary>A complaint to return, or null when the report is acceptable.</summary>
    private static string? Validate(Report report)
    {
        if (report is null) return "no report";
        if (report.Kind is not ("bug" or "idea")) return "kind must be 'bug' or 'idea'";

        if (string.IsNullOrWhiteSpace(report.Summary)) return "summary is required";
        if (report.Summary.Length > MaxSummary) return $"summary must be under {MaxSummary} characters";
        if (report.Detail?.Length > MaxDetail) return $"detail must be under {MaxDetail} characters";
        if (report.Log?.Length > MaxLog) return $"log must be under {MaxLog} characters";

        if (Guard.LooksLikeMash(report.Summary)) return "summary needs to say something";

        return null;
    }

    // Newlines in a title turn one report into something that reads like several.
    private static string Title(Report report)
    {
        string summary = Collapse(Guard.Clean(report.Summary, MaxSummary), MaxSummary);
        return report.Kind == "idea" ? $"[idea] {summary}" : $"[bug] {summary}";
    }

    /// <summary>
    /// Builds the issue body.
    ///
    /// <para>Everything the reporter wrote goes inside a fenced block. That is not formatting: a
    /// mention in an issue body notifies the person named, so unfenced text is a way to make this
    /// server ping strangers. Fencing also stops markdown and HTML in a report rendering as
    /// anything but what was typed.</para>
    /// </summary>
    private static string Body(Report report)
    {
        var body = new System.Text.StringBuilder();

        body.AppendLine("Filed from inside the game.").AppendLine();

        body.AppendLine("| | |").AppendLine("| --- | --- |");
        body.Append("| Mod | ").Append(Cell(report.ModVersion)).AppendLine(" |");
        body.Append("| KSA | ").Append(Cell(report.KsaVersion)).AppendLine(" |");
        body.Append("| Platform | ").Append(Cell(report.Platform)).AppendLine(" |");
        body.Append("| Received | ")
            .Append(DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture))
            .AppendLine(" |");
        body.AppendLine();

        if (!string.IsNullOrWhiteSpace(report.Detail))
        {
            body.AppendLine("### What happened").AppendLine();
            body.AppendLine(Fence(Guard.Clean(report.Detail, MaxDetail), MaxDetail)).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(report.Log))
        {
            body.AppendLine("### Log").AppendLine();
            body.AppendLine("<details><summary>KSArmory.log</summary>").AppendLine();
            body.AppendLine(Fence(Guard.Clean(report.Log, MaxLog), MaxLog)).AppendLine();
            body.AppendLine("</details>");
        }

        return body.ToString();
    }

    // A table cell that cannot break the table or the page around it.
    private static string Cell(string? value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : $"`{Collapse(Guard.Clean(value, MaxField), MaxField)}`";

    private static string Collapse(string value, int limit)
    {
        string flat = value.ReplaceLineEndings(" ").Replace('`', '\'').Trim();
        return flat.Length <= limit ? flat : flat[..limit];
    }

    // Backticks inside the text would close the fence early and let the rest render as markdown.
    private static string Fence(string? value, int limit)
    {
        string text = (value ?? string.Empty).ReplaceLineEndings("\n");
        if (text.Length > limit) text = text[..limit];

        return "```text\n" + text.Replace("```", "'''") + "\n```";
    }
}

/// <summary>What the mod sends. Every field is a stranger's text; none of it is trusted.</summary>
public sealed record Report(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("log")] string? Log,
    [property: JsonPropertyName("modVersion")] string? ModVersion,
    [property: JsonPropertyName("ksaVersion")] string? KsaVersion,
    [property: JsonPropertyName("platform")] string? Platform,
    [property: JsonPropertyName("secret")] string? Secret = null);
