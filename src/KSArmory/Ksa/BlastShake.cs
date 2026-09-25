using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The view shaken as a blast front passes the camera: <see cref="ViewShake"/>'s jolt, struck the
/// step a cloud's front grows past the camera's distance from its burst, as hard as the real
/// overpressure there. <b>The model, not the drawing</b>: <see cref="CloudPass"/> moves the picture
/// by <see cref="Offset"/>.
///
/// <para>On the simulated step, like the bang and the front itself: it waits through a pause and
/// slows with the world. Read against the live front rather than timed from the flash, so a camera
/// that moves while the front is on its way is struck where it has got to. Every front that passes
/// is felt, and fronts passing close together add, held to the most one front can throw.</para>
/// </summary>
internal static class BlastShake
{
    // How far each cloud's front had got at the last step, by the cloud's serial.
    private static readonly Dictionary<int, double> _fronts = [];
    private static readonly HashSet<int> _seen = [];

    // The rattles still running: how hard, how long since the front passed, how long each lasts.
    private static readonly List<(double Strength, double Since, double Seconds, int Seed)> _shakes = [];

    /// <summary>
    /// The picture's offset now: across and up in shares of the screen's height, and a roll in
    /// radians. Zero when nothing is shaking.
    /// </summary>
    public static (double X, double Y, double Roll) Offset { get; private set; }

    /// <summary>Whether the picture is being moved at all this frame.</summary>
    public static bool Shaking => Offset != (0.0, 0.0, 0.0);

    public static void Clear()
    {
        _fronts.Clear();
        _shakes.Clear();
        Offset = (0.0, 0.0, 0.0);
    }

    public static void Update(double dt)
    {
        if (!double.IsFinite(dt) || dt < 0.0) return;

        try
        {
            Watch();
        }
        catch
        {
            // A front that cannot be read does not shake anything.
        }

        double x = 0.0, y = 0.0, roll = 0.0;
        for (int i = _shakes.Count - 1; i >= 0; i--)
        {
            (double strength, double since, double seconds, int seed) = _shakes[i];
            since += dt;
            if (since > seconds)
            {
                _shakes.RemoveAt(i);
                continue;
            }

            _shakes[i] = (strength, since, seconds, seed);
            (double sx, double sy, double sr) = ViewShake.At(strength, since, seconds, seed);
            x += sx;
            y += sy;
            roll += sr;
        }

        double most = ViewShake.MostShare;
        Offset = (Math.Clamp(x, -most, most), Math.Clamp(y, -most, most), Math.Clamp(roll, -most, most));
    }

    private static void Watch()
    {
        _seen.Clear();
        if (NuclearClouds.Count == 0)
        {
            _fronts.Clear();
            return;
        }

        bool eye = KsaWorld.TryMainCameraPose(out double3 eyeEcl, out _);

        for (int i = 0; i < NuclearClouds.Count; i++)
        {
            int serial = NuclearClouds.SerialAt(i);
            if (!NuclearClouds.TryFront(i, out double3 burstEcl, out _, out double front, out double chargeKg)) continue;

            _seen.Add(serial);
            double was = _fronts.GetValueOrDefault(serial, 0.0);
            _fronts[serial] = front;

            if (!eye) continue;

            double range = Vec.Len(eyeEcl - burstEcl);
            if (!(was < range && front >= range)) continue;

            double air = NuclearClouds.AirRatioAt(i, eyeEcl);
            if (!(air > Medium.NoticeableDensity)) continue;

            // The ground's own gain where the eye is, as the parts' damage takes it: a surface burst's
            // hemisphere doubles it everywhere, an air burst's stem only near the ground.
            double gain = NuclearClouds.ReflectionAt(i).GainAt(eyeEcl, chargeKg);
            double ambient = BlastWave.SeaLevelPascals * air;
            double pascals = BlastWave.PeakOverpressurePascals(chargeKg, range, ambient, gain);
            double strength = ViewShake.Strength(pascals);
            if (!(strength > 0.0)) continue;

            double seconds = ViewShake.Seconds(BlastWave.PositivePhaseSeconds(chargeKg, range, gain));
            _shakes.Add((strength, 0.0, seconds, serial));

            Log.Info($"blast front passed the camera {Distance.Say(range)} from the burst at "
                     + $"{pascals / 1000.0:F1} kPa (ground gain {gain:F2}); "
                     + $"shaking at {strength:F2} for {seconds:F1} s");
        }

        if (_fronts.Count > _seen.Count)
        {
            foreach (int gone in _fronts.Keys.Where(k => !_seen.Contains(k)).ToList()) _fronts.Remove(gone);
        }
    }
}
