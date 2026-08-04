namespace AirDefence;

/// <summary>
/// What to do with one tube's round body this frame.
///
/// <para><b>Note what is missing: there is no value meaning "hide without seating".</b> That is
/// deliberate, and it is the launch-flash bug made unrepresentable. <c>HideMissile</c> writes
/// <c>Scale</c> and nothing else, so a body that was never seated keeps whatever transform it had
/// — and a part with no position written sits at the assembly origin, in the middle of the truck.
/// Skipping the seat for spent tubes parked them there invisibly, which is harmless right up until
/// that tube fires: the placement then writes position and scale together, but the engine has
/// already sampled the cached matrix, so it draws a frame or two at the <em>old</em> transform with
/// the <em>new</em> scale and the round flashes at the centre of the vehicle.</para>
///
/// <para>Both <see cref="Loaded"/> and <see cref="Spent"/> seat the body. Only the visibility
/// differs.</para>
/// </summary>
internal enum TubeVisual
{
    /// <summary>Its round is in the air; the flight path places the body, not the tube.</summary>
    InFlight,

    /// <summary>Still holds a round. Seat it in the tube and show it.</summary>
    Loaded,

    /// <summary>Already fired and not yet reloaded. Seat it, then hide it.</summary>
    Spent,
}

/// <summary>
/// Which tubes hold a round, and which one fires next.
///
/// <para>Authoritative, rather than derived from an ammo count. <c>TubeCount - Ammo</c> was a guess
/// at the next tube and it is only right while a magazine empties monotonically: a reload restarts
/// it at zero while earlier rounds are still in the air, so two rounds end up claiming the same
/// tube — and since a body subpart is chosen by tube number, one body then flips between two
/// rounds every frame while the rest of the salvo appears not to leave at all.</para>
///
/// <para>Lifted out of <c>DefenceBattery</c> because none of it touches KSA: it is a
/// <c>bool[]</c> and two loops, and it was a shipped bug that only in-game observation caught.
/// See <c>docs/MODULARITY.md</c>.</para>
/// </summary>
internal sealed class Magazine
{
    private bool[] _loaded = [];

    /// <summary>Tubes this launcher has.</summary>
    public int Capacity => _loaded.Length;

    /// <summary>
    /// Rounds still in the tubes.
    ///
    /// <para>Counted rather than tracked. It used to be a field maintained alongside the tube
    /// flags, which is two representations of one fact and an invitation for them to disagree —
    /// and the bug this class exists to prevent was exactly a disagreement between them.</para>
    /// </summary>
    public int Ammo
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _loaded.Length; i++) if (_loaded[i]) n++;
            return n;
        }
    }

    /// <summary>Tubes already fired and not yet reloaded.</summary>
    public int SpentCount => Capacity - Ammo;

    public bool IsEmpty => Ammo == 0;

    /// <summary>Sizes the magazine to a launcher and fills it. Safe to call with the same size.</summary>
    public void Resize(int tubeCount)
    {
        if (tubeCount < 0) tubeCount = 0;
        if (_loaded.Length != tubeCount) _loaded = new bool[tubeCount];
        RefillAll();
    }

    /// <summary>Puts a round back in every tube.</summary>
    public void RefillAll() => Array.Fill(_loaded, true);

    /// <summary>Empties every tube. For teardown, not for firing.</summary>
    public void Clear() => Array.Fill(_loaded, false);

    public bool IsLoaded(int tubeIndex)
        => tubeIndex >= 0 && tubeIndex < _loaded.Length && _loaded[tubeIndex];

    /// <summary>
    /// The lowest tube that is both loaded and not already occupied by a round in the air, and
    /// takes its round. False when there is none.
    /// </summary>
    /// <param name="inFlight">
    /// Rounds currently flying. A reload refills a tube whose previous round has not landed yet;
    /// firing it again would hand two rounds the same body.
    /// </param>
    public bool TryTakeTube(IReadOnlyList<IProjectile> inFlight, out int tubeIndex)
    {
        for (int i = 0; i < _loaded.Length; i++)
        {
            if (!_loaded[i]) continue;
            if (IsOccupied(inFlight, i)) continue;

            _loaded[i] = false;
            tubeIndex = i;
            return true;
        }

        tubeIndex = -1;
        return false;
    }

    /// <summary>Whether a round in the air already claims this tube. Rounds number tubes from one.</summary>
    public static bool IsOccupied(IReadOnlyList<IProjectile> inFlight, int tubeIndex)
    {
        for (int r = 0; r < inFlight.Count; r++)
        {
            if (inFlight[r].Tube == tubeIndex + 1) return true;
        }
        return false;
    }

    /// <summary>
    /// What to do with one tube's body this frame.
    ///
    /// <para>Spent tubes are the first <see cref="SpentCount"/>, matching the order
    /// <see cref="TryTakeTube"/> hands them out.</para>
    /// </summary>
    public TubeVisual Plan(int tubeIndex, bool inFlight) => Plan(tubeIndex, inFlight, SpentCount);

    /// <inheritdoc cref="Plan(int, bool)"/>
    public static TubeVisual Plan(int tubeIndex, bool inFlight, int spentCount)
        => inFlight ? TubeVisual.InFlight
         : tubeIndex < spentCount ? TubeVisual.Spent
         : TubeVisual.Loaded;

    /// <summary>
    /// Whether this plan requires the body to be seated in its tube first.
    ///
    /// <para>True for everything the tube is responsible for placing — which is both
    /// <see cref="TubeVisual.Loaded"/> and <see cref="TubeVisual.Spent"/>. See
    /// <see cref="TubeVisual"/> for why the spent case is not an optimisation opportunity.</para>
    /// </summary>
    public static bool RequiresSeating(TubeVisual plan) => plan != TubeVisual.InFlight;

    /// <summary>Whether the body is visible once seated.</summary>
    public static bool IsVisible(TubeVisual plan) => plan == TubeVisual.Loaded;
}
