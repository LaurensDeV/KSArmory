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
                double3 burst = cloud.Body.GetPositionEcl()
                                + cloud.BurstCcf.Transform(cloud.Body.GetCce2Ccf().Inverse());
                double3 up = cloud.Up.Transform(cloud.Body.GetCce2Ccf().Inverse());
                double3 centre = burst + (Vec.Unit(up) * MushroomCloud.At(cloud.ChargeKg, cloud.Age).CapCentre);

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
    {
        ageSeconds = 0.0;
        chargeKg = 0.0;

        if (_clouds.Count > 0)
        {
            ageSeconds = _clouds[^1].Age;
            chargeKg = _clouds[^1].ChargeKg;
            return true;
        }

        if (_burning.Count > 0)
        {
            ageSeconds = _burning[^1].Age;
            chargeKg = _burning[^1].ChargeKg;
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


    // Every burst still burning, which is NOT the same list as the clouds. A fireball does not
    // need air -- it is incandescent gas, and the vacuum one is if anything brighter for having no
    // atmosphere to attenuate it -- so an airless burst belongs here while it grows no column at
    // all. Kept separate rather than folded into _clouds, because the pass draws that list.
    private sealed class Burning
    {
        public required Celestial Body;
        public required double3 BurstCcf;
        public required double ChargeKg;
        public required bool Rises;
        public double Age;
    }

    private static readonly List<Burning> _burning = [];

    // The ground each burst burned. A third list rather than a field on either of the others,
    // because it outlives both: a cloud is gone in under five minutes and a fireball in two, and a crater is
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
                                 out double3 downwindEcl, out double overSeaMetres, out bool airless)
    {
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
    private static void Burn(Celestial body, double3 burstCcf, double chargeKg, bool airless)
    {
        for (int i = 0; i < _scorches.Count; i++)
        {
            Scorch standing = _scorches[i];
            if (!ReferenceEquals(standing.Body, body)) continue;

            if (standing.Airless != airless) continue;

            double reach = Scorch.ReachOf(standing.ChargeKg + chargeKg, airless);
            if (Vec.Len2(burstCcf - standing.BurstCcf) > reach * reach) continue;

            standing.ChargeKg += chargeKg;
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

    /// <summary>
    /// As above, with the body it burst over, which is where its daylight is read, and its charge,
    /// which is what its light is reckoned from.
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
            flash = MushroomCloud.FlashAt(one.ChargeKg, one.Age);

            return Vec.IsFinite(burstEcl);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Where one of <see cref="TryBurning"/>'s fireballs is now: at the cap's centre, where the
    /// cloud pass draws its fire, for a burst that grows a cap; at the burst for one that does not.
    /// What the glare round the ball is centred on -- centred on the burst instead, it hung on the
    /// ground under a cloud that had risen away from it.
    /// </summary>
    public static bool TryBall(int index, out double3 ballEcl)
    {
        ballEcl = default;
        if (index < 0 || index >= _burning.Count) return false;

        try
        {
            Burning one = _burning[index];
            double rise = one.Rises ? MushroomCloud.At(one.ChargeKg, one.Age).CapCentre : 0.0;
            double3 ballCcf = one.BurstCcf + (Vec.Unit(one.BurstCcf) * rise);

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

    /// <summary>
    /// One standing cloud by index, in the order they were made. <see cref="Count"/> bounds it.
    ///
    /// <para>The pass draws them one dispatch each, so it needs them all rather than the newest —
    /// a six-warhead bus makes six of these.</para>
    /// </summary>
    public static bool TryAt(int index, out double3 burstEcl, out double3 up, out double radiusMetres,
                             out double ageSeconds, out MushroomCloud.Shape shape,
                             out double3 downwind, out MushroomCloud.Flash flash, out double heat,
                             out bool water)
    {
        flash = default;
        heat = 0.0;
        water = false;

        downwind = default;

        burstEcl = default;
        up = default;
        radiusMetres = 0.0;
        ageSeconds = 0.0;
        shape = default;

        if (index < 0 || index >= _clouds.Count) return false;

        Cloud cloud = _clouds[index];

        try
        {
            burstEcl = cloud.Body.GetPositionEcl()
                       + cloud.BurstCcf.Transform(cloud.Body.GetCce2Ccf().Inverse());
            up = cloud.Up.Transform(cloud.Body.GetCce2Ccf().Inverse());
            ageSeconds = cloud.Age;
            shape = MushroomCloud.At(cloud.ChargeKg, cloud.Age);

            // The ball, so the pass can light the cloud from inside it while it burns.
            flash = MushroomCloud.FlashAt(cloud.ChargeKg, cloud.Age);
            heat = MushroomCloud.Incandescence(cloud.ChargeKg, cloud.Age);
            water = cloud.Water;

            // Chosen at the burst rather than per frame, so the column leans one way for its whole
            // life instead of wandering.
            downwind = cloud.Downwind.Transform(cloud.Body.GetCce2Ccf().Inverse());

            // The whole thing, cap and lean included, so the bounding sphere cannot clip the shape
            // it is there to reject against.
            double kt = MushroomCloud.KilotonsFor(cloud.ChargeKg);
            radiusMetres = MushroomCloud.DrawnBound(kt, cloud.Age);

            return Vec.IsFinite(burstEcl) && Vec.IsFinite(up) && Vec.IsFinite(downwind)
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
    public static void Begin(double3 burstEcl, Vehicle? near, double chargeKg, Celestial? known = null)
    {
        if (chargeKg < MushroomCloud.ThresholdKg) return;
        if (Detonation.BodyFor(near, known) is not { } body) return;
        if (!Vec.IsFinite(burstEcl)) return;

        try
        {
            double3 burstCcf = (burstEcl - body.GetPositionEcl()).Transform(body.GetCce2Ccf());
            if (!Vec.IsFinite(burstCcf) || Vec.Len2(burstCcf) < 1.0) return;

            // No air refuses a cloud twice over: a mushroom is buoyant and has nothing to rise
            // through, and KSA raymarches the trail volume only for an AtmosphericBody, so smoke
            // laid here would draw nowhere at any altitude. Before this the cloud was built anyway
            // and its dimensions logged, over a body where nobody could ever see one.
            // WHAT IT WENT OFF ON OR IN, which decides what it leaves. Only land is burned: on the
            // sea there is nothing to stain, and a mark projected off the depth buffer there lies
            // on the water's surface.
            BurstSetting setting = KsaWorld.SettingOf(
                body, burstEcl, MushroomCloud.PeakFireballRadius(MushroomCloud.KilotonsFor(chargeKg)));

            // Registered before the fork, because a fireball happens either way -- and rises with
            // the cap wherever a cap is grown, which is in air and not deep under the sea.
            _burning.Add(new Burning
            {
                Body = body,
                BurstCcf = burstCcf,
                ChargeKg = chargeKg,
                Rises = KsaWorld.HasAtmosphere(body) && setting != BurstSetting.Underwater,
            });

            if (setting == BurstSetting.Land) Burn(body, burstCcf, chargeKg, !KsaWorld.HasAtmosphere(body));

            if (!KsaWorld.HasAtmosphere(body))
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

            // A burst inside a standing cloud's own fireball is the SAME EVENT, and is added to it
            // rather than starting another. Six warheads of a bus land about 9 mm apart: drawn as
            // six clouds that is six times the smoke in one place and six full-screen dispatches
            // marching the same pixels, where the truth is one burst of the combined yield.
            for (int i = 0; i < _clouds.Count; i++)
            {
                Cloud standing = _clouds[i];
                if (!ReferenceEquals(standing.Body, body)) continue;

                double reach = MushroomCloud.PeakFireballRadius(
                    MushroomCloud.KilotonsFor(standing.ChargeKg + chargeKg));

                if (Vec.Len2(burstCcf - standing.BurstCcf) > reach * reach) continue;

                standing.ChargeKg += chargeKg;

                Log.Info($"nuclear cloud: burst {Vec.Len(burstCcf - standing.BurstCcf):F1} m from a "
                         + $"standing one and inside its {reach:F0} m fireball, so it is that one -- "
                         + $"now {MushroomCloud.KilotonsFor(standing.ChargeKg):F2} kt");
                return;
            }

            double3 up = Vec.Unit(burstCcf);

            _clouds.Add(new Cloud
            {
                Body = body,
                BurstCcf = burstCcf,
                Up = up,
                Downwind = DownwindAt(burstCcf),
                ChargeKg = chargeKg,
                Water = setting == BurstSetting.WaterSurface,
            });

            Log.Info($"nuclear burst on {body.Id}: "
                     + (setting == BurstSetting.WaterSurface
                            ? "on the sea -- a white column of spray, and nothing burned"
                            : "over land -- a column of lifted ground, and the ground burned"));

            double kt = MushroomCloud.KilotonsFor(chargeKg);

            // No particle collar round the foot. The raymarch flares the stem into the skirt itself,
            // lit like the column above it and hidden by weather in front of it -- where particles
            // are drawn after KSA's clouds and never tested against them, so a collar sat on top of
            // a deck the column had gone behind, and at high yields came out black besides.

            // And the condensation shell over the first couple of seconds, which is why a
            // photograph of a burst that early is a white dome rather than a ball of fire.
            BurstEjecta.BeginWilson(body, burstCcf, chargeKg);

            // And the bang, which is seconds behind the light.
            BurstSound.Begin(body, burstEcl, chargeKg);

            // Under the tropopause a column is about as tall as it is wide, so one number frames it
            // both ways; an anvil is wider than it stands.
            _watch = (body, burstCcf, up,
                      new AirlessBurst.Extent(Math.Max(MushroomCloud.DrawnCloudTop(kt),
                                                       MushroomCloud.DrawnCapAcross(kt) * 0.5),
                                              MushroomCloud.DrawnStandingTop(kt)));

            // At its largest, not at age zero: the ramp is at 60% there, and a diagnostic that
            // reports the smallest the thing ever is sends the next reader looking in the wrong place.
            MushroomCloud.Flash peak = MushroomCloud.FlashAt(chargeKg, MushroomCloud.GrowthSeconds(kt));

            // What is drawn, with the law beside it: they differ by MushroomCloud.DrawnScale on
            // purpose, and a diagnostic reporting only the law sends the next reader to the wrong
            // place when the thing on screen is not the size it says.
            // Where the burst was, because a cloud with no column under it is the correct drawing
            // of an airburst and the wrong drawing of a surface one. Against the mean sphere, which
            // is what the shape's own heights are measured from.
            double burstAlt = Vec.Len(burstCcf) - body.MeanRadius;

            Log.Info($"nuclear cloud: {kt:F2} kt at {burstAlt:F0} m altitude, rising to "
                     + $"{MushroomCloud.DrawnStandingTop(kt) / 1000.0:F2} km, "
                     + $"cap {MushroomCloud.DrawnCapAcross(kt) / 1000.0:F2} km across "
                     + $"(drawn at {MushroomCloud.DrawnScale:P0} of the law's "
                     + $"{MushroomCloud.CloudTop(kt) / 1000.0:F2} km)");
            Log.Info($"  fireball {peak.Radius:F0} m for {MushroomCloud.FlashSeconds(kt):F1} s, "
                     + $"glow {peak.Glow:F0}, light {(Fireball.LightAccepted ? "on" : "STOOD DOWN")}, "
                     + $"white-hot {MushroomCloud.WhiteHotSeconds(kt):F1} s, "
                     + $"grown by {MushroomCloud.GrowthSeconds(kt):F2} s");
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

        if (_clouds.Count == 0 && _burning.Count == 0)
        {
            Fireball.Clear();
            return;
        }

        double step = Math.Max(0.0, dtSim);

        for (int i = _burning.Count - 1; i >= 0; i--)
        {
            _burning[i].Age += step;
            if (MushroomCloud.FlashAt(_burning[i].ChargeKg, _burning[i].Age).Spent) _burning.RemoveAt(i);
        }

        // The light is re-submitted per frame, so a frame with no flash in it has to say so.
        bool lit = false;

        for (int i = _clouds.Count - 1; i >= 0; i--)
        {
            Cloud cloud = _clouds[i];
            cloud.Age += step;

            MushroomCloud.Shape shape = MushroomCloud.At(cloud.ChargeKg, cloud.Age);
            if (cloud.Age > 0.0 && shape.Spent) { _clouds.RemoveAt(i); continue; }

            MushroomCloud.Flash flash = MushroomCloud.FlashAt(cloud.ChargeKg, cloud.Age);
            if (flash.Spent) continue;

            lit = true;

            // THE BALL IS THE CAP. A fireball cools, goes buoyant, rises, and the toroidal
            // circulation the raymarch draws begins inside it -- there is one object, and the
            // glowing core and the opaque cloud around it are two ages of it rather than two
            // things. Drawn on a law of its own the ball stopped a hundred metres up while the cap
            // it had become climbed away without it, and the ember then sat in the stem.
            double3 riseCcf = cloud.Up * shape.CapCentre;

            Fireball.Draw(cloud.Body.GetPositionEcl()
                          + (cloud.BurstCcf + riseCcf)
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
        _watch = null;
        BurstFlash.Reset();
        BurstSound.Clear();
        Fireball.Clear();
    }
}
