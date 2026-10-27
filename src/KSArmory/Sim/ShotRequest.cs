using System.Globalization;

namespace KSArmory;

/// <summary>
/// What a scripted ballistic shot was asked for: where the warheads go, and the bar the group that
/// arrives is judged against.
///
/// <para>Text in, no file access, so every refusal is testable without the game.</para>
/// </summary>
/// <param name="BarMetres">
/// How far the worst warhead of the group may land from the aim point and still be a pass.
/// </param>
/// <param name="AimWasGiven">
/// Whether the aim point came from the request rather than the default; only a default may be
/// replaced by a defended site the scenario finds in the scene.
/// </param>
/// <param name="Extra">
/// The places after the first; null and empty both mean one target. Callers read <see cref="Targets"/>.
/// </param>
/// <param name="TrackTargets">
/// How many more targets the scenario lays out from the first, <see cref="TrackSpacingMetres"/> apart: uprange along
/// the great circle to the launch site, or square to it under <see cref="TrackAcross"/>. Placed in the game, where the
/// pad is known; the Earth turning under the flight bends the real ground track off that circle.
/// </param>
internal readonly record struct ShotRequest(double LatitudeDeg, double LongitudeDeg, double BarMetres,
                                            bool AimWasGiven = false,
                                            IReadOnlyList<ShotRequest.Aim>? Extra = null,
                                            int TrackTargets = 0, double TrackSpacingMetres = 0.0,
                                            bool TrackAcross = false)
{
    public int TargetCount => Targets.Count + (Targets.Count > 0 ? Math.Max(0, TrackTargets) : 0);

    internal readonly record struct Aim(double LatitudeDeg, double LongitudeDeg);

    /// <summary>
    /// Every place this shot names, the first included; none where no aim was named, so the scenario
    /// shoots at whatever the scene defends. Not the ones a track lays out in game.
    /// </summary>
    public IReadOnlyList<Aim> Targets
    {
        get
        {
            if (!AimWasGiven) return [];
            if (Extra is not { Count: > 0 }) return [new Aim(LatitudeDeg, LongitudeDeg)];

            List<Aim> all = [new Aim(LatitudeDeg, LongitudeDeg)];
            all.AddRange(Extra);
            return all;
        }
    }

    /// <summary>
    /// Where a shot goes when nobody says. It is the aim point the deorbit numbers in <c>docs/ACCURACY-PLAN.md</c>,
    /// "Moved from ICBM-GUIDANCE", were taken against, 2,300–2,700 km downrange of the pad they were flown from, which
    /// is what makes a run comparable with what is written down.
    /// </summary>
    public const double DefaultLatitudeDeg = -26.485;

    public const double DefaultLongitudeDeg = -68.148;

    /// <summary>
    /// Loose on purpose: it separates a shot that worked from the failures this harness catches, each
    /// kilometres wide — an untrimmed separation at 3.5 km, a tumbling bus at 7.4, a drag-free prediction
    /// at 59. A request can name a tighter one.
    /// </summary>
    public const double DefaultBarMetres = 5_000.0;

    public static ShotRequest Default => new(DefaultLatitudeDeg, DefaultLongitudeDeg, DefaultBarMetres);

    public string Describe()
    {
        char ns = LatitudeDeg >= 0.0 ? 'N' : 'S';
        char ew = LongitudeDeg >= 0.0 ? 'E' : 'W';

        return $"{Math.Abs(LatitudeDeg):F3}{ns} {Math.Abs(LongitudeDeg):F3}{ew}, "
               + $"pass under {BarMetres / 1000.0:F1} km";
    }

    // track:6x100 is six more targets 100 km apart uprange of the first; across:6x100 lays them square to it.
    // False with empty trouble for a segment that is not a track.
    private static bool TryTrack(string segment, out int count, out double spacingMetres, out bool across,
                                 out string trouble)
    {
        count = 0;
        spacingMetres = 0.0;
        trouble = "";
        string s = segment.Trim();
        across = s.StartsWith("across:", StringComparison.OrdinalIgnoreCase);
        if (!across && !s.StartsWith("track:", StringComparison.OrdinalIgnoreCase)) return false;

        string[] parts = s[(s.IndexOf(':') + 1)..].Split('x', 'X');
        if (parts.Length != 2
            || !int.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out count)
            || !double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double km)
            || count < 1 || !(km > 0.0) || !double.IsFinite(km))
        {
            trouble = $"expected {(across ? "across" : "track")}:<count>x<km> with a count of one or more and a "
                      + $"positive spacing, got '{s}'";
            count = 0;
            return false;
        }

        spacingMetres = km * 1000.0;
        return true;
    }

    /// <summary>
    /// Reads the arguments a scenario name carries — everything after the colon in
    /// <c>mirv:26.485S,68.148W</c>, and the empty string for a bare <c>mirv</c>.
    ///
    /// <para>One field is the bar in kilometres, for a shot at whatever the scene defends; two are an
    /// aim point, a hemisphere letter allowed for the sign; three add the bar. Several places are joined
    /// by ';', and <c>track:</c>/<c>across:</c> as the second segment lays out the rest.</para>
    /// </summary>
    /// <param name="trouble">What an operator has to change, naming the field, or empty.</param>
    public static bool TryParse(string? arguments, out ShotRequest shot, out string trouble)
    {
        shot = Default;
        trouble = "";

        if (string.IsNullOrWhiteSpace(arguments)) return true;

        // Each segment parses down the single-target path, so it cannot behave differently.
        string[] segments = arguments.Split(';', StringSplitOptions.RemoveEmptyEntries);

        string trackTrouble = "";
        if (segments.Length >= 2 && TryTrack(segments[1], out int count, out double spacing, out bool across,
                                              out trackTrouble))
        {
            if (segments.Length > 2)
            {
                trouble = "a track is the first target and one track:<count>x<km>, nothing after it";
                return false;
            }

            if (!TryParse(segments[0], out ShotRequest lead, out trouble)) return false;
            if (!lead.AimWasGiven)
            {
                trouble = "a track needs the first target as <lat>,<lon>";
                return false;
            }

            if (1 + count > TargetSet.MaxTargets)
            {
                trouble = $"no bus carries more than {TargetSet.MaxTargets} warheads; a track of {count} after the "
                          + $"first is {1 + count} places";
                return false;
            }

            shot = lead with { TrackTargets = count, TrackSpacingMetres = spacing, TrackAcross = across };
            return true;
        }
        else if (trackTrouble.Length > 0)
        {
            trouble = trackTrouble;
            return false;
        }

        if (segments.Length > 1)
        {
            if (segments.Length > TargetSet.MaxTargets)
            {
                trouble = $"no bus carries more than {TargetSet.MaxTargets} warheads, so it can be sent to at most "
                          + $"{TargetSet.MaxTargets} places; got {segments.Length}";
                return false;
            }

            List<Aim> extra = [];
            ShotRequest first = Default;

            for (int i = 0; i < segments.Length; i++)
            {
                if (!TryParse(segments[i], out ShotRequest one, out trouble))
                {
                    trouble = $"target {i + 1}: {trouble}";
                    return false;
                }

                if (!one.AimWasGiven)
                {
                    trouble = $"target {i + 1}: expected <lat>,<lon>";
                    return false;
                }

                if (i == 0) first = one;
                else extra.Add(new Aim(one.LatitudeDeg, one.LongitudeDeg));

                // The bar belongs to the shot, so the last segment to name one wins.
                if (one.BarMetres != DefaultBarMetres) first = first with { BarMetres = one.BarMetres };
            }

            shot = first with { Extra = extra };
            return true;
        }

        string[] fields = segments[0].Split(',');

        if (fields.Length == 1)
        {
            if (!TryBar(fields[0], out double barOnly, out trouble))
            {
                trouble = $"expected <km>, <lat>,<lon> or <lat>,<lon>,<km> -- {trouble}";
                return false;
            }

            shot = Default with { BarMetres = barOnly };
            return true;
        }

        if (fields.Length is not (2 or 3))
        {
            trouble = $"expected <km>, <lat>,<lon> or <lat>,<lon>,<km>, got {fields.Length} field(s)";
            return false;
        }

        if (!TryAngle(fields[0], 'N', 'S', 90.0, out double lat, out trouble))
        {
            trouble = "latitude: " + trouble;
            return false;
        }

        if (!TryAngle(fields[1], 'E', 'W', 180.0, out double lon, out trouble))
        {
            trouble = "longitude: " + trouble;
            return false;
        }

        double bar = DefaultBarMetres;

        if (fields.Length == 3 && !TryBar(fields[2], out bar, out trouble)) return false;

        shot = new ShotRequest(lat, lon, bar, AimWasGiven: true);
        return true;
    }

    private static bool TryBar(string field, out double metres, out string trouble)
    {
        metres = DefaultBarMetres;
        trouble = "";

        if (!double.TryParse(field.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double km)
            || !double.IsFinite(km) || km <= 0.0)
        {
            trouble = $"the bar has to be a positive number of kilometres, not '{field.Trim()}'";
            return false;
        }

        metres = km * 1000.0;
        return true;
    }

    // The wrong hemisphere letter is refused rather than ignored: a longitude written 68.148S is a mistake.
    private static bool TryAngle(string field, char positive, char negative, double limit,
                                 out double degrees, out string trouble)
    {
        degrees = 0.0;
        trouble = "";

        string text = field.Trim();

        if (text.Length == 0)
        {
            trouble = "is empty";
            return false;
        }

        double sign = 1.0;
        char last = char.ToUpperInvariant(text[^1]);

        if (char.IsLetter(last))
        {
            if (last == char.ToUpperInvariant(negative)) sign = -1.0;
            else if (last != char.ToUpperInvariant(positive))
            {
                trouble = $"'{text}' ends in {last}, which is not {positive} or {negative}";
                return false;
            }

            text = text[..^1].Trim();
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value))
        {
            trouble = $"'{field.Trim()}' is not a number of degrees";
            return false;
        }

        degrees = sign * value;

        if (Math.Abs(degrees) > limit)
        {
            trouble = $"{degrees:F3} is outside +/-{limit:F0}";
            return false;
        }

        return true;
    }
}
