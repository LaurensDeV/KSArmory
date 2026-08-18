using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The registry contract, which is what adding a weapon system relies on.
///
/// These are cheap and mostly obvious, which is the point: the failure mode of a registry is a
/// typo in a name or a mismatched count, and neither shows up until something silently picks
/// the wrong profile in game.
/// </summary>
public class ArsenalTests
{
    [Fact]
    public void EveryLauncherNamesARegisteredMunitionAndSensor()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            Assert.Equal(launcher.Munition, Arsenal.MunitionNamed(launcher.Munition).Name);
            Assert.Equal(launcher.Sensor, Arsenal.SensorNamed(launcher.Sensor).Name);

            // The cannon's round as well as the missile's. Arsenal.Named falls back to element
            // zero, which for munitions is a 20 kg missile at 45 m/s under a rocket boost: a gun
            // naming a shell that does not exist compiles, loads, passes every other gate and
            // fires warheads out of its barrel.
            if (launcher.GunMunition is { } shell)
            {
                Assert.Equal(shell, Arsenal.MunitionNamed(shell).Name);
            }
        }
    }

    [Fact]
    public void LauncherPartIdsAreUniqueAndNonEmpty()
    {
        // Discovery is by part Id, so a duplicate would make which system you get depend on
        // registration order.
        var seen = new HashSet<string>();
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            Assert.False(string.IsNullOrWhiteSpace(launcher.PartId));
            Assert.True(seen.Add(launcher.PartId), $"duplicate part Id {launcher.PartId}");
        }
    }

    [Fact]
    public void MunitionAndSensorNamesAreUnique()
    {
        Assert.Equal(Arsenal.Munitions.Count, Arsenal.Munitions.Select(m => m.Name).Distinct().Count());
        Assert.Equal(Arsenal.Sensors.Count, Arsenal.Sensors.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void LauncherLookupMatchesOnPartIdAndRejectsAnythingElse()
    {
        Assert.Same(Arsenal.PantsirS1, Arsenal.LauncherForPart(Arsenal.PantsirS1.PartId));
        Assert.Null(Arsenal.LauncherForPart("SomeOtherMod_Prefab_Thing"));
        Assert.Null(Arsenal.LauncherForPart(null));
        Assert.Null(Arsenal.LauncherForPart(""));
    }

    [Fact]
    public void UnknownNamesFallBackRatherThanThrow()
    {
        // A launcher naming a round that does not exist is a typo in Arsenal, not a reason for
        // the game to fall over mid-flight.
        Assert.NotNull(Arsenal.MunitionNamed("no such round"));
        Assert.NotNull(Arsenal.SensorNamed("no such sensor"));
    }

    [Fact]
    public void EveryLauncherHasAsManyTubesAsItHasTubePositions()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            Assert.Equal(launcher.Tubes.Length, launcher.TubeCount);
        }
    }

    /// <summary>
    /// A launcher must be able to shoot with <em>something</em>: tubes, a cannon, or both.
    ///
    /// <para>Not "every launcher has a tube": a CIWS is a gun and nothing else, and
    /// <c>TubeCount</c> of zero is a supported shape rather than a broken profile. A launcher with
    /// neither is a part that can never fire, and nothing else would report that.</para>
    /// </summary>
    [Fact]
    public void EveryLauncherCanActuallyShootWithSomething()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            Assert.True(launcher.TubeCount > 0 || launcher.HasCannon,
                        $"{launcher.DisplayName} has neither tubes nor a cannon");
        }
    }

    [Fact]
    public void ATurretedLauncherDeclaresThePiecesItAnimates()
    {
        // A traverse has to carry something, or it turns and nothing follows. What it carries can
        // be pods or a cannon -- a CIWS traverses a gun and has no launcher assembly at all -- but
        // whichever it declares needs a trunnion offset, because a pivot measured from nothing is
        // an assembly that swings around the mount instead of elevating in place.
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            if (launcher.TurretMarker is null) continue;

            Assert.True(launcher.PodsMarker is not null || launcher.GunsMarker is not null,
                        $"{launcher.DisplayName} traverses but carries nothing that moves with it");

            if (launcher.PodsMarker is not null)
            {
                Assert.True(Vec.Len(launcher.PodPivotFromTurret) > 0.0,
                            $"{launcher.DisplayName} elevates pods with no trunnion offset");
            }

            if (launcher.GunsMarker is not null)
            {
                Assert.True(Vec.Len(launcher.GunPivotFromTurret) > 0.0,
                            $"{launcher.DisplayName} elevates guns with no trunnion offset");
            }
        }
    }

    [Fact]
    public void ProfilesAreSelectableAndDriveTheTurretLimits()
    {
        (MunitionProfile munition, SensorProfile sensor) = Arsenal.LoadoutFor(Arsenal.PantsirS1);

        Assert.Equal("57E6", munition.Name);
        Assert.Equal("1RS1", sensor.Name);

        var turret = new Turret();
        Arsenal.PantsirS1.ConfigureTurret(turret);
        Assert.Equal(float.DegreesToRadians(Arsenal.PantsirS1.MaxElevationDeg), turret.MaxElevationRad, 9);
        Assert.Equal(float.DegreesToRadians(Arsenal.PantsirS1.ForwardArcDeg), turret.ForwardArcRad, 9);
        Assert.Equal(float.DegreesToRadians(Arsenal.PantsirS1.ForwardPlateauDeg), turret.ForwardPlateauRad, 9);
    }

    /// <summary>
    /// The registry ships more than one system, and they have to resolve to different weapons.
    ///
    /// <para>Every other assertion in this file is satisfied by a registry of one. This is the one
    /// that distinguishes "picked the right system" from "picked the only system", and it is what
    /// a further entry inherits.</para>
    /// </summary>
    [Fact]
    public void TwoRegisteredSystemsResolveToDifferentWeapons()
    {
        Assert.True(Arsenal.Launchers.Count >= 2);

        (MunitionProfile pantsirRound, SensorProfile pantsirSet) = Arsenal.LoadoutFor(Arsenal.PantsirS1);
        (MunitionProfile railRound, SensorProfile railSet) = Arsenal.LoadoutFor(Arsenal.SidewinderRail);

        Assert.NotSame(pantsirRound, railRound);
        Assert.NotSame(pantsirSet, railSet);

        Assert.Same(Arsenal.SidewinderRail, Arsenal.LauncherForPart(Arsenal.SidewinderRail.PartId));
        Assert.Same(Arsenal.PantsirS1, Arsenal.LauncherForPart(Arsenal.PantsirS1.PartId));
    }

    /// <summary>
    /// The AIM-9J boosts hard and then coasts, which is what a Sidewinder does and what decides
    /// how it looks in flight.
    ///
    /// <para>The Mk 17 is a booster with no sustainer: about 2.2 s of thrust to roughly Mach 2.5,
    /// then drag for the rest of the flight. Written down because the alternative is not a
    /// slightly different number but a different weapon — a five-second burn holds speed like the
    /// Pantsir's two-stage round and reads as far too quick.</para>
    /// </summary>
    [Fact]
    public void TheSidewinderBoostsBrieflyAndThenCoasts()
    {
        MunitionProfile round = Arsenal.Missile9J;

        // A booster, not a sustainer. Anything approaching the flight time is the wrong shape.
        Assert.InRange(round.BoostSeconds, 1.5f, 3.0f);
        Assert.True(round.BoostSeconds < round.MaxFlightSeconds / 10f);

        // Peak speed at burnout, before drag: about Mach 2.5 at sea level.
        double peak = round.LaunchSpeed + (round.BoostAccel * round.BoostSeconds);
        Assert.InRange(peak, 800.0, 900.0);

        // And it must actually bleed that off, or "coasts" means "holds speed forever".
        Assert.True(round.DragK > 0f);
    }

    /// <summary>
    /// The rail is the shipped example of a launcher with nothing that moves, so the shape
    /// <see cref="AFixedLauncherIsJustAProfileWithNothingThatMoves"/> describes has to hold for a
    /// real entry rather than only for one the test builds.
    /// </summary>
    [Fact]
    public void TheSidewinderRailIsAFixedLauncherAndSaysSo()
    {
        LauncherProfile rail = Arsenal.SidewinderRail;

        Assert.Null(rail.TurretMarker);
        Assert.Null(rail.PodsMarker);
        Assert.False(rail.Trains);
        Assert.False(rail.HasCannon);
        Assert.Equal(1, rail.TubeCount);

        // A fixed launcher's rounds have no pods to follow, so the tube is the only thing that
        // says which way they leave. Without a direction they would depart along whatever the
        // fallback produces, which for a rail is into the craft it is bolted to.
        Assert.True(rail.Tubes[0].HasOwnDirection);

        // Never refilled: the round count in the panel is the number of rails fitted.
        Assert.Equal(0f, rail.ReloadSeconds);

        // Leaves along the rail and pushed clear of the mount, rather than pivoting onto the
        // target at the muzzle. A rail has no walls to hold the round in, so separation is
        // outward as well as forward -- without it the round departs along the skin of whatever
        // carries it.
        Assert.True(rail.LaunchAlongTube);
        Assert.True(rail.EjectAwayFromMount > 0f);
        Assert.Equal(0f, rail.LaunchLoft);

        // And coasts before it steers, so the turn onto the target happens clear of the craft.
        Assert.True(Arsenal.MunitionNamed(rail.Munition).SeparationSeconds > 0f);
    }

    [Fact]
    public void AFixedLauncherIsJustAProfileWithNothingThatMoves()
    {
        // The shape a future non-turreted system takes. Nothing should require a turret.
        var fixedLauncher = new LauncherProfile
        {
            PartId = "Test_Prefab_FixedRail",
            DisplayName = "test rail",
            Munition = "57E6",
            Sensor = "1RS1",
            Tubes = [new(1, 0, 0), new(1, 0, 0.4)],
            LaunchAlongTube = false,
        };

        Assert.Null(fixedLauncher.TurretMarker);
        Assert.Null(fixedLauncher.PodsMarker);
        Assert.Equal(2, fixedLauncher.TubeCount);
        Assert.NotNull(Arsenal.MunitionNamed(fixedLauncher.Munition));
    }

    /// <summary>
    /// A prefab is one Part with its radar, optical head and cannon as SubParts, so the survey
    /// walking parts finds a launcher and stops. The roles it carries have to be declared, or a
    /// system with a camera reports as having none.
    /// </summary>
    [Fact]
    public void ThePantsirReportsTheRolesItCarriesInside()
    {
        List<SurveyedPart> parts =
            [new SurveyedPart(Arsenal.PantsirS1.PartId, default, doubleQuat.Identity)];

        WeaponInventory inv = WeaponSurvey.Survey(parts, Arsenal.Components);

        Assert.Equal(1, inv.CountOf(WeaponRole.Launcher));
        Assert.Equal(1, inv.CountOf(WeaponRole.Sensor));
        Assert.Equal(1, inv.CountOf(WeaponRole.Gun));

        // The director on its turret roof. Declared rather than found, because it is a subpart of
        // the launcher and the survey only walks parts -- which is the whole reason Provides
        // exists. A standalone director fitted beside it is found on its own and counts again.
        Assert.Equal(1, inv.CountOf(WeaponRole.Camera));
        Assert.Equal(1, inv.CountOf(WeaponRole.FireControl));
    }

    /// <summary>
    /// Declared roles are not a licence to invent parts: a built-in still needs the part it is
    /// built into, so a craft carrying nothing of ours reports nothing.
    /// </summary>
    [Fact]
    public void BuiltInRolesNeedThePartTheyAreBuiltInto()
    {
        List<SurveyedPart> parts = [new SurveyedPart("SomeoneElsesTank", default, doubleQuat.Identity)];

        Assert.False(WeaponSurvey.Survey(parts, Arsenal.Components).IsWeaponSystem);
    }

    /// <summary>
    /// Every launcher is also a component, because they are two registries keyed on the same part
    /// Id and only one of them decides whether a craft is a weapons system at all.
    ///
    /// <para>A launcher missing here loads, resolves its tubes, matches <c>LauncherForPart</c> and
    /// is then invisible: the panel says "no weapons systems" about a craft carrying it, with
    /// nothing in any log and nothing on screen but a part that does nothing.</para>
    /// </summary>
    [Fact]
    public void EveryRegisteredLauncherIsAlsoARecognisedComponent()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            Assert.True(Arsenal.Components.Any(c => c.PartId == launcher.PartId),
                        $"{launcher.DisplayName} ({launcher.PartId}) is registered as a launcher "
                        + "but not as a component, so no craft carrying it becomes a weapons system");
        }
    }

    /// <summary>
    /// The panel decides whether to draw the guidance section from <c>Armament.Steers</c>; the
    /// system decides which flight model to build when it fires. Two answers to one question, and
    /// they have to agree for every registered launcher.
    ///
    /// <para>The two magazines reach that decision differently, which is what makes this worth
    /// pinning: a belt is built as a <c>Slug</c> outright, so its munition's <c>Guidance</c> is
    /// never read and is left at a default that says the opposite. Only a tube reaches the branch.
    /// Reading either term alone is wrong, and each is wrong about a different launcher — the slot
    /// alone offers a bomb rack a guidance section, the round alone offers one to a Phalanx.</para>
    /// </summary>
    [Fact]
    public void SteersAgreesWithTheFlightModelForEveryArmament()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            WeaponFit fit = WeaponFit.Of(launcher, Arsenal.SensorNamed(launcher.Sensor));

            foreach (Armament arm in fit.Armaments)
            {
                bool flownAsInterceptor =
                    arm.Kind == ArmamentKind.Tubes
                    && Arsenal.MunitionNamed(arm.Munition).Guidance != GuidanceMode.None;

                Assert.True(arm.Steers == flownAsInterceptor,
                            $"{launcher.DisplayName} / {arm.Label}: the panel says "
                            + $"Steers={arm.Steers} while the round is flown as "
                            + (flownAsInterceptor ? "an Interceptor" : "a Slug"));
            }
        }
    }

    /// <summary>
    /// And the reverse: a component naming a launcher role must name a launcher that exists, or
    /// the survey reports a system the loadout cannot be resolved for.
    /// </summary>
    [Fact]
    public void EveryLauncherComponentNamesARegisteredLauncher()
    {
        foreach (ComponentProfile component in Arsenal.Components)
        {
            if (component.Role != WeaponRole.Launcher) continue;

            Assert.True(Arsenal.Launchers.Any(l => l.PartId == component.PartId),
                        $"component {component.DisplayName} ({component.PartId}) claims to be a "
                        + "launcher, but no LauncherProfile has that part Id");
        }
    }

    /// <summary>
    /// A provided row is declared as a profile's DisplayName, and the panel decides whether that row
    /// belongs to the crewed system by matching it back against the profile the system is running.
    /// So the two have to be the same string, resolved from the same registry — anything else is a
    /// second name for one thing, and it fails silently in the only place nobody can unit-test.
    ///
    /// <para>What that costs: a Pantsir reports its working cannon as "fitted, not run" the moment
    /// the panel matches a row called "2A38M 30 mm cannon" against <c>Armament.Label</c>, which is
    /// the belt's heading — "Cannon". Fire control reads neither, so the gun fires throughout and
    /// only the panel lies.</para>
    /// </summary>
    [Fact]
    public void EveryProvidedGunAndSensorRowNamesTheProfileItsSystemRuns()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            ComponentProfile? component = null;
            for (int i = 0; i < Arsenal.Components.Count; i++)
            {
                if (Arsenal.Components[i].PartId == launcher.PartId
                    && Arsenal.Components[i].Role == WeaponRole.Launcher)
                {
                    component = Arsenal.Components[i];
                }
            }

            Assert.NotNull(component);

            WeaponFit fit = WeaponFit.Of(launcher, Arsenal.SensorNamed(launcher.Sensor));

            foreach (BuiltInComponent provided in component!.Provides)
            {
                if (provided.Role == WeaponRole.Sensor)
                {
                    Assert.Equal(Arsenal.SensorNamed(launcher.Sensor).DisplayName, provided.DisplayName);
                }
                else if (provided.Role == WeaponRole.Gun)
                {
                    Assert.True(fit.FirstOf(ArmamentKind.Belt) is not null,
                        $"{launcher.DisplayName} declares a Gun row and its fit carries no belt");

                    // The question the panel asks, asked here where it can be checked.
                    Assert.True(fit.Describes(ArmamentKind.Belt, provided.DisplayName),
                        $"{launcher.DisplayName}'s Gun row is called '{provided.DisplayName}', "
                        + "which its own fit does not recognise -- the panel will report a working "
                        + "gun as 'fitted, not run'");
                }
            }
        }
    }

    /// <summary>
    /// The heading a belt is displayed under is not its identity. <c>Armament.Label</c> is
    /// "Cannon"; the row naming that armament is "2A38M 30 mm cannon". Matching on the first is
    /// what made every Pantsir report its gun as not run.
    /// </summary>
    [Fact]
    public void AFitDoesNotRecogniseItsArmamentByTheHeadingItIsListedUnder()
    {
        WeaponFit fit = WeaponFit.Of(Arsenal.PantsirS1, Arsenal.SearchRadar1Rs1);
        Armament belt = fit.FirstOf(ArmamentKind.Belt)!.Value;

        Assert.True(fit.Describes(ArmamentKind.Belt, Arsenal.Cannon30Mm.DisplayName));

        // The two are different strings, and only one of them identifies the armament.
        Assert.NotEqual(belt.Label, Arsenal.Cannon30Mm.DisplayName);
        Assert.False(fit.Describes(ArmamentKind.Belt, belt.Label));

        // A launcher with no belt recognises nothing, rather than matching on a null.
        WeaponFit rail = WeaponFit.Of(Arsenal.SidewinderRail, Arsenal.SeekerHeadAim9);
        Assert.False(rail.Describes(ArmamentKind.Belt, Arsenal.Cannon30Mm.DisplayName));
    }

    /// <summary>
    /// Every launcher has fire control, because fire control is the thing that decides to shoot and
    /// nothing that shoots can lack one.
    ///
    /// <para>It is a declared role rather than a found part, so a launcher that omits it gets no
    /// fire-control row — and every control that lives on that row goes with it: master arm, FIRE,
    /// aim with the mouse, fire at the mouse, protecting the craft being flown, and resetting the
    /// installation. Three of the four launchers shipped without one, so a CIWS could not be armed
    /// from the panel at all.</para>
    ///
    /// <para><c>tools/check-tunables.py</c> cannot catch this and passed throughout: it asks whether
    /// a setting is written <em>somewhere</em> in the panel, and <c>Armed</c> is — on the one row
    /// only a Pantsir has. Reachable for one system is not reachable.</para>
    /// </summary>
    [Fact]
    public void EveryLauncherProvidesFireControl()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            ComponentProfile? component = null;
            for (int i = 0; i < Arsenal.Components.Count; i++)
            {
                if (Arsenal.Components[i].PartId == launcher.PartId
                    && Arsenal.Components[i].Role == WeaponRole.Launcher)
                {
                    component = Arsenal.Components[i];
                }
            }

            Assert.True(component is not null, $"{launcher.DisplayName} has no launcher component");

            bool declares = false;
            foreach (BuiltInComponent provided in component!.Provides)
            {
                if (provided.Role == WeaponRole.FireControl) declares = true;
            }

            Assert.True(declares,
                $"{launcher.DisplayName} declares no fire control, so its panel has no master arm, "
                + "no FIRE, and no mouse aim");
        }
    }

    /// <summary>
    /// Which rounds survive their launcher being destroyed, stated as the one thing that decides
    /// it: whether the steering is aboard the round or back at the shooter.
    ///
    /// <para>Every mode has to be named here, so a new one cannot arrive and be quietly assumed
    /// autonomous — the failure would be a round that goes on steering with nothing behind it,
    /// which looks exactly like one that is working.</para>
    /// </summary>
    [Theory]
    [InlineData(GuidanceMode.Seeker, false)]
    [InlineData(GuidanceMode.AntiRadiation, false)]
    [InlineData(GuidanceMode.CommandLink, true)]
    // Told the point at release and left to it: nothing to uplink, and nothing to lose.
    [InlineData(GuidanceMode.Inertial, false)]
    [InlineData(GuidanceMode.None, false)]
    public void OnlyACommandLinkRoundNeedsItsLauncher(GuidanceMode mode, bool needsUplink)
    {
        MunitionProfile round = new() { Name = "t", DisplayName = "t", Guidance = mode };

        Assert.Equal(needsUplink, round.NeedsUplink);
    }

    /// <summary>And the list above covers the enum, so adding a mode fails here rather than in flight.</summary>
    [Fact]
    public void EveryGuidanceModeIsAccountedFor()
    {
        Assert.Equal(5, Enum.GetValues<GuidanceMode>().Length);
    }
}
