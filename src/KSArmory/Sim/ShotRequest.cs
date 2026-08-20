using System.Globalization;

namespace KSArmory;

/// <summary>
/// What a scripted ballistic shot was asked for: where the warheads go, and the bar the group that
/// arrives is judged against.
///
/// <para><b>Text in, no file access</b>, the same rule <see cref="PackReader"/> follows and for the
/// same reason: the harness's whole request is one line beside the log, so every refusal is
/// testable without the game. A scenario that silently aims somewhere else because a coordinate
/// would not parse is a seven-minute flight spent proving nothing.</para>
/// </summary>
/// <param name="BarMetres">
/// How far the worst warhead of the group may land from the aim point and still be a pass. A bar
/// rather than a mean, because a salvo is only as good as the round that went furthest astray.
/// </param>
/// <param name="AimWasGiven">
/// Whether the aim point came from the request rather than from the default. A scenario that finds
/// a defended site in the scene should shoot at <em>it</em> — that is the engagement worth flying —
/// but not over the top of somewhere the operator named explicitly.
/// </param>
internal readonly record struct ShotRequest(double LatitudeDeg, double LongitudeDeg, double BarMetres,
                                            bool AimWasGiven = false)
{
    /// <summary>
    /// Where a shot goes when nobody says. It is the aim point <c>docs/ICBM-GUIDANCE.md</c>'s flown
    /// numbers were taken against, 2,300–2,700 km downrange of the pad they were flown from, which
    /// is what makes a run comparable with what is written down.
    /// </summary>
    public const double DefaultLatitudeDeg = -26.485;

    /// <summary>The other half of that aim point.</summary>
    public const double DefaultLongitudeDeg = -68.148;

    /// <summary>
    /// The default bar, in metres.
    ///
    /// <para>Deliberately loose. The flown group is best 371 m at a CEP of about 710 m, so five
    /// kilometres is not a standard to aspire to — it is the line between a shot that worked and
    /// one of the failures this harness exists to catch, every one of which was kilometres wide:
    /// an untrimmed separation at 3.5 km, a tumbling bus at 7.4, a late release at 8.2–9.0, a
    /// drag-free prediction at 59. It is stated in the verdict so it can be argued with, and the
    /// request can name a tighter one.</para>
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

    /// <summary>
    /// Reads the arguments a scenario name carries — everything after the colon in
    /// <c>mirv:26.485S,68.148W</c>, and the empty string for a bare <c>mirv</c>.
    ///
    /// <para>Two fields are an aim point and three add the bar in kilometres. A hemisphere letter
    /// may stand in for the sign, because that is how coordinates are written down everywhere else
    /// and a request nobody can type from a map is a request nobody uses.</para>
    /// </summary>
    /// <param name="trouble">
    /// What an operator has to change, or empty. Naming the field is the whole point: a scenario
    /// that refuses a request has to say which half of it was wrong, or the next attempt is a
    /// guess.
    /// </param>
    public static bool TryParse(string? arguments, out ShotRequest shot, out string trouble)
    {
        shot = Default;
        trouble = "";

        if (string.IsNullOrWhiteSpace(arguments)) return true;

        string[] fields = arguments.Split(',');

        if (fields.Length is not (2 or 3))
        {
            trouble = $"expected <lat>,<lon> or <lat>,<lon>,<km>, got {fields.Length} field(s)";
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

        if (fields.Length == 3)
        {
            if (!double.TryParse(fields[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                 out double km)
                || !double.IsFinite(km) || km <= 0.0)
            {
                trouble = $"the bar has to be a positive number of kilometres, not '{fields[2].Trim()}'";
                return false;
            }

            bar = km * 1000.0;
        }

        shot = new ShotRequest(lat, lon, bar, AimWasGiven: true);
        return true;
    }

    // A signed decimal, or the same number with the hemisphere spelled out. The wrong hemisphere
    // letter is refused rather than ignored: a longitude written 68.148S is somebody's mistake, and
    // silently dropping the letter aims a quarter of the way round the planet from where they meant.
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
