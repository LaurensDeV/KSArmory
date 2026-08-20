using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What a guided tungsten rod could and could not do, measured rather than argued.
///
/// <para>Measurement only, in the discipline of <c>ErrorBudgetTests</c> and <c>MirvBudgetTests</c>:
/// every test either reports a number or pins one that was measured, and nothing here asserts an
/// improvement or changes how anything flies.</para>
///
/// <para><b>The question this exists to answer.</b> A rod has no lethal radius, so the delivery has
/// to be exact rather than close. It does not follow that the ballistic solution has to be exact —
/// only that it has to land inside whatever a tail kit can still remove. These measure that
/// envelope.</para>
///
/// <para><b>What the rig cannot see.</b> The planet is at the origin and carries no orbital
/// velocity, so a term differenced against a body sample cannot show an epoch fault here. Its spin
/// is real. See <see cref="KineticRod"/> and <c>docs/FRAMES-AND-EPOCHS.md</c>.</para>
/// </summary>
public class KineticRodTests(ITestOutputHelper Out)
{
    /// <summary>Where the air starts mattering to a body of this ballistic coefficient.</summary>
    private const double EntryAltitude = 100_000.0;

    // ------------------------------------------------------------------ the rod itself

    /// <summary>
    /// Every number about the rod follows from its dimensions and what it is made of, so this
    /// reports them rather than a profile block somebody chose.
    /// </summary>
    [Fact]
    public void WhatARodIs()
    {
        Out.WriteLine($"{KineticRod.LengthMetres:F1} m x {KineticRod.DiameterMetres:F2} m tungsten");
        Out.WriteLine($"  mass                 {KineticRod.MassKg:F0} kg");
        Out.WriteLine($"  frontal area         {KineticRod.FrontalAreaM2:F4} m^2");
        Out.WriteLine($"  planform area        {KineticRod.PlanformAreaM2:F3} m^2");
        Out.WriteLine($"  DragK                {KineticRod.DragK:E3}  (sea level, a = -k|v|v)");
        Out.WriteLine($"  ballistic coefficient {KineticRod.BallisticCoefficient:F0} kg/m^2");

        MunitionProfile mk21 = Arsenal.ReentryVehicleMk21;
        Out.WriteLine($"  Mk 21 for comparison  DragK {mk21.DragK:E3}"
                      + $" — the rod is {mk21.DragK / KineticRod.DragK:F0}x cleaner");

        double kinetic = 0.5 * KineticRod.MassKg * 3000.0 * 3000.0;
        Out.WriteLine($"  kinetic energy at 3 km/s  {kinetic / 1e9:F1} GJ"
                      + $" = {kinetic / 4.184e9:F1} t of TNT");
    }

    // ------------------------------------------------------------------ what the air will give it

    /// <summary>
    /// Fin authority is bought with dynamic pressure, and a rod is above the air until seconds
    /// before it arrives. This is the ceiling: what one degree of angle of attack is worth, and
    /// what pressure a whole g costs.
    /// </summary>
    [Fact]
    public void WhatTheAirWouldHaveToGiveItToPullOneG()
    {
        Out.WriteLine("dynamic pressure needed for 1 g of body lift, by trim angle:");

        foreach (double alpha in new[] { 2.0, 5.0, 10.0, 15.0, 20.0 })
        {
            double perPascal = KineticRod.AvailableG(1.0, alpha);
            Out.WriteLine($"  alpha {alpha,4:F0} deg -> {1.0 / perPascal / 1000.0,9:F0} kPa for 1 g");
        }

        Out.WriteLine("");
        Out.WriteLine("and what a rod actually flies through, at 10 deg of trim:");
        Out.WriteLine("  alt km   speed m/s        q kPa    available g");

        foreach ((double altitude, double speed) in new[]
                 {
                     (80_000.0, 7_400.0), (60_000.0, 7_300.0), (40_000.0, 6_900.0),
                     (30_000.0, 6_200.0), (20_000.0, 4_600.0), (10_000.0, 2_600.0),
                     (5_000.0, 1_800.0), (0.0, 1_400.0),
                 })
        {
            double ratio = Math.Exp(-altitude / DeorbitShot.ScaleHeight);
            double q = KineticRod.DynamicPressure(ratio, speed);

            Out.WriteLine($"  {altitude / 1000.0,6:F0} {speed,10:F0} {q / 1000.0,12:F0} "
                          + $"{KineticRod.AvailableG(q, 10.0),14:F2}");
        }
    }

    /// <summary>
    /// The same question asked of the trajectory rather than of a table: how long a rod spends
    /// with usable authority, and how much lateral velocity it could buy in that time.
    /// </summary>
    [Theory]
    [InlineData(7_400.0, 3.0)]
    [InlineData(7_400.0, 10.0)]
    [InlineData(5_000.0, 20.0)]
    [InlineData(3_000.0, 45.0)]
    public void HowLongARodHasAnyAuthorityAtAll(double speed, double gammaDeg)
    {
        KineticRod.Entry entry = new(EntryAltitude, speed, gammaDeg);
        KineticRod.Authority aero = KineticRod.Authority.FromDynamicPressure(10.0, 20.0);

        (double seconds, double impulse, double displacement, double peakQ, double peakG) =
            Profile(entry, aero);

        Out.WriteLine($"entry {speed / 1000.0:F1} km/s at {gammaDeg:F0} deg:");
        Out.WriteLine($"  peak q {peakQ / 1000.0:F0} kPa, peak available {peakG:F1} g");
        Out.WriteLine($"  {seconds:F1} s above {KineticRod.UsableG:F2} g");
        Out.WriteLine($"  lateral velocity purchasable {impulse:F0} m/s");
        Out.WriteLine($"  displacement that buys, integrated properly {displacement / 1000.0:F1} km");
    }

    /// <summary>
    /// Fly the entry and integrate what the fins could have done, without letting them do it —
    /// so the trajectory is the unguided one and the numbers are the authority available on it.
    /// </summary>
    private static (double Seconds, double ImpulseMps, double DisplacementMetres,
                    double PeakQ, double PeakG) Profile(
        KineticRod.Entry entry, KineticRod.Authority authority)
    {
        BallisticBody body = DeorbitShot.Earth;
        MunitionProfile rod = KineticRod.Profile(0.0, GuidanceMode.None);
        (double3 from, double3 velocity) = entry.StateCci();

        Slug round = new(from, velocity, null, 1, from, Vec.Zero)
        {
            Munition = rod,
            Ground = new DeorbitShot.Ball(),
            AirDensityAt = (pos, _) => DeorbitShot.DensityAt(pos),
        };

        double seconds = 0.0;
        double impulse = 0.0;
        double peakQ = 0.0;
        double peakG = 0.0;
        double elapsed = 0.0;

        List<(double At, double Accel, double Step)> commanded = [];

        while (round.State == RoundState.Flying && elapsed < 3600.0)
        {
            double density = DeorbitShot.DensityAt(round.PositionEcl);
            double3 air = body.GroundVelocityCci(round.PositionEcl);
            double q = KineticRod.DynamicPressure(density, Vec.Len(round.VelocityEcl - air));
            double available = authority.AvailableG(q);

            double dt = density > Medium.NoticeableDensity ? 0.02 : 0.5;

            peakQ = Math.Max(peakQ, q);
            peakG = Math.Max(peakG, available);

            if (available > KineticRod.UsableG)
            {
                impulse += available * 9.80665 * dt;
                seconds += dt;
                commanded.Add((elapsed, available * 9.80665, dt));
            }

            round.Update(dt, null, body.GravityCci(round.PositionEcl), air, from, rod, density);
            elapsed += dt;
        }

        // What that authority is worth as ground: an acceleration applied t seconds before impact
        // moves the impact by a*(T-t)*dt, and the whole of it is that summed. The half-a-t-squared
        // form is wrong here by an order of magnitude, because the authority is not constant —
        // nearly all of it arrives in the last seconds, where there is no time left to use it.
        double displacement = 0.0;
        foreach ((double at, double accel, double step) in commanded)
        {
            displacement += accel * (elapsed - at) * step;
        }

        return (seconds, impulse, displacement, peakQ, peakG);
    }

    // ------------------------------------------------------------------ the correction envelope

    /// <summary>
    /// <b>What the mod does today.</b> <see cref="Slug"/> applies the commanded lateral
    /// acceleration whatever the air is doing, so a rod given a tail kit steers in vacuum from the
    /// moment it is released — which is a reaction-control divert stage, not fins.
    /// </summary>
    [Theory]
    [InlineData(0.4)]
    [InlineData(3.0)]
    public void WhatTheUngatedTailKitCorrects(double maxG)
    {
        KineticRod.Entry entry = new(EntryAltitude, 7_400.0, 10.0);

        Report($"{maxG:F1} g applied whatever the air is doing, from 100 km",
               entry, KineticRod.Authority.Fiat(maxG));
    }

    /// <summary>
    /// <b>What a real tail kit would get.</b> The same law, with the authority read off the
    /// dynamic pressure the rod is actually in.
    /// </summary>
    [Theory]
    [InlineData(7_700.0, 3.4)]
    [InlineData(7_400.0, 3.0)]
    [InlineData(7_400.0, 10.0)]
    [InlineData(7_400.0, 20.0)]
    [InlineData(5_000.0, 20.0)]
    [InlineData(3_000.0, 45.0)]
    public void WhatAnAirBreathingTailKitCorrects(double speed, double gammaDeg)
    {
        KineticRod.Entry entry = new(EntryAltitude, speed, gammaDeg);

        Report($"entering at {speed / 1000.0:F1} km/s, {gammaDeg:F1} deg, 10 deg of trim",
               entry, KineticRod.Authority.FromDynamicPressure(10.0, 20.0));
    }

    /// <summary>
    /// The shape behind the edge: what a rod is left holding for a given ballistic error. Reported
    /// because an envelope is one number and the interesting part is how it degrades — a kit that
    /// removes 95% of a 10 km error is useful even where the edge says it cannot close.
    /// </summary>
    [Fact]
    public void HowTheCorrectionDegrades()
    {
        KineticRod.Entry entry = new(EntryAltitude, 7_400.0, 10.0);
        KineticRod.Authority aero = KineticRod.Authority.FromDynamicPressure(10.0, 20.0);

        Out.WriteLine("ballistic error -> what is left of it, entering at 7.4 km/s and 10 deg");
        Out.WriteLine("   asked      cross-track       down-track");

        foreach (double offset in new[] { 100.0, 300.0, 600.0, 1_000.0, 3_000.0, 10_000.0, 30_000.0 })
        {
            double across = KineticRod.Residual(entry, aero, lateral: true, offset);
            double along = KineticRod.Residual(entry, aero, lateral: false, offset);

            Out.WriteLine($"  {offset,6:F0} m {across,12:F1} m {along,15:F1} m");
        }
    }

    /// <summary>
    /// <b>Too little authority is worse than none.</b> Proportional navigation nulls the
    /// line-of-sight rate, and against a point on the ground five hundred kilometres away it starts
    /// commanding from the first step — so a kit that cannot finish the turn it began spends its
    /// whole flight off the arc it was on and arrives nowhere near either answer.
    ///
    /// <para>Which is why <em>gating the fins on dynamic pressure is not only realism</em>: it is
    /// what keeps the law quiet until there is enough authority to be worth using.</para>
    /// </summary>
    [Fact]
    public void HowMuchAuthorityBeforeSteeringHelpsAtAll()
    {
        KineticRod.Entry entry = new(EntryAltitude, 7_400.0, 10.0);
        const double error = 600.0;

        Out.WriteLine($"a {error:F0} m ballistic error, cross-track, entering at 7.4 km/s and 10 deg");
        Out.WriteLine("        authority   left over");

        double unsteered = KineticRod.Residual(entry, KineticRod.Authority.Fiat(0.0), true, error);

        foreach (double g in new[] { 0.0, 0.05, 0.1, 0.2, 0.4, 1.0, 3.0, 10.0 })
        {
            double fiat = KineticRod.Residual(entry, KineticRod.Authority.Fiat(g), true, error);
            Out.WriteLine($"  {g,6:F2} g flat {fiat,12:F0} m"
                          + (g <= 0.0 ? "   (no steering at all — the error itself)" : string.Empty));

            // The whole point of the table: there is a band where steering loses.
            if (g is > 0.0 and <= 1.0) Assert.True(fiat > unsteered, $"{g} g left {fiat:F0} m");
        }

        foreach (double alpha in new[] { 2.0, 5.0, 10.0 })
        {
            double aero = KineticRod.Residual(
                entry, KineticRod.Authority.FromDynamicPressure(alpha, 20.0), true, error);

            Out.WriteLine($"  {alpha,4:F0} deg of trim, off the air {aero,6:F1} m");
        }
    }

    /// <summary>
    /// What the frame the world is held to costs a rod that arrives at 7 km/s.
    ///
    /// <para><see cref="Medium.FaithfulStepInAir"/> is 50 ms, chosen against a Mk 21 that arrives
    /// at 2.7 km/s. A rod arrives nearly three times faster and pulls its correction in the last
    /// seconds, so the step it needs is its own question and this is the number to answer it
    /// with.</para>
    /// </summary>
    [Fact]
    public void WhatTheFrameCostsARodThatArrivesAtSevenKilometresASecond()
    {
        KineticRod.Entry entry = new(EntryAltitude, 7_400.0, 10.0);
        KineticRod.Authority aero = KineticRod.Authority.FromDynamicPressure(10.0, 20.0);

        double3 reference = KineticRod.Fly(KineticRod.Profile(0.0, GuidanceMode.None), entry,
                                           null, null, 0.002).GroundFixedCci;

        Out.WriteLine("frame in air -> what the step is worth, against a 2 ms reference");
        Out.WriteLine("             unguided moves    guided lands");

        foreach (double step in new[] { 0.005, 0.01, 0.02, Medium.FaithfulStepInAir, 0.1, 0.2 })
        {
            double3 unguided = KineticRod.Fly(KineticRod.Profile(0.0, GuidanceMode.None), entry,
                                              null, null, step).GroundFixedCci;

            double drift = DeorbitShot.GroundMetres(unguided, reference);
            double residual = KineticRod.Residual(entry, aero, true, 600.0, step);

            Out.WriteLine($"  {step * 1000.0,6:F0} ms {drift,14:F1} m {residual,14:F1} m");
        }
    }

    // ------------------------------------------------------------------ the shot as it is flown

    /// <summary>
    /// The 3,459 km shot end to end, with a rod on it and the ballistic solution deliberately
    /// wrong by the amount the flown group is wrong by.
    ///
    /// <para>This is the question the whole exercise is for: the delivery does not have to be
    /// exact, it has to land inside what the kit can still remove.</para>
    /// </summary>
    [Theory]
    [InlineData(450.0)]
    [InlineData(600.0)]
    [InlineData(1_400.0)]
    [InlineData(5_000.0)]
    public void TheFlownDeliveryErrorOnARodWithATailKit(double errorMetres)
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);
        KineticRod.Authority aero = KineticRod.Authority.FromDynamicPressure(10.0, 20.0);

        double3 unguided = KineticRod.Fly(KineticRod.Profile(0.0, GuidanceMode.None),
                                          from, arc.RequiredVelocityCci, null).GroundFixedCci;

        foreach (bool lateral in new[] { true, false })
        {
            double3 aim = KineticRod.Displace(unguided, errorMetres, lateral);

            KineticRod.Flight flight = KineticRod.Fly(KineticRod.Profile(aero.CeilingG),
                                                      from, arc.RequiredVelocityCci, aim, aero);

            (double along, double across) = KineticRod.MissComponents(flight.GroundFixedCci, aim);

            Out.WriteLine($"{errorMetres,6:F0} m {(lateral ? "cross-track" : "down-track ")} error"
                          + $" -> {along / 1000.0,8:F2} km down-track, {across,8:F1} m across;"
                          + $" arrived {flight.ArrivalSpeed:F0} m/s at {flight.ArrivalGammaDeg:F1} deg,"
                          + $" steered {flight.SteeredSeconds:F0} s, peak {flight.PeakG:F1} g");
        }
    }

    /// <summary>
    /// <b>Where the shipped steering law stops working, and why.</b> Proportional navigation with
    /// no gravity bias flattens a shallow arc: it reads a target below and ahead, pulls to null the
    /// line-of-sight rate, and turns a fall into a glide. The cross-track error goes to nothing and
    /// the rod lands tens of kilometres long.
    ///
    /// <para>The two axes have to be reported apart. One scalar miss reads as "the guidance did
    /// nothing", which is the opposite of what happened.</para>
    /// </summary>
    [Fact]
    public void WhereProportionalNavigationTurnsAFallIntoAGlide()
    {
        KineticRod.Authority aero = KineticRod.Authority.FromDynamicPressure(10.0, 20.0);

        Out.WriteLine("a 600 m cross-track error, entering at 7.7 km/s -- 98% of circular -- by how steep");
        Out.WriteLine("  entry   down-track      across    arrival   (down-track negative is short)");

        foreach (double gammaDeg in new[] { 2.0, 3.0, 5.0, 7.0, 10.0, 15.0, 25.0, 40.0 })
        {
            KineticRod.Entry entry = new(EntryAltitude, 7_700.0, gammaDeg);

            double3 unguided = KineticRod.Fly(KineticRod.Profile(0.0, GuidanceMode.None), entry, null)
                .GroundFixedCci;
            double3 aim = KineticRod.Displace(unguided, 600.0, lateral: true);

            KineticRod.Flight flight = KineticRod.Fly(KineticRod.Profile(aero.CeilingG), entry, aim, aero);
            (double along, double across) = KineticRod.MissComponents(flight.GroundFixedCci, aim);

            Out.WriteLine($"  {gammaDeg,4:F0} deg {along / 1000.0,10:F2} km {across,10:F1} m "
                          + $"  {flight.ArrivalSpeed,6:F0} m/s at {flight.ArrivalGammaDeg,5:F1} deg");

            double left = DeorbitShot.GroundMetres(flight.GroundFixedCci, aim);

            if (gammaDeg <= 3.0) Assert.True(left > 10_000.0, $"{gammaDeg} deg left only {left:F0} m");
            else Assert.True(left < Arrived, $"{gammaDeg} deg left {left:F0} m");
        }
    }

    /// <summary>
    /// Whether the bias the glide leaves can be tuned out of the shipped law with the one knob it
    /// has. <see cref="MunitionProfile.GravityCompensation"/> is what stops the round fighting the
    /// fall — a bomb wants none of it, and the question is whether a rod does.
    /// </summary>
    [Fact]
    public void WhetherGravityCompensationRescuesTheGlide()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);
        KineticRod.Authority aero = KineticRod.Authority.FromDynamicPressure(10.0, 20.0);

        double3 unguided = KineticRod.Fly(KineticRod.Profile(0.0, GuidanceMode.None),
                                          from, arc.RequiredVelocityCci, null).GroundFixedCci;
        double3 aim = KineticRod.Displace(unguided, 600.0, lateral: true);

        Out.WriteLine("the 3,459 km shot, 600 m cross-track, by how much gravity is biased out");

        foreach (double compensation in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            MunitionProfile rod = KineticRod.Profile(aero.CeilingG, GuidanceMode.Inertial, compensation);
            KineticRod.Flight flight = KineticRod.Fly(rod, from, arc.RequiredVelocityCci, aim, aero);
            (double along, double across) = KineticRod.MissComponents(flight.GroundFixedCci, aim);

            Out.WriteLine($"  {compensation,4:F2} -> {along / 1000.0,9:F2} km down-track, {across,8:F1} m across, "
                          + $"arrived {flight.ArrivalSpeed:F0} m/s at {flight.ArrivalGammaDeg:F1} deg");
        }
    }

    /// <summary>
    /// Whether the number that rescued one shot rescues the rest of them. A knob tuned on a single
    /// geometry is a coincidence until it has been asked about a second.
    /// </summary>
    [Fact]
    public void WhetherThatNumberHoldsAcrossEntryAngles()
    {
        KineticRod.Authority aero = KineticRod.Authority.FromDynamicPressure(10.0, 20.0);

        Out.WriteLine("worst of a 600 m cross-track and a 600 m down-track error, in metres left over");
        Out.WriteLine("  entry            5.0 km/s       7.0 km/s       7.7 km/s");

        foreach (double compensation in new[] { 0.0, 0.5, 1.0 })
        {
            Out.WriteLine($"  gravity biased out by {compensation:F2}:");

            foreach (double gammaDeg in new[] { 2.0, 3.0, 5.0, 10.0, 30.0 })
            {
                string row = $"  {gammaDeg,4:F0} deg";

                foreach (double speed in new[] { 5_000.0, 7_000.0, 7_700.0 })
                {
                    double worst = Worst(new KineticRod.Entry(EntryAltitude, speed, gammaDeg),
                                         compensation);

                    // Fully biasing out gravity is the only setting that closes every cell. Half
                    // of it looks like a fix on one geometry and leaves 188 km at two degrees.
                    if (compensation >= 1.0)
                    {
                        Assert.True(worst < Arrived,
                                    $"{gammaDeg} deg at {speed} m/s left {worst:F1} m fully compensated");
                    }

                    row += $" {worst,14:F1}";
                }

                Out.WriteLine(row);
            }
        }

        double Worst(KineticRod.Entry entry, double compensation)
        {
            double3 unguided = KineticRod.Fly(KineticRod.Profile(0.0, GuidanceMode.None), entry, null)
                .GroundFixedCci;
            double worst = 0.0;

            foreach (bool lateral in new[] { true, false })
            {
                double3 aim = KineticRod.Displace(unguided, 600.0, lateral);
                MunitionProfile rod = KineticRod.Profile(aero.CeilingG, GuidanceMode.Inertial,
                                                         compensation);

                worst = Math.Max(worst,
                                 DeorbitShot.GroundMetres(
                                     KineticRod.Fly(rod, entry, aim, aero).GroundFixedCci, aim));
            }

            return worst;
        }
    }

    /// <summary>
    /// The same steering, the same rod, the same air, released a hundred kilometres apart on one
    /// trajectory — because the constructed entries close a 600 m error at three degrees and the
    /// flown arc, which reaches the air at 3.4 degrees, does not.
    /// </summary>
    [Fact]
    public void WhetherWhereItWasReleasedChangesWhatTheKitCanDo()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);
        KineticRod.Authority aero = KineticRod.Authority.FromDynamicPressure(10.0, 20.0);

        (double3 at100, double3 speed100) = StateAtAltitude(from, arc.RequiredVelocityCci, 100_000.0);

        // The same speed and flight path angle, rebuilt on the +x axis the way every constructed
        // entry in this file is. If this one behaves and the one above does not, the difference is
        // not the entry conditions.
        double3 relative = speed100 - DeorbitShot.Earth.GroundVelocityCci(at100);
        double gamma = (Vec.AngleBetween(at100, relative) * 180.0 / Math.PI) - 90.0;
        (double3 rebuiltAt, double3 rebuiltVelocity) =
            new KineticRod.Entry(100_000.0, Vec.Len(speed100), gamma).StateCci();

        Out.WriteLine($"at 100 km: {Vec.Len(speed100):F1} m/s, {gamma:F2} deg below horizontal, "
                      + $"{Math.Abs(speed100.Z):F3} m/s out of plane");

        (string what, double3 position, double3 velocity)[] releases =
        [
            ("released at 200 km, as the bus does", from, arc.RequiredVelocityCci),
            ("the same trajectory picked up at 100 km", at100, speed100),
            ("rebuilt from that speed and angle alone", rebuiltAt, rebuiltVelocity),
        ];

        foreach ((string what, double3 position, double3 velocity) in releases)
        {
            double3 unguided = KineticRod.Fly(KineticRod.Profile(0.0, GuidanceMode.None),
                                              position, velocity, null).GroundFixedCci;
            double3 aim = KineticRod.Displace(unguided, 600.0, lateral: true);

            KineticRod.Flight flight = KineticRod.Fly(KineticRod.Profile(aero.CeilingG),
                                                      position, velocity, aim, aero);
            (double along, double across) = KineticRod.MissComponents(flight.GroundFixedCci, aim);

            Out.WriteLine($"{what}:");
            Out.WriteLine($"  {along / 1000.0:F2} km down-track, {across:F1} m across; "
                          + $"steered {flight.SteeredSeconds:F0} s of a {flight.Seconds:F0} s flight, "
                          + $"peak {flight.PeakG:F1} g");
        }
    }

    /// <summary>Where a rod on this trajectory is when it first falls below an altitude.</summary>
    private static (double3 Position, double3 Velocity) StateAtAltitude(
        double3 from, double3 velocityCci, double altitude)
    {
        BallisticBody body = DeorbitShot.Earth;
        MunitionProfile rod = KineticRod.Profile(0.0, GuidanceMode.None);

        Slug round = new(from, velocityCci, null, 1, from, Vec.Zero)
        {
            Munition = rod,
            Ground = new DeorbitShot.Ball(),
            AirDensityAt = (pos, _) => DeorbitShot.DensityAt(pos),
        };

        while (round.State == RoundState.Flying && body.AltitudeOf(round.PositionEcl) > altitude)
        {
            double density = DeorbitShot.DensityAt(round.PositionEcl);

            round.Update(density > Medium.NoticeableDensity ? 0.02 : 0.5, null,
                         body.GravityCci(round.PositionEcl),
                         body.GroundVelocityCci(round.PositionEcl), from, rod, density);
        }

        return (round.PositionEcl, round.VelocityEcl);
    }

    /// <summary>
    /// What links the two framings above: the shot the mod actually flies is <em>shallower</em> at
    /// the top of the air than any of the constructed entries, which is why it lands in the one
    /// place the shipped law falls over.
    /// </summary>
    [Fact]
    public void HowSteeplyTheFlownShotReachesTheAir()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);
        BallisticBody body = DeorbitShot.Earth;

        MunitionProfile rod = KineticRod.Profile(0.0, GuidanceMode.None);
        Slug round = new(from, arc.RequiredVelocityCci, null, 1, from, Vec.Zero)
        {
            Munition = rod,
            Ground = new DeorbitShot.Ball(),
            AirDensityAt = (pos, _) => DeorbitShot.DensityAt(pos),
        };

        double next = 150_000.0;
        Out.WriteLine("the 3,459 km deorbit arc, flown with a rod on it:");

        for (int i = 0; i < 400_000 && round.State == RoundState.Flying; i++)
        {
            double altitude = body.AltitudeOf(round.PositionEcl);

            if (altitude <= next)
            {
                double3 relative = round.VelocityEcl - body.GroundVelocityCci(round.PositionEcl);
                double gamma = (Vec.AngleBetween(round.PositionEcl, relative) * 180.0 / Math.PI) - 90.0;
                double q = KineticRod.DynamicPressure(DeorbitShot.DensityAt(round.PositionEcl),
                                                      Vec.Len(relative));

                Out.WriteLine($"  {next / 1000.0,4:F0} km: {Vec.Len(relative),6:F0} m/s at "
                              + $"{gamma,5:F2} deg, q {q / 1000.0,8:F1} kPa, "
                              + $"{KineticRod.AvailableG(q, 10.0),6:F2} g available");

                next -= 25_000.0;
                if (next < 0.0) break;
            }

            double density = DeorbitShot.DensityAt(round.PositionEcl);
            double dt = density > Medium.NoticeableDensity ? 0.02 : 0.5;

            round.Update(dt, null, body.GravityCci(round.PositionEcl),
                         body.GroundVelocityCci(round.PositionEcl), from, rod, density);
        }
    }

    /// <summary>Walk the ladder and say where it stopped, for one entry and one authority.</summary>
    private void Report(string what, KineticRod.Entry entry, KineticRod.Authority authority)
    {
        (double across, double acrossFail, double acrossLeft) =
            KineticRod.Envelope(entry, authority, lateral: true);
        (double along, double alongFail, double alongLeft) =
            KineticRod.Envelope(entry, authority, lateral: false);

        Out.WriteLine($"{what}:");
        Out.WriteLine($"  cross-track {across / 1000.0,8:F2} km removed"
                      + $"   (first failure {Kilometres(acrossFail)}, {acrossLeft:F0} m left)");
        Out.WriteLine($"  down-track  {along / 1000.0,8:F2} km removed"
                      + $"   (first failure {Kilometres(alongFail)}, {alongLeft:F0} m left)");
    }

    private static string Kilometres(double metres)
        => double.IsPositiveInfinity(metres) ? "none in the ladder" : $"{metres / 1000.0:F2} km";

    // ------------------------------------------------------------------ what a rod does to the arc

    /// <summary>
    /// The 3,459 km shot flown with a rod instead of a Mk 21. Drag is what the aim correction
    /// spends most of its authority on, and a rod removes most of the drag.
    /// </summary>
    [Fact]
    public void WhatARodDoesToTheBallisticProblem()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out double3 target);

        foreach ((string what, MunitionProfile munition) in new[]
                 {
                     ("Mk 21", DeorbitShot.Warhead),
                     ("rod", KineticRod.Profile(0.0, GuidanceMode.None)),
                 })
        {
            Assert.True(ImpactPredictor.TryPredict(DeorbitShot.Earth, from, arc.RequiredVelocityCci,
                                                   1.0, 20_000.0, out ImpactPredictor.Impact vac));
            Assert.True(ImpactPredictor.TryPredict(DeorbitShot.Earth, from, arc.RequiredVelocityCci,
                                                   1.0, 20_000.0, out ImpactPredictor.Impact air,
                                                   null, null,
                                                   new ImpactPredictor.Drag(DeorbitShot.DensityAt, munition)));

            double gamma = 90.0 - (Vec.AngleBetween(air.PointCci, air.VelocityCci) * 180.0 / Math.PI);
            double lost = DeorbitShot.GroundMetres(vac.GroundFixedPointCci, air.GroundFixedPointCci);

            Out.WriteLine($"{what,-6} arrives at {Vec.Len(air.VelocityCci),6:F0} m/s, "
                          + $"gamma {-gamma,5:F2} deg, "
                          + $"drag costs {lost / 1000.0,6:F1} km of range, "
                          + $"{air.Seconds - arc.FlightSeconds,5:F1} s late");

            // The aim correction exists to remove the drag term. A rod removes most of it by being
            // a rod, which is worth knowing before anything is tuned to chase the rest.
            if (munition.DragK < 1e-6) Assert.True(lost < 10_000.0, $"the rod lost {lost:F0} m");
            Assert.True(Vec.IsFinite(target));
        }
    }

    /// <summary>
    /// <b>The flat limit the profile holds is not a limit a rod can be given.</b>
    /// <see cref="MunitionProfile.MaxLateralG"/> is applied by <see cref="Interceptor.GuidanceAccel"/>
    /// whatever the air is doing, so a rod released above the atmosphere steers for the whole coast
    /// — five minutes of it on the flown shot — and arrives a thousand kilometres short at every
    /// value tried, with gravity biased out or not.
    ///
    /// <para>Which makes the dynamic-pressure gate the first missing piece rather than a refinement:
    /// what a rod needs is not a different number in that field but for the field to stop applying
    /// where there is nothing to push against.</para>
    /// </summary>
    [Fact]
    public void WhyAFlatAuthorityCannotBeGivenToARod()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out _);

        double3 unguided = KineticRod.Fly(KineticRod.Profile(0.0, GuidanceMode.None),
                                          from, arc.RequiredVelocityCci, null).GroundFixedCci;
        double3 aim = KineticRod.Displace(unguided, 600.0, lateral: true);

        Out.WriteLine("the flown 3,459 km shot, 600 m cross-track error");
        Out.WriteLine("            comp 0.0        comp 1.0     (metres left over)");

        foreach (double g in new[] { 0.4, 1.0, 3.0, 10.0, 35.0 })
        {
            string row = $"  {g,5:F1} g flat";

            foreach (double compensation in new[] { 0.0, 1.0 })
            {
                MunitionProfile rod = KineticRod.Profile(g, GuidanceMode.Inertial, compensation);
                KineticRod.Flight flight = KineticRod.Fly(rod, from, arc.RequiredVelocityCci, aim,
                                                          KineticRod.Authority.Fiat(g));
                double left = DeorbitShot.GroundMetres(flight.GroundFixedCci, aim);

                Assert.True(left > 100_000.0,
                            $"{g} g flat at comp {compensation} left {left:F0} m — the flat "
                            + "authority stopped being catastrophic, which is what this pins");

                row += $" {left,15:F0}";
            }

            Out.WriteLine(row);
        }

        foreach (double compensation in new[] { 0.0, 1.0 })
        {
            KineticRod.Authority aero = KineticRod.Authority.FromDynamicPressure(10.0, 20.0);
            MunitionProfile rod = KineticRod.Profile(aero.CeilingG, GuidanceMode.Inertial, compensation);
            KineticRod.Flight flight = KineticRod.Fly(rod, from, arc.RequiredVelocityCci, aim, aero);
            double left = DeorbitShot.GroundMetres(flight.GroundFixedCci, aim);

            Out.WriteLine($"  gated on the air, comp {compensation:F1}: {left:F1} m left, "
                          + $"steered {flight.SteeredSeconds:F0} s of {flight.Seconds:F0} s, "
                          + $"peak {flight.PeakG:F1} g");

            // The B61's tail kit biases out nothing, and on this arc that is the difference
            // between arriving and falling short by a county.
            if (compensation >= 1.0) Assert.True(left < Arrived, $"gated and compensated left {left:F1} m");
            else Assert.True(left > 10_000.0, $"gated and uncompensated left only {left:F1} m");
        }
    }

    /// <summary>Close enough that nothing else in this file is what decided it.</summary>
    private const double Arrived = 25.0;

    // ------------------------------------------------------------------ killing without a charge

    /// <summary>
    /// <b>The mod can already express "kills by impact alone", and only just.</b> A zero charge
    /// takes every radius to zero, so the splash sweep reaches nothing and the only way a rod kills
    /// is by the contact rule — which is exactly what a kinetic weapon is.
    /// </summary>
    [Fact]
    public void AZeroChargeRoundReachesNothingItDoesNotTouch()
    {
        MunitionProfile rod = KineticRod.Profile(0.0);

        Assert.Equal(0.0, rod.LethalRadius);
        Assert.Equal(0.0, rod.BlastRadius);
        Assert.Equal(0.0, Warhead.EffectScale(rod.ChargeKg));

        // The kill path is `MissDistance <= LethalRadius + MeanRadius`, so with no charge the
        // craft's own bounding sphere is the whole envelope. A hull strike reports zero, which is
        // what makes a touch lethal.
        Assert.Equal(BlastEffect.Lethal, BlastSweep.Effect(0.0, rod));
        Assert.Equal(BlastEffect.Untouched, BlastSweep.Effect(0.01, rod));

        Out.WriteLine("charge 0 kg: lethal 0 m, blast 0 m, effect scale 0 — nothing is drawn, "
                      + "nothing is heard, and only a contact kills");
    }

    /// <summary>
    /// The contact rule is what a rod would kill with, and it needs the hull test to be the thing
    /// that answers. Without one, a zero-charge round can still only reach a body's bounding
    /// sphere — which is the sphere rejecting, not a kill.
    /// </summary>
    [Fact]
    public void TheContactRuleIsWhatAKineticRoundKillsWith()
    {
        MunitionProfile rod = KineticRod.Profile(0.0);

        // Passing 15 m off the centre of a body whose bounding sphere is 20 m: inside the sphere,
        // outside the skin. That is the case the two phases disagree about.
        double3 separation = new(0, 15.0, 30.0);
        double3 closing = new(0, 0, -3_000.0);

        Assert.True(ContactSweep.TryStrike(separation, closing, 0.02, rod.FuseRadius, 20.0,
                                           null, null, out _, out double sphereMiss));
        Out.WriteLine($"with no hull to ask, the sphere's verdict stands at {sphereMiss:F1} m — "
                      + $"lethal, because the kill test is `miss <= {rod.LethalRadius:F0} + 20 m`");

        Assert.False(ContactSweep.TryStrike(separation, closing, 0.02, rod.FuseRadius, 20.0,
                                            new AlwaysMissed(), null, out _, out _));
        Out.WriteLine("with a hull that says it passed, nothing happens at all");

        Assert.True(ContactSweep.TryStrike(separation, closing, 0.02, rod.FuseRadius, 20.0,
                                           new AlwaysStruck(), null, out _, out double hullMiss));
        Assert.Equal(0.0, hullMiss);
        Out.WriteLine($"with a hull that says it touched, {hullMiss:F1} m — which is what a zero "
                      + "charge needs, and the only way one kills");
    }

    private sealed class AlwaysStruck : IHullTest
    {
        public HullVerdict Judge(object? body, double3 separation, double3 travel, out double fraction)
        {
            fraction = 0.5;
            return HullVerdict.Struck;
        }
    }

    private sealed class AlwaysMissed : IHullTest
    {
        public HullVerdict Judge(object? body, double3 separation, double3 travel, out double fraction)
        {
            fraction = 0.0;
            return HullVerdict.Missed;
        }
    }
}
