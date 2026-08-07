namespace KSArmory;

/// <summary>
/// What to do with one tube's round body this frame.
///
/// <para><b>There is no value meaning "hide without seating".</b> <c>HideMissile</c> writes
/// <c>Scale</c> and nothing else, so an unseated body keeps an unwritten transform and sits at the
/// assembly origin. It stays invisible there until its tube fires, at which point the engine has
/// already sampled the cached matrix and draws a frame at the old transform with the new scale.
/// Both <see cref="Loaded"/> and <see cref="Spent"/> therefore seat the body; only visibility
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
/// <para>Authoritative rather than derived from an ammo count. <c>TubeCount - Ammo</c> only names
/// the next tube while a magazine empties monotonically: a reload restarts it at zero while earlier
/// rounds are still in the air, so two rounds claim one tube — and a body subpart is chosen by tube
/// number, so one body would flip between them every frame.</para>
/// </summary>
internal sealed class Magazine
{
    private bool[] _loaded = [];

    /// <summary>Firing positions this launcher has.</summary>
    public int TubeCount => _loaded.Length;

    /// <summary>
    /// Rounds carried in total, which need not equal <see cref="TubeCount"/>.
    ///
    /// <para>A tube is a place to fire from; the magazine is how much there is to fire. A missile
    /// launcher has one round per tube, and a belt-fed gun has one barrel and a thousand rounds.
    /// Zero means the two are the same.</para>
    /// </summary>
    public int Depth { get; private set; }

    /// <summary>Firing positions this launcher has. Kept for callers that mean tubes.</summary>
    public int Capacity => _loaded.Length;

    /// <summary>
    /// Rounds still in the tubes, counted from the flags rather than tracked beside them. Two
    /// representations of one fact can disagree; one cannot.
    /// </summary>
    public int Ammo
    {
        get
        {
            if (_reserve >= 0) return _reserve;

            int n = 0;
            for (int i = 0; i < _loaded.Length; i++) if (_loaded[i]) n++;
            return n;
        }
    }

    // Negative when the magazine is one-round-per-tube, in which case the tube flags are the
    // count. Non-negative when it holds more rounds than tubes and has to track them separately.
    private int _reserve = -1;

    /// <summary>Tubes already fired and not yet reloaded. Always zero for a deep magazine.</summary>
    public int SpentCount => Depth > 0 ? 0 : Capacity - Ammo;

    public bool IsEmpty => Ammo == 0;

    /// <summary>
    /// Sizes the magazine to a launcher and fills it. Safe to call with the same size.
    /// </summary>
    /// <param name="depth">
    /// Rounds carried. Zero or anything at or below <paramref name="tubeCount"/> means one round
    /// per tube, which is the missile-launcher case and how this behaves without it.
    /// </param>
    public void Resize(int tubeCount, int depth = 0)
    {
        if (tubeCount < 0) tubeCount = 0;
        if (_loaded.Length != tubeCount) _loaded = new bool[tubeCount];

        Depth = depth > tubeCount ? depth : 0;
        _reserve = Depth > 0 ? Depth : -1;
        Array.Fill(_loaded, true);
    }

    /// <summary>
    /// Puts back a round that <see cref="TryTakeTube"/> handed out but that was never fired.
    ///
    /// <para>Needed because a shot cannot be fully judged until it has a tube: the launch geometry
    /// is per tube, and a seeker round is refused on the angle between where that tube points and
    /// where it was sent. Without this a refused shot still costs a round, which on a single-rail
    /// launcher means one misjudged click empties it.</para>
    /// </summary>
    public void Return(int tubeIndex)
    {
        if (Depth > 0)
        {
            if (_reserve < Depth) _reserve++;
            return;
        }

        if (tubeIndex >= 0 && tubeIndex < _loaded.Length) _loaded[tubeIndex] = true;
    }

    /// <summary>Puts a round back in every tube, and refills the reserve if there is one.</summary>
    public void RefillAll()
    {
        Array.Fill(_loaded, true);
        if (Depth > 0) _reserve = Depth;
    }

    /// <summary>Empties every tube. For teardown, not for firing.</summary>
    public void Clear()
    {
        Array.Fill(_loaded, false);
        if (Depth > 0) _reserve = 0;
    }

    public bool IsLoaded(int tubeIndex)
    {
        if (tubeIndex < 0 || tubeIndex >= _loaded.Length) return false;

        // A deep magazine keeps every tube loaded while it has rounds left: the tube is a barrel,
        // not a container, so it is full whenever the belt is.
        return Depth > 0 ? _reserve > 0 : _loaded[tubeIndex];
    }

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
        tubeIndex = -1;

        // A deep magazine cycles rounds through its tubes rather than emptying them, so a tube is
        // reusable the moment its previous round is clear. Occupancy still applies: a body subpart
        // is chosen by tube number, so two rounds on one tube would share a body.
        if (Depth > 0)
        {
            if (_reserve <= 0) return false;

            for (int i = 0; i < _loaded.Length; i++)
            {
                if (IsOccupied(inFlight, i)) continue;

                _reserve--;
                tubeIndex = i;
                return true;
            }
            return false;
        }

        for (int i = 0; i < _loaded.Length; i++)
        {
            if (!_loaded[i]) continue;
            if (IsOccupied(inFlight, i)) continue;

            _loaded[i] = false;
            tubeIndex = i;
            return true;
        }

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
    public TubeVisual Plan(int tubeIndex, bool inFlight)
        => Depth > 0
            ? (inFlight ? TubeVisual.InFlight : _reserve > 0 ? TubeVisual.Loaded : TubeVisual.Spent)
            : Plan(tubeIndex, inFlight, SpentCount);

    /// <inheritdoc cref="Plan(int, bool)"/>
    public static TubeVisual Plan(int tubeIndex, bool inFlight, int spentCount)
        => inFlight ? TubeVisual.InFlight
         : tubeIndex < spentCount ? TubeVisual.Spent
         : TubeVisual.Loaded;

    /// <summary>
    /// Whether this plan requires the body to be seated first. True for everything the tube places,
    /// which is both <see cref="TubeVisual.Loaded"/> and <see cref="TubeVisual.Spent"/> — see
    /// <see cref="TubeVisual"/> for why the spent case is not an optimisation opportunity.
    /// </summary>
    public static bool RequiresSeating(TubeVisual plan) => plan != TubeVisual.InFlight;

    /// <summary>Whether the body is visible once seated.</summary>
    public static bool IsVisible(TubeVisual plan) => plan == TubeVisual.Loaded;
}
