using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>One search set fitted to a craft, scanning on its own.</summary>
internal sealed class SearchSet
{
    /// <summary>Its side is its craft's, declared by whatever else aboard carries a flag.</summary>
    internal sealed class Policy : ISensorPolicy
    {
        public IffPolicy Iff { get; } = new();

        public bool ProtectControlledVehicle => false;
    }

    public SearchSet(Config config, Part part, SearchSetProfile profile)
    {
        Part = part;
        Profile = profile;
        Radar = new Radar(config, SensorPolicy);
    }

    public Part Part { get; }

    public SearchSetProfile Profile { get; }

    public Policy SensorPolicy { get; } = new();

    public Radar Radar { get; }

    /// <summary>The craft carrying the part this frame, which a split changes.</summary>
    public Vehicle? Craft { get; set; }

    public SensorProfile Sensor => Catalogue.SensorNamed(Profile.Sensor);

    public double3 Boresight { get; private set; } = new(0, 0, 1);

    public void SampleWorld()
    {
        if (Craft is not { } craft) return;

        SensorProfile sensor = Sensor;
        double3 axis = sensor.BoresightSource switch
        {
            BoresightMode.PartForward => new double3(0, 1, 0),
            BoresightMode.LocalUp => Vec.Zero,
            _ => new double3(1, 0, 0),
        };

        Boresight = Vec.Len2(axis) > 0.0 && LauncherPart.TryLauncherDirectionEcl(craft, Part, axis, out double3 ecl)
                        ? ecl
                        : KsaWorld.LocalUp(craft);
    }

    public void Update(double dt, IReadOnlyList<IContact>? airborne)
    {
        if (Craft is not { } craft || !KsaWorld.IsAlive(craft)) return;

        Radar.Sensor = Sensor;
        Radar.Scan(craft, Boresight, dt, airborne);
    }
}

/// <summary>
/// One <see cref="SearchSet"/> per search part fitted. Keyed on the part itself, so a set a
/// decoupler carries onto another craft is the same set on the next frame: it holds no operator
/// settings and no magazine, so nothing needs handing over and a part not found is simply dropped.
/// </summary>
internal sealed class SearchSets(Config config)
{
    private readonly Config _config = config;

    private readonly Dictionary<Part, SearchSet> _sets = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Part> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly List<Part> _gone = [];

    public IEnumerable<SearchSet> All => _sets.Values;

    public int Count => _sets.Count;

    /// <summary>The sets on one craft, appended in no particular order.</summary>
    public void On(Vehicle? craft, List<SearchSet> into)
    {
        into.Clear();
        if (craft is null) return;

        foreach (SearchSet s in _sets.Values)
        {
            if (ReferenceEquals(s.Craft, craft)) into.Add(s);
        }
    }

    public void Sync(IReadOnlyList<Vehicle> craft)
    {
        if (Arsenal.SearchSets.Count == 0)
        {
            _sets.Clear();
            return;
        }

        _seen.Clear();

        for (int i = 0; i < craft.Count; i++)
        {
            Vehicle v = craft[i];

            // A set lying on the ground as a craft of its own is debris, as a pod is.
            if (!KsaWorld.IsAlive(v) || !KsaWorld.HasPlatform(v)) continue;

            try
            {
                ReadOnlySpan<Part> parts = v.Parts.Parts;
                for (int p = 0; p < parts.Length; p++)
                {
                    if (parts[p] is not { } part || Catalogue.SearchSetForPart(part.Id) is not { } profile) continue;

                    _seen.Add(part);

                    if (!_sets.TryGetValue(part, out SearchSet? set))
                    {
                        set = new SearchSet(_config, part, profile);
                        _sets[part] = set;
                        Log.Info($"crewed {profile.DisplayName} on {KsaWorld.DisplayName(v)}");
                    }

                    set.Craft = v;
                }
            }
            catch
            {
                // Part tree mid-rebuild: keep what was crewed and look again next frame.
                foreach (SearchSet s in _sets.Values)
                {
                    if (ReferenceEquals(s.Craft, v)) _seen.Add(s.Part);
                }
            }
        }

        _gone.Clear();
        foreach (Part part in _sets.Keys)
        {
            if (!_seen.Contains(part)) _gone.Add(part);
        }

        for (int i = 0; i < _gone.Count; i++) _sets.Remove(_gone[i]);
    }

    /// <summary>Each set takes its craft's declared side and that side's rules.</summary>
    public void AdoptTeams()
    {
        foreach (SearchSet s in _sets.Values)
        {
            string? team = KsaWorld.TeamRoster.For(s.Craft);
            s.SensorPolicy.Iff.OwnTeam = team;
            _config.RulesFor(team).ApplyTo(s.SensorPolicy.Iff);
        }
    }

    public void SampleWorld()
    {
        foreach (SearchSet s in _sets.Values) s.SampleWorld();
    }

    public void Update(double dt, IReadOnlyList<IContact>? airborne)
    {
        foreach (SearchSet s in _sets.Values) s.Update(dt, airborne);
    }

    public void Clear() => _sets.Clear();
}
