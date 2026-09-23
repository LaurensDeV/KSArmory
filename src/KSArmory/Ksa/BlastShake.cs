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
/// that moves while the front is on its way is struck where it has got to.</para>
/// </summary>
internal static class BlastShake
{
    // How far each cloud's front had got at the last step, by the cloud's serial.
    private static readonly Dictionary<int, double> _fronts = [];
    private static readonly HashSet<int> _seen = [];

    private static double _strength;
    private static double _since;
    private static double _seconds;
    private static int _seed;

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
        _strength = 0.0;
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

        _since += dt;
        Offset = _strength > 0.0 ? ViewShake.At(_strength, _since, _seconds, _seed) : (0.0, 0.0, 0.0);
        if (_since > _seconds) _strength = 0.0;
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

            double ambient = BlastWave.SeaLevelPascals * air;
            double strength = ViewShake.Strength(BlastWave.PeakOverpressurePascals(chargeKg, range, ambient));
            if (strength <= _strength && _since <= _seconds) continue;

            _strength = strength;
            _since = 0.0;
            _seconds = ViewShake.Seconds(BlastWave.PositivePhaseSeconds(chargeKg, range));
            _seed = serial;

            Log.Info($"blast front passed the camera {Distance.Say(range)} from the burst at "
                     + $"{BlastWave.PeakOverpressurePascals(chargeKg, range, ambient) / 1000.0:F1} kPa; "
                     + $"shaking at {strength:F2} for {_seconds:F1} s");
        }

        if (_fronts.Count > _seen.Count)
        {
            foreach (int gone in _fronts.Keys.Where(k => !_seen.Contains(k)).ToList()) _fronts.Remove(gone);
        }
    }
}
