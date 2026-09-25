using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// The mushroom clouds standing in the world, and what draws them.
///
/// <para>This holds where each one is and how old it is; <see cref="MushroomCloud"/> says what
/// shape that age makes, and <see cref="CloudPass"/> raymarches it. Nothing here draws the column
/// itself — what this class still draws directly is the fireball over it.</para>
///
/// <para><b>Everything is body-fixed.</b> The burst is converted to the body's rotating frame once,
/// and every position after that is an offset in it — so the cloud stands over the ground it was
/// made on, through the planet's spin and its 29.8 km/s around the star. Held in the ecliptic it
/// would be left behind within a frame, which is the same trap the bomb sight fell into.</para>
///
/// <para>Advanced on <b>simulated</b> time. A cloud is a thing in the world rather than a viewing
/// duration, so it freezes with a pause and slows with the panel's slow-motion.</para>
/// </summary>
internal static class NuclearClouds
{
    private sealed class Cloud
    {
        public required Celestial Body;
        public required double3 BurstCcf;
        public required double3 Up;
        public double ChargeKg;

        // How far over the ground it went off. The cloud stands on the ground under it, which is
        // where every height in its shape is measured from.
        public double Height;
        public double3 GroundCcf => BurstCcf - (Up * Height);

        // The air at the burst against sea level: a ball of debris rather than a mushroom under
        // MushroomCloud.ThinAirRatio, and a bigger fireball the thinner it is.
        public double AirRatio = 1.0;

        // The air at the burst and its body's speed of sound, which its blast front runs in.
        public AmbientAir BurstAir = AmbientAir.SeaLevel;
        public double SoundMetresPerSecond = BlastWave.SoundMetresPerSecond;
        public BlastFront Front => BlastFront.For(ChargeKg, BurstAir, SoundMetresPerSecond, Height);

        // How dry the air it went off in is for condensation (BurstRegime.Dryness).
        public double Dryness;
        public MushroomCloud.Shape Shape => MushroomCloud.At(ChargeKg, Age, Height, AirRatio, Front);

        // Which way this one leans. Fixed per cloud rather than per frame, or the column would
        // wander; and per cloud rather than global, so two bursts in sight of each other do not
        // lean identically.
        public required double3 Downwind;

        // Lifted off the sea rather than the ground, so the column is spray rather than dirt.
        public bool Water;

        public double Age;

        // Which cloud this is, across frames: an index moves when an older one expires.
        public int Serial = ++_serials;
    }

    private static int _serials;

    /// <summary>
    /// Whether any burst's ionised air stands between a radar and a contact. The region rides up
    /// with the fireball into the cap, so it is centred on the cap as the cloud has it now.
    /// </summary>
    public static bool BlacksOut(double3 radarEcl, double3 contactEcl)
    {
        foreach (Cloud cloud in _clouds)
        {
            double radius = FireballBlackout.Radius(cloud.ChargeKg, cloud.Age);
            if (!(radius > 0.0)) continue;

            try
            {
                double3 ground = cloud.Body.GetPositionEcl()
                                 + cloud.GroundCcf.Transform(cloud.Body.GetCce2Ccf().Inverse());
                double3 up = cloud.Up.Transform(cloud.Body.GetCce2Ccf().Inverse());
                double3 centre = ground
                                 + (Vec.Unit(up) * cloud.Shape.CapCentre);

                if (FireballBlackout.Blocks(radarEcl, contactEcl, centre, radius)) return true;
            }
            catch
            {
                // A body gone from under a cloud blinds nothing.
            }
        }

        return false;
    }

    /// <summary>The newest burst's age and charge: its cloud if it grew one, else its fireball.</summary>
    public static bool TryNewest(out double ageSeconds, out double chargeKg)
        => TryNewest(out ageSeconds, out chargeKg, out _);

    /// <summary>How dry one standing cloud's air was for condensation, in [0, 1].</summary>
    public static double DrynessAt(int index)
        => index >= 0 && index < _clouds.Count ? _clouds[index].Dryness : 0.0;

    /// <summary>The newest burst's shape as it is drawn, against its own front.</summary>
    public static bool TryNewestShape(out MushroomCloud.Shape shape)
    {
        shape = default;
        if (_clouds.Count > 0) { shape = _clouds[^1].Shape; return true; }
        if (_burning.Count > 0) { shape = _burning[^1].Shape; return true; }
        return false;
    }

    /// <summary>As above, with how far over the ground it went off.</summary>
    public static bool TryNewest(out double ageSeconds, out double chargeKg, out double heightMetres)
    {
        ageSeconds = 0.0;
        chargeKg = 0.0;
        heightMetres = 0.0;

        if (_clouds.Count > 0)
        {
            ageSeconds = _clouds[^1].Age;
            chargeKg = _clouds[^1].ChargeKg;
            heightMetres = _clouds[^1].Height;
            return true;
        }

        if (_burning.Count > 0)
        {
            ageSeconds = _burning[^1].Age;
            chargeKg = _burning[^1].ChargeKg;
            heightMetres = _burning[^1].Height;
            return true;
        }

        return false;
    }

    /// <summary>The identity of the cloud at <paramref name="index"/>, or zero when there is none.</summary>
    public static int SerialAt(int index) => index >= 0 && index < _clouds.Count ? _clouds[index].Serial : 0;

    private static readonly List<Cloud> _clouds = [];

    // Which way the wind aloft blows at a place, in the body's own frame. Off where the burst is
    // rather than off a clock, so the same crater leans the same way every time -- and two bursts
    // within sight of each other land on nearly the same bearing, which is right: they stand in
    // one wind.
    //
    // ONE RULE for the column and for the ground under it. The cloud leans downwind and sails
    // downwind, and what falls out of it lands downwind; computed twice they could disagree, and a
    // plume at right angles to the column it fell from is the plainest possible tell.
    private static double3 DownwindAt(double3 burstCcf)
    {
        // The vertical is the way out from the centre. The other two only have to be perpendicular
        // to it: the shape has an axis of symmetry, so which way "east" points is not a question
        // anybody has to answer.
        double3 up = Vec.Unit(burstCcf);
        double3 east = Vec.Unit(Vec.AnyPerpendicular(up));
        double3 north = Vec.Unit(Vec.Cross(up, east));

        double bearing = Math.Tau * ((Math.Abs(burstCcf.X) + Math.Abs(burstCcf.Z)) * 0.001 % 1.0);

        return Vec.Unit((east * Math.Cos(bearing)) + (north * Math.Sin(bearing)));
    }


    // Every burst still burning, which is NOT the same list as the clouds. A burst with no air still
    // flashes -- the device's own vapour, for half a second (MushroomCloud.VacuumFlashSeconds) -- so an
    // airless burst belongs here while it grows no column at all. Kept separate rather than folded
    // into _clouds, because the pass draws that list.
    private sealed class Burning
    {
        public required Celestial Body;
        public required double3 BurstCcf;
        public required double ChargeKg;
        public required bool Rises;
        public double Height;
        public double AirRatio = 1.0;
        public AmbientAir BurstAir = AmbientAir.SeaLevel;
        public double SoundMetresPerSecond = BlastWave.SoundMetresPerSecond;
        public double Age;
        public MushroomCloud.Shape Shape
            => MushroomCloud.At(ChargeKg, Age, Height, AirRatio,
                                BlastFront.For(ChargeKg, BurstAir, SoundMetresPerSecond, Height));
    }

    private static readonly List<Burning> _burning = [];

    // The layer a burst high over the atmosphere lit, which outlasts its fireball and its debris by
    // minutes: XRayGlow. A fourth list for the reason the others are separate -- it lives on its own
    // clock, and the pass draws it on its own dispatch.
    private sealed class Glow
    {
        public required Celestial Body;
        public required double3 BurstCcf;
        public required double ChargeKg;
        public double Age;
    }

    private static readonly List<Glow> _glows = [];

    // The aurora a burst above the atmosphere lights at each end of its field line: Sim/Aurora.cs.
    private sealed class Curtain
    {
        public required Celestial Body;
        public required double3 BurstCcf;
        public required double3 FootCcf;
        public required double3 EastCcf;
        public double SinLatitude;
        public double ChargeKg;
        public double Age;
    }

    private static readonly List<Curtain> _curtains = [];

    // How many auroral curtains are drawn at once; each is a full-screen dispatch, and a burst makes two.
    private const int MaxCurtains = 4;

    /// <summary>How many auroral curtains are glowing. Bounds <see cref="TryAurora"/>.</summary>
    public static int AuroraCount => _curtains.Count;

    /// <summary>
    /// One curtain: where its foot is on the ground, which way its arc runs, the sine of its magnetic
    /// latitude, how bright it is and how old, and the body it is over.
    /// </summary>
    public static bool TryAurora(int index, out double3 footEcl, out double3 eastEcl, out double sinLatitude,
                                 out double nits, out double age, out object? body)
    {
        footEcl = default;
        eastEcl = default;
        sinLatitude = 0.0;
        nits = 0.0;
        age = 0.0;
        body = null;
        if (index < 0 || index >= _curtains.Count) return false;

        try
        {
            Curtain c = _curtains[index];
            body = c.Body;
            age = c.Age;
            sinLatitude = c.SinLatitude;
            footEcl = c.Body.GetPositionEcl() + (c.FootCcf * c.Body.MeanRadius).Transform(c.Body.GetCce2Ccf().Inverse());
            eastEcl = Vec.Unit(c.EastCcf.Transform(c.Body.GetCce2Ccf().Inverse()));
            nits = Aurora.Strength(MushroomCloud.KilotonsFor(c.ChargeKg), c.Age);

            return Vec.IsFinite(footEcl) && Vec.IsFinite(eastEcl) && nits > 0.0;
        }
        catch
        {
            return false;
        }
    }

    // Adds a burst to the one still going off that it is part of, if any: its fireball, glow, curtains and
    // cloud, each found by the same test against where its own burst was.
    private static bool JoinTheSameBurst(Celestial body, double3 burstCcf, double chargeKg)
    {
        Burning? same = null;
        foreach (Burning one in _burning)
        {
            if (ReferenceEquals(one.Body, body)
                && MushroomCloud.IsTheSameBurst(Vec.Len(burstCcf - one.BurstCcf), one.Age, one.ChargeKg + chargeKg))
            {
                same = one;
                break;
            }
        }

        if (same is null) return false;

        bool Near(Celestial other, double3 otherCcf) => ReferenceEquals(other, body)
                                                        && Vec.Len2(otherCcf - same.BurstCcf) < 1.0;

        same.ChargeKg += chargeKg;
        foreach (Glow glow in _glows) if (Near(glow.Body, glow.BurstCcf)) glow.ChargeKg += chargeKg;
        foreach (Curtain curtain in _curtains) if (Near(curtain.Body, curtain.BurstCcf)) curtain.ChargeKg += chargeKg;
        foreach (Cloud cloud in _clouds) if (Near(cloud.Body, cloud.BurstCcf)) cloud.ChargeKg += chargeKg;

        Log.Info($"nuclear burst {Vec.Len(burstCcf - same.BurstCcf):F1} m from one {same.Age:F2} s old and inside "
                 + $"its fireball, so it is that one -- now {MushroomCloud.KilotonsFor(same.ChargeKg):F2} kt");
        return true;
    }

    // Both ends of a burst's field line, as curtains to draw.
    private static void Light(Celestial body, double3 burstCcf, double chargeKg)
    {
        double3 axis = body.GetRotationAxisCce().Transform(body.GetCce2Ccf());
        Span<double3> feet = stackalloc double3[2];
        int count = Aurora.Footpoints(burstCcf, axis, body.MeanRadius, Aurora.BottomAltitude(KsaWorld.BodyAirOf(body)), feet);

        for (int i = 0; i < count; i++)
        {
            if (_curtains.Count >= MaxCurtains) _curtains.RemoveAt(0);
            _curtains.Add(new Curtain
            {
                Body = body,
                BurstCcf = burstCcf,
                FootCcf = feet[i],
                EastCcf = Aurora.EastAt(feet[i], axis),
                SinLatitude = Vec.Dot(feet[i], Vec.Unit(axis)),
                ChargeKg = chargeKg,
            });
        }

        if (count > 0)
        {
            Log.Info($"nuclear burst over the air: its debris runs along the field and lights an aurora at "
                     + $"{count} end(s) of its field line"
                     + (count > 1
                            ? $", the far one {Vec.AngleBetween(feet[0], feet[1]) * body.MeanRadius / 1000.0:F0} km away"
                            : string.Empty));
        }
    }

    // How many glowing layers are drawn at once; each is a full-screen dispatch.
    private const int MaxGlows = 4;

    /// <summary>How many layers are glowing. Bounds <see cref="TryGlow"/>.</summary>
    public static int GlowCount => _glows.Count;

    /// <summary>
    /// One glowing layer: where the burst that lit it is, how bright the layer is under it, and how
    /// much of that is still green.
    /// </summary>
    public static bool TryGlow(int index, out double3 burstEcl, out double nits, out double green, out object? body)
    {
        body = null;
        burstEcl = default;
        nits = 0.0;
        green = 0.0;
        if (index < 0 || index >= _glows.Count) return false;

        try
        {
            Glow glow = _glows[index];
            body = glow.Body;
            burstEcl = glow.Body.GetPositionEcl() + glow.BurstCcf.Transform(glow.Body.GetCce2Ccf().Inverse());

            double over = Vec.Len(glow.BurstCcf) - glow.Body.MeanRadius - XRayGlow.LayerAltitude(KsaWorld.BodyAirOf(glow.Body));
            nits = XRayGlow.Strength(MushroomCloud.KilotonsFor(glow.ChargeKg), over, glow.Age);
            green = XRayGlow.GreenShare(glow.Age);

            return Vec.IsFinite(burstEcl) && nits > 0.0;
        }
        catch
        {
            return false;
        }
    }

    // The ground each burst burned. A third list rather than a field on either of the others,
    // because it outlives both: a cloud is gone in under ten minutes and a fireball in two, and a crater is
    // not. It is also the only one an airless burst reaches, which is the case that most wants it
    // -- there is no atmosphere out there to absorb the pulse.
    private sealed class Scorch
    {
        public required Celestial Body;
        public required double3 BurstCcf;
        public required double3 Downwind;
        public double ChargeKg;

        // Whether there was air over it, which decides what law says how far it reaches.
        public bool Airless;

        // How much of a surface burst made it: an air burst burns the ground and drops no fallout.
        public double Coupling = 1.0;

        public double Radius => ReachOf(ChargeKg, Airless);

        public static double ReachOf(double chargeKg, bool airless)
            => airless ? AirlessBurst.ScorchRadius(chargeKg) : Warhead.LethalRadius(chargeKg);

        // How far the burned patch's centre stands above the sea, so the drawing can leave the
        // water alone. Past anything reachable on a body with no sea.
        public double OverSea = NoSea;
    }

    private const double NoSea = 1.0e9;

    private static readonly List<Scorch> _scorches = [];

    // How many marks stand at once. Still bounded for a different reason from MaxClouds -- a cloud
    // expires and that list drains on its own, where this one never does -- but no longer bounded
    // LOW: a mark is dispatched over its own screen footprint rather than over the whole screen,
    // so what each one costs for the rest of the session is a few per cent of what it was. The
    // oldest is still dropped, because permanent and unbounded is a leak.
    private const int MaxScorches = 12;

    /// <summary>How many patches of burned ground stand. Bounds <see cref="TryScorch"/>.</summary>
    public static int ScorchCount => _scorches.Count;

    /// <summary>
    /// One of them: where it is now, and how wide. Under air its radius is the warhead's own lethal
    /// radius, so the stain is the reach the panel, the overlay and the blast sweep already quote;
    /// in vacuum there is no blast, and it is how far the radiation reaches instead.
    /// </summary>
    public static bool TryScorch(int index, out double3 centreEcl, out double radiusMetres,
                                 out double3 downwindEcl, out double overSeaMetres, out bool airless,
                                 out double coupling)
    {
        coupling = 1.0;
        airless = false;
        centreEcl = default;
        radiusMetres = 0.0;
        downwindEcl = default;
        overSeaMetres = NoSea;

        if (index < 0 || index >= _scorches.Count) return false;

        try
        {
            Scorch one = _scorches[index];

            centreEcl = one.Body.GetPositionEcl()
                        + one.BurstCcf.Transform(one.Body.GetCce2Ccf().Inverse());
            radiusMetres = one.Radius;

            // A DIRECTION, so only the rotation applies -- the body's position would carry the
            // ecliptic's 29.8 km/s into what is meant to be a unit vector.
            downwindEcl = Vec.Unit(one.Downwind.Transform(one.Body.GetCce2Ccf().Inverse()));
            overSeaMetres = one.OverSea;
            airless = one.Airless;
            coupling = one.Coupling;

            return Vec.IsFinite(centreEcl) && radiusMetres > 0.0 && Vec.IsFinite(downwindEcl);
        }
        catch
        {
            return false;
        }
    }

    // A burst inside a mark that is already there is the same mark, for the reason the clouds
    // merge: six warheads of a bus land 9 mm apart, and six coincident stains are six dispatches
    // over one patch of ground. Charges add, so the merged mark is the one the combined yield
    // would have made rather than the largest single.
    private static void Burn(Celestial body, double3 burstCcf, double chargeKg, bool airless,
                             double coupling = 1.0)
    {
        for (int i = 0; i < _scorches.Count; i++)
        {
            Scorch standing = _scorches[i];
            if (!ReferenceEquals(standing.Body, body)) continue;

            if (standing.Airless != airless) continue;

            double reach = Scorch.ReachOf(standing.ChargeKg + chargeKg, airless);
            if (Vec.Len2(burstCcf - standing.BurstCcf) > reach * reach) continue;

            standing.ChargeKg += chargeKg;
            standing.Coupling = Math.Max(standing.Coupling, coupling);
            return;
        }

        if (_scorches.Count >= MaxScorches) _scorches.RemoveAt(0);

        Log.Info($"ground marked to {Scorch.ReachOf(chargeKg, airless):F0} m, "
                 + (airless ? "as far as its radiation reaches in vacuum" : "its lethal radius"));

        _scorches.Add(new Scorch
        {
            Body = body,
            BurstCcf = burstCcf,
            Downwind = DownwindAt(burstCcf),
            ChargeKg = chargeKg,
            Airless = airless,
            Coupling = coupling,
            OverSea = KsaWorld.TrySeaLevel(body, out double sea)
                          ? Vec.Len(burstCcf) - body.MeanRadius - sea
                          : NoSea,
        });
    }

    /// <summary>How many bursts are still alight, over any kind of body. Bounds <see cref="TryBurning"/>.</summary>
    public static int BurningCount => _burning.Count;

    /// <summary>
    /// One of them: where it is and what its fireball is doing. What the whiteout is read off, so
    /// a burst on an airless body blinds a viewer exactly as one in air does.
    /// </summary>
    public static bool TryBurning(int index, out double3 burstEcl, out MushroomCloud.Flash flash)
        => TryBurning(index, out burstEcl, out flash, out _, out _);

    /// <summary>The air one burning ball went off in, against the reference; zero with none.</summary>
    public static double BurningAir(int index)
        => index >= 0 && index < _burning.Count ? _burning[index].AirRatio : 1.0;

    /// <summary>
    /// The burning ball as the shorter overload gives it, with the body it burst over, which is where
    /// its daylight is read, and its charge, which is what its light is reckoned from.
    /// </summary>
    public static bool TryBurning(int index, out double3 burstEcl, out MushroomCloud.Flash flash,
                                  out Celestial? body, out double chargeKg)
    {
        burstEcl = default;
        flash = default;
        body = index >= 0 && index < _burning.Count ? _burning[index].Body : null;
        chargeKg = index >= 0 && index < _burning.Count ? _burning[index].ChargeKg : 0.0;

        if (index < 0 || index >= _burning.Count) return false;

        try
        {
            Burning one = _burning[index];

            burstEcl = one.Body.GetPositionEcl()
                       + one.BurstCcf.Transform(one.Body.GetCce2Ccf().Inverse());
            flash = MushroomCloud.FlashAt(one.ChargeKg, one.Age, one.Height, one.AirRatio);

            return Vec.IsFinite(burstEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a burning ball, indexed as <see cref="TryBall"/> is, went off in air too thin for a
    /// mushroom, so it is drawn as a debris shell rather than marched as a cloud.
    /// </summary>
    public static bool BallIsThin(int index)
        => index >= 0 && index < _burning.Count && MushroomCloud.IsThin(_burning[index].AirRatio);

    /// <summary>
    /// Where one of <see cref="TryBurning"/>'s fireballs is now: at the cap's centre, where the
    /// cloud pass draws its fire, for a burst that grows a cap; at the burst for one that does not.
    /// What the glare round the ball is centred on -- centred on the burst instead, it hung on the
    /// ground under a cloud that had risen away from it. With its radius, so the glare can ask how
    /// much of the ball is hidden.
    /// </summary>
    public static bool TryBall(int index, out double3 ballEcl, out double radiusMetres)
    {
        ballEcl = default;
        radiusMetres = 0.0;
        if (index < 0 || index >= _burning.Count) return false;

        try
        {
            Burning one = _burning[index];
            radiusMetres = MushroomCloud.FlashAt(one.ChargeKg, one.Age, one.Height, one.AirRatio).Radius;

            // Measured from the ground under it, where the cap's heights are.
            double3 up = Vec.Unit(one.BurstCcf);
            double3 ballCcf = one.Rises
                                  ? one.BurstCcf + (up * (one.Shape.CapCentre
                                                          - one.Height))
                                  : one.BurstCcf;

            ballEcl = one.Body.GetPositionEcl() + ballCcf.Transform(one.Body.GetCce2Ccf().Inverse());
            return Vec.IsFinite(ballEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>How old one of <see cref="TryBurning"/>'s bursts is, or NaN.</summary>
    public static double BurningAge(int index)
        => index >= 0 && index < _burning.Count ? _burning[index].Age : double.NaN;

    // The newest burst worth pointing a camera at, which is NOT the newest cloud: an airless burst
    // grows no column and is still the thing to watch. Body-fixed like the clouds, and flat rather
    // than a list because nothing about it advances -- how big a burst draws is settled when it
    // happens.
    private static (Celestial Body, double3 BurstCcf, double3 Up, AirlessBurst.Extent Drawn)? _watch;

    /// <summary>How many clouds are standing. Diagnostic.</summary>
    public static int Count => _clouds.Count;

    /// <summary>How old one standing cloud is (s), on the simulated clock; zero past the end.</summary>
    public static double AgeOf(int index) => index >= 0 && index < _clouds.Count ? _clouds[index].Age : 0.0;

    /// <summary>
    /// How far one cloud's blast front has got, and where it started: what the pass bends the light
    /// at. The charge rides out with it, because how hard the front still is depends on it.
    /// </summary>
    public static bool TryFront(int index, out double3 burstEcl, out double3 up, out double frontMetres,
                                out double chargeKg)
        => TryFront(index, out burstEcl, out up, out frontMetres, out chargeKg, out _);

    /// <summary>As above, with how far over the ground it went off, where the front meets it.</summary>
    public static bool TryFront(int index, out double3 burstEcl, out double3 up, out double frontMetres,
                                out double chargeKg, out double heightMetres)
    {
        heightMetres = index >= 0 && index < _clouds.Count ? _clouds[index].Height : 0.0;

        // Air too thin for a column is too thin for a front worth seeing: it bends light by the density
        // it piles up, and at 100 km there is a millionth of sea level's to pile. Its reach is the sea-level
        // law's besides, crawling out at the speed of sound under a ball of debris that formed long before.
        if (index >= 0 && index < _clouds.Count && MushroomCloud.IsThin(_clouds[index].AirRatio))
        {
            burstEcl = default;
            up = default;
            frontMetres = 0.0;
            chargeKg = 0.0;
            return false;
        }
        burstEcl = default;
        up = default;
        frontMetres = 0.0;
        chargeKg = 0.0;

        if (index < 0 || index >= _clouds.Count) return false;

        Cloud cloud = _clouds[index];

        try
        {
            burstEcl = cloud.Body.GetPositionEcl()
                       + cloud.BurstCcf.Transform(cloud.Body.GetCce2Ccf().Inverse());
            up = cloud.Up.Transform(cloud.Body.GetCce2Ccf().Inverse());
            chargeKg = cloud.ChargeKg;
            frontMetres = cloud.Front.Radius(cloud.Age);

            return Vec.IsFinite(burstEcl) && Vec.IsFinite(up) && frontMetres > 0.0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// What the ground under one standing cloud's burst adds to its blast (<see cref="GroundReflection"/>),
    /// at the yield it now carries.
    /// </summary>
    public static GroundReflection ReflectionAt(int index)
    {
        if (index < 0 || index >= _clouds.Count) return GroundReflection.FreeAir;

        try
        {
            Cloud cloud = _clouds[index];
            double3 burstEcl = cloud.Body.GetPositionEcl() + cloud.BurstCcf.Transform(cloud.Body.GetCce2Ccf().Inverse());
            return KsaWorld.GroundReflectionAt(cloud.Body, burstEcl, cloud.ChargeKg);
        }
        catch
        {
            return GroundReflection.FreeAir;
        }
    }

    /// <summary>The air at a point over the body one cloud stands on, against sea level.</summary>
    public static double AirRatioAt(int index, double3 positionEcl)
    {
        if (index < 0 || index >= _clouds.Count) return 0.0;

        try
        {
            return KsaWorld.AirDensityRatioAt(_clouds[index].Body, positionEcl);
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// Whether this burst's air was too thin for a mushroom, so its debris is drawn as a glowing shell
    /// (<see cref="TryDebris"/>) rather than marched as a cloud.
    /// </summary>
    public static bool IsThin(int index)
        => index >= 0 && index < _clouds.Count && MushroomCloud.IsThin(_clouds[index].AirRatio);

    /// <summary>
    /// A thin-air burst's debris shell: where its centre is, which way the field runs through it, how
    /// big it is, how it looks, and the body it is over. False once it has faded.
    /// </summary>
    public static bool TryDebris(int index, out double3 centreEcl, out double3 fieldEcl, out double radius,
                                 out DebrisShell.Look look, out object? body)
        => TryDebris(index, out centreEcl, out fieldEcl, out radius, out look, out body, out _);

    /// <summary>
    /// As above, and where the field rather than the air holds the debris (<see cref="DebrisBubble"/>),
    /// the bubble instead of the shell: centred on the burst, and <paramref name="clipAltitude"/> the
    /// X-ray layer it is cut off at where it runs into air. Zero for the shell.
    /// </summary>
    public static bool TryDebris(int index, out double3 centreEcl, out double3 fieldEcl, out double radius,
                                 out DebrisShell.Look look, out object? body, out double clipAltitude)
    {
        centreEcl = default;
        fieldEcl = default;
        radius = 0.0;
        look = default;
        body = null;
        clipAltitude = 0.0;
        if (!IsThin(index)) return false;

        Cloud cloud = _clouds[index];
        try
        {
            body = cloud.Body;
            double3 axis = cloud.Body.GetRotationAxisCce().Transform(cloud.Body.GetCce2Ccf());
            double3 fieldCcf = DebrisShell.FieldDirection(cloud.Up, axis);
            fieldEcl = Vec.Unit(fieldCcf.Transform(cloud.Body.GetCce2Ccf().Inverse()));

            BodyAir air = KsaWorld.BodyAirOf(cloud.Body);
            double tesla = DebrisBubble.FieldTesla(air.Traits.FieldTesla, cloud.Body.MeanRadius, cloud.BurstCcf, axis);
            if (DebrisBubble.FieldShare(tesla, cloud.BurstAir.Pascals) >= 0.5)
            {
                DebrisBubble.Look bubble = DebrisBubble.At(cloud.ChargeKg, cloud.Age, tesla);
                if (bubble.Spent) return false;

                look = new DebrisShell.Look(bubble.Radiance, bubble.Colour, 0.0,
                                            bubble.Held ? DebrisBubble.Stretch : 1.0);
                radius = bubble.Across;
                clipAltitude = Math.Max(XRayGlow.LayerAltitude(air), 1.0);
                centreEcl = cloud.Body.GetPositionEcl() + cloud.BurstCcf.Transform(cloud.Body.GetCce2Ccf().Inverse());
                return Vec.IsFinite(centreEcl) && Vec.IsFinite(fieldEcl) && radius > 0.0;
            }

            look = DebrisShell.At(cloud.ChargeKg, cloud.Age, cloud.Height, cloud.AirRatio);
            if (look.Spent) return false;

            MushroomCloud.Shape shape = cloud.Shape;
            radius = shape.CapRadius;

            centreEcl = cloud.Body.GetPositionEcl()
                        + (cloud.GroundCcf + (cloud.Up * shape.CapCentre)).Transform(cloud.Body.GetCce2Ccf().Inverse());

            return Vec.IsFinite(centreEcl) && Vec.IsFinite(fieldEcl) && radius > 0.0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// One standing cloud by index, in the order they were made. <see cref="Count"/> bounds it.
    ///
    /// <para>The pass draws them one dispatch each, so it needs them all rather than the newest —
    /// a six-warhead bus makes six of these.</para>
    ///
    /// <para><paramref name="groundEcl"/> is the ground under the burst, not the burst: the shape's
    /// heights are all measured from there, which for an air burst is a long way below it.</para>
    /// </summary>
    public static bool TryAt(int index, out double3 groundEcl, out double3 up, out double radiusMetres,
                             out double ageSeconds, out MushroomCloud.Shape shape,
                             out double3 downwind, out MushroomCloud.Flash flash, out double heat,
                             out bool water)
    {
        flash = default;
        heat = 0.0;
        water = false;

        downwind = default;

        groundEcl = default;
        up = default;
        radiusMetres = 0.0;
        ageSeconds = 0.0;
        shape = default;

        if (index < 0 || index >= _clouds.Count) return false;

        Cloud cloud = _clouds[index];

        try
        {
            groundEcl = cloud.Body.GetPositionEcl()
                       + cloud.GroundCcf.Transform(cloud.Body.GetCce2Ccf().Inverse());
            up = cloud.Up.Transform(cloud.Body.GetCce2Ccf().Inverse());
            ageSeconds = cloud.Age;
            shape = cloud.Shape;

            // The ball, so the pass can light the cloud from inside it while it burns.
            flash = MushroomCloud.FlashAt(cloud.ChargeKg, cloud.Age, cloud.Height, cloud.AirRatio);
            heat = MushroomCloud.Incandescence(cloud.ChargeKg, cloud.Age);
            water = cloud.Water;

            // Chosen at the burst rather than per frame, so the column leans one way for its whole
            // life instead of wandering.
            downwind = cloud.Downwind.Transform(cloud.Body.GetCce2Ccf().Inverse());

            // The bound as the risen cloud has it, which is also what the lean is measured against;
            // the shader grows it for its march, as MushroomCloud.GrownBound does.
            double kt = MushroomCloud.KilotonsFor(cloud.ChargeKg);
            radiusMetres = MushroomCloud.RisenBound(kt, cloud.Height, cloud.AirRatio);

            return Vec.IsFinite(groundEcl) && Vec.IsFinite(up) && Vec.IsFinite(downwind)
                   && radiusMetres > 0.0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The newest burst, as a camera has to frame it: where it is, which way is up there, how far
    /// across the whole thing reaches and how high its top stands.
    ///
    /// <para>Separate from <see cref="TryNewest"/> because the two answer different questions. That
    /// one is the shader's, and there is nothing for it to march on an airless body; this one is
    /// the camera's, and a dust dome is every bit as much a thing to look at as a column. The two
    /// numbers are both needed because a dome is four times wider than it is tall, so a view framed
    /// on height alone stands far too close.</para>
    /// </summary>
    public static bool TryWatch(out double3 burstEcl, out double3 up,
                                out double radiusMetres, out double topMetres,
                                out double3 downwindEcl)
    {
        burstEcl = default;
        up = default;
        radiusMetres = 0.0;
        topMetres = 0.0;
        downwindEcl = default;

        if (_watch is not { } watch || watch.Drawn.Empty) return false;

        try
        {
            burstEcl = watch.Body.GetPositionEcl()
                       + watch.BurstCcf.Transform(watch.Body.GetCce2Ccf().Inverse());
            up = watch.Up.Transform(watch.Body.GetCce2Ccf().Inverse());
            radiusMetres = watch.Drawn.RadiusMetres;
            topMetres = watch.Drawn.TopMetres;

            // A direction, so only the rotation applies.
            downwindEcl = Vec.Unit(DownwindAt(watch.BurstCcf)
                                       .Transform(watch.Body.GetCce2Ccf().Inverse()));

            return Vec.IsFinite(burstEcl) && Vec.IsFinite(up) && radiusMetres > 0.0
                   && Vec.IsFinite(downwindEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Starts whatever a burst leaves standing, if the charge is big enough to have left anything.
    ///
    /// <para>Silent for a conventional warhead: a 500 lb bomb does not grow a mushroom, and
    /// deciding that here rather than at the call site keeps every caller from having to know.</para>
    ///
    /// <para><b>A cloud is only one of the two answers.</b> On a body with no air this hands over
    /// to <see cref="BurstEjecta"/>, which throws dust instead — so a burst still has one entry
    /// point and no caller has to know which kind of body it happened over.</para>
    /// </summary>
    /// <param name="burstHeight">
    /// How far over the ground or the sea it went off, where the caller measured it better than a
    /// position part-way through a step allows; measured here where it is not given.
    /// </param>
    public static void Begin(double3 burstEcl, Vehicle? near, double chargeKg, Celestial? known = null,
                             double? burstHeight = null)
    {
        if (chargeKg < MushroomCloud.ThresholdKg) return;
        if (Detonation.BodyFor(near, known) is not { } body) return;
        if (!Vec.IsFinite(burstEcl)) return;

        try
        {
            double3 burstCcf = (burstEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            if (!Vec.IsFinite(burstCcf) || Vec.Len2(burstCcf) < 1.0) return;

            // No air refuses a cloud: a mushroom is buoyant and has nothing to rise through.
            // WHAT IT WENT OFF ON OR IN, which decides what it leaves. Only land is burned: on the
            // sea there is nothing to stain, and a mark projected off the depth buffer there lies
            // on the water's surface.
            BurstSetting setting = KsaWorld.SettingOf(
                body, burstEcl, MushroomCloud.PeakFireballRadius(MushroomCloud.KilotonsFor(chargeKg)));

            // How far over the ground or the sea it went off. Only in air: an airless burst has its
            // own answer, and BurstEjecta asks for it below.
            bool hasAir = KsaWorld.HasAtmosphere(body);
            double kt = MushroomCloud.KilotonsFor(chargeKg);
            double height = !hasAir ? 0.0 : burstHeight is { } given && double.IsFinite(given)
                                                 ? Math.Max(given, 0.0)
                                                 : KsaWorld.BurstHeightOf(body, burstEcl);
            double airRatio = hasAir ? KsaWorld.AirDensityRatioAt(body, burstEcl) : 0.0;
            AmbientAir burstAir = hasAir ? KsaWorld.AirAt(body, burstEcl) : AmbientAir.None;
            double sound = KsaWorld.BodyAirOf(body).SoundMetresPerSecond;
            double dryness = hasAir ? BurstRegime.Dryness(KsaWorld.BodyAirOf(body), Vec.Len(burstCcf) - body.MeanRadius) : 1.0;
            bool thin = hasAir && MushroomCloud.IsThin(airRatio);
            double3 up = Vec.Unit(burstCcf);
            double3 groundCcf = burstCcf - (up * height);
            double coupling = MushroomCloud.GroundCoupling(kt, height);

            // A burst inside one still going off is the SAME EVENT (MushroomCloud.IsTheSameBurst): six
            // warheads of a bus land metres apart in one frame, and the truth is one burst of the combined
            // yield. Its yield is added to everything that burst started -- the fireball's light, the glow,
            // the aurora and the cloud -- rather than each being started again: six of each drew six glows,
            // six curtain pairs and six lights over one spot. Only a burst still going off merges; a later
            // bomb on the same spot is its own burst.
            if (JoinTheSameBurst(body, burstCcf, chargeKg))
            {
                if (setting == BurstSetting.Land && coupling > 0.01) Burn(body, groundCcf, chargeKg, !hasAir, coupling);
                if (hasAir && setting != BurstSetting.Underwater) BurstSound.Begin(body, burstEcl, chargeKg, height);
                return;
            }

            // Registered before the fork, because a fireball happens either way -- and rises with
            // the cap wherever a cap is grown, which is in air and not deep under the sea.
            // High enough that its X-rays run down to the air before they stop, it lights a layer of it --
            // unless so far out that the layer would not show, which would still cost a full screen.
            double layer = XRayGlow.LayerAltitude(KsaWorld.BodyAirOf(body));
            double overLayer = Vec.Len(burstCcf) - body.MeanRadius - layer;
            if (hasAir && Aurora.Lights(airRatio)) Light(body, burstCcf, chargeKg);

            if (hasAir && XRayGlow.Lights(airRatio)
                && XRayGlow.Strength(kt, overLayer, XRayGlow.RiseSeconds) > XRayGlow.FaintestNits)
            {
                if (_glows.Count >= MaxGlows) _glows.RemoveAt(0);
                _glows.Add(new Glow { Body = body, BurstCcf = burstCcf, ChargeKg = chargeKg });
                Log.Info($"nuclear burst over the air: its X-rays light the layer {layer / 1000.0:F0} km "
                         + $"up, {XRayGlow.Strength(kt, overLayer, 2.0):F2} "
                         + "nits under it at first, red for minutes");
            }

            _burning.Add(new Burning
            {
                Body = body,
                BurstCcf = burstCcf,
                ChargeKg = chargeKg,
                Rises = hasAir && setting != BurstSetting.Underwater,
                Height = height,
                AirRatio = hasAir ? airRatio : 0.0,
                BurstAir = hasAir ? burstAir : AmbientAir.SeaLevel,
                SoundMetresPerSecond = hasAir ? sound : BlastWave.SoundMetresPerSecond,
            });

            // As far as the fireball reached the ground: an air burst's leaves no crater or fallout.
            if (setting == BurstSetting.Land && coupling > 0.01) Burn(body, groundCcf, chargeKg, !hasAir, coupling);

            if (!hasAir)
            {
                // Above the GROUND, not above the mean sphere: what decides a surface burst is
                // whether the fireball touches the terrain that is there, and on the Moon the two
                // differ by kilometres.
                AirlessBurst.Extent thrown = BurstEjecta.Begin(
                    body, burstCcf, chargeKg, KsaWorld.HeightAboveTerrain(body, burstEcl));

                _watch = (body, burstCcf, Vec.Unit(burstCcf), thrown);
                return;
            }

            // Deep enough under the sea that the fireball never breaks the surface, there is no
            // mushroom: the column is air rising through air, and there is none down there. Said
            // once, because an empty sky over a burst is otherwise indistinguishable from a cloud
            // that failed to draw.
            if (setting == BurstSetting.Underwater)
            {
                Log.Info($"nuclear burst under the sea on {body.Id}: its fireball never reaches the "
                         + "surface, so it raises no column and burns nothing");
                return;
            }

            // Heard whether or not it joins a cloud already standing: a bomb dropped on the one before
            // still goes off. Coincident bursts are made one bang by the sound itself, and whether a
            // high one is heard at all is the pressure its front brings to the camera.
            BurstSound.Begin(body, burstEcl, chargeKg, height);

            _clouds.Add(new Cloud
            {
                Body = body,
                BurstCcf = burstCcf,
                Up = up,
                Height = height,
                AirRatio = airRatio,
                BurstAir = burstAir,
                SoundMetresPerSecond = sound,
                Dryness = dryness,
                Downwind = DownwindAt(groundCcf),
                ChargeKg = chargeKg,
                Water = setting == BurstSetting.WaterSurface && !thin,
            });

            Log.Info($"nuclear burst on {body.Id}: "
                     + (thin
                            ? $"in air {airRatio:G2} of sea level's -- too thin for a column: a ball of debris, "
                              + $"the fireball {MushroomCloud.ThinAirGrowth(airRatio):F1}x its size in dense air"
                            : setting == BurstSetting.WaterSurface
                            ? (coupling < 0.5
                                   ? "in the air over the sea -- spray, not dirt, under it, and nothing burned"
                                   : "on the sea -- a white column of spray, and nothing burned")
                            : "over land -- a column of lifted ground, and the ground burned"));
            Log.Info($"  {height:F0} m over the surface: "
                     + $"{coupling:P0} a surface burst, dust column {MushroomCloud.StemShare(kt, height):P0} "
                     + $"of a surface burst's stem (fallout-safe from {MushroomCloud.FalloutSafeHeight(kt):F0} m)");

            // No particle collar round the foot. The raymarch flares the stem into the skirt itself,
            // lit like the column above it and hidden by weather in front of it -- where particles
            // are drawn after KSA's clouds and never tested against them, so a collar sat on top of
            // a deck the column had gone behind, and at high yields came out black besides.

            // And the condensation shell over the first couple of seconds, which is why a
            // photograph of a burst that early is a white dome rather than a ball of fire.
            if (!thin && dryness < 0.5) BurstEjecta.BeginWilson(body, burstCcf, chargeKg);

            // Under the tropopause a column is about as tall as it is wide, so one number frames it
            // both ways; an anvil is wider than it stands.
            _watch = (body, groundCcf, up,
                      new AirlessBurst.Extent(Math.Max(MushroomCloud.DrawnCloudTop(kt),
                                                       MushroomCloud.DrawnCapAcross(kt) * 0.5),
                                              MushroomCloud.TallestDrawn(kt, height, airRatio)));

            // At its largest, not at age zero: the ramp is at 60% there, and a diagnostic that
            // reports the smallest the thing ever is sends the next reader looking in the wrong place.
            MushroomCloud.Flash peak = MushroomCloud.FlashAt(chargeKg, MushroomCloud.GrowthSeconds(kt, airRatio), height, airRatio);

            // What is drawn, with the law beside it: they differ by MushroomCloud.DrawnScale, and a
            // diagnostic reporting only the law sends the next reader to the wrong place when the
            // thing on screen is not the size it says.
            double burstAlt = Vec.Len(burstCcf) - body.MeanRadius;

            Log.Info($"nuclear cloud: {kt:F2} kt at {burstAlt:F0} m altitude, rising to "
                     + $"{MushroomCloud.TallestDrawn(kt, height, airRatio) / 1000.0:F2} km, "
                     + (thin
                            ? $"its debris {2.0 * MushroomCloud.At(chargeKg, MushroomCloud.LifeFor(kt) * 0.9, height, airRatio).CapTube / 1000.0:F2} km across "
                            : $"cap {MushroomCloud.DrawnCapAcross(kt) / 1000.0:F2} km across ")
                     + $"(drawn at {MushroomCloud.DrawnScale:P0} of the law's "
                     + $"{MushroomCloud.CloudTop(kt) / 1000.0:F2} km)");
            Log.Info($"  fireball {peak.Radius:F0} m for {MushroomCloud.FlashSeconds(kt, hasAir ? airRatio : 0.0):F1} s, "
                     + $"glow {peak.Glow:F0}, light {(Fireball.LightAccepted ? "on" : "STOOD DOWN")}, "
                     + $"white-hot {MushroomCloud.WhiteHotSeconds(kt, airRatio):F1} s, "
                     + $"grown by {MushroomCloud.GrowthSeconds(kt, airRatio):F2} s");
        }
        catch (Exception e)
        {
            Log.Warn($"nuclear cloud: could not start one ({e.Message})");
        }
    }

    /// <summary>Advances every cloud and lays this frame's smoke.</summary>
    public static void Update(double dtSim)
    {
        if (!double.IsFinite(dtSim)) return;

        for (int i = _curtains.Count - 1; i >= 0; i--)
        {
            _curtains[i].Age += Math.Max(dtSim, 0.0);
            if (_curtains[i].Age >= Aurora.LifeSeconds) _curtains.RemoveAt(i);
        }

        for (int i = _glows.Count - 1; i >= 0; i--)
        {
            _glows[i].Age += Math.Max(dtSim, 0.0);
            if (_glows[i].Age >= XRayGlow.LifeSeconds) _glows.RemoveAt(i);
        }

        if (_clouds.Count == 0 && _burning.Count == 0)
        {
            Fireball.Clear();
            return;
        }

        double step = Math.Max(0.0, dtSim);

        for (int i = _burning.Count - 1; i >= 0; i--)
        {
            _burning[i].Age += step;
            if (MushroomCloud.FlashAt(_burning[i].ChargeKg, _burning[i].Age, _burning[i].Height,
                                      _burning[i].AirRatio).Spent)
            {
                _burning.RemoveAt(i);
            }
        }

        // The light is re-submitted per frame, so a frame with no flash in it has to say so.
        bool lit = false;

        for (int i = _clouds.Count - 1; i >= 0; i--)
        {
            Cloud cloud = _clouds[i];
            cloud.Age += step;

            MushroomCloud.Shape shape = cloud.Shape;
            if (cloud.Age > 0.0 && shape.Spent) { _clouds.RemoveAt(i); continue; }

            // A thin-air burst is drawn as its debris shell alone, so it ends when the shell does.
            if (MushroomCloud.IsThin(cloud.AirRatio) && cloud.Age >= DebrisShell.LifeSeconds) { _clouds.RemoveAt(i); continue; }

            MushroomCloud.Flash flash = MushroomCloud.FlashAt(cloud.ChargeKg, cloud.Age, cloud.Height, cloud.AirRatio);
            if (flash.Spent) continue;

            lit = true;

            // THE BALL IS THE CAP. A fireball cools, goes buoyant, rises, and the toroidal
            // circulation the raymarch draws begins inside it -- there is one object, and the
            // glowing core and the opaque cloud around it are two ages of it rather than two
            // things. Drawn on a law of its own the ball stopped a hundred metres up while the cap
            // it had become climbed away without it, and the ember then sat in the stem.
            double3 riseCcf = cloud.Up * shape.CapCentre;

            Fireball.Draw(cloud.Body.GetPositionEcl()
                          + (cloud.GroundCcf + riseCcf)
                                .Transform(cloud.Body.GetCce2Ccf().Inverse()),
                          flash.Radius,
                          new float3((float)flash.Colour.X, (float)flash.Colour.Y,
                                     (float)flash.Colour.Z),
                          (float)flash.Glow);
        }

        if (!lit) Fireball.Clear();
    }

    /// <summary>Forgets every cloud, for a scene that no longer contains them.</summary>
    public static void Clear()
    {
        _clouds.Clear();
        _burning.Clear();
        _scorches.Clear();
        _glows.Clear();
        _curtains.Clear();
        _watch = null;
        BurstFlash.Reset();
        BurstSound.Clear();
        BlastArrivals.Clear();
        BlastShake.Clear();
        Fireball.Clear();
    }
}
