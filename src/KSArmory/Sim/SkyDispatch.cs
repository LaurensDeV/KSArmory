namespace KSArmory;

/// <summary>
/// The kinds of full-screen dispatch the cloud pass sends for what a burst lights in the sky, named.
///
/// <para>Every one is told apart from a cloud by a <b>negative bound</b> (<c>CentreRadius.w</c>, which
/// carries its brightness) and from each other by <c>FireSun.z</c>. The shader tests the kinds from the
/// highest down, so each threshold sits between one kind and the next.</para>
///
/// <para><b>What each puts in <c>FireSun.w</c></b> is its own, and the scorch-mark branch, which
/// offsets every pixel of a dispatch whose <c>FireSun.w</c> is positive, skips any dispatch with a
/// negative bound: an aurora's is the sine of its foot's latitude, positive in the north, and before that
/// guard every northern curtain was drawn off the screen.</para>
/// </summary>
internal static class SkyDispatch
{
    /// <summary>The X-ray-heated layer (<see cref="XRayGlow"/>). <c>FireSun.w</c> is 0.</summary>
    public const float Glow = 0f;

    /// <summary>One end of a burst's field line (<see cref="Aurora"/>). <c>FireSun.w</c> is the sine of the foot's magnetic latitude.</summary>
    public const float Aurora = 1f;

    /// <summary>A thin-air burst's debris shell (<see cref="DebrisShell"/>). <c>FireSun.w</c> is 0.</summary>
    public const float Debris = 2f;

    /// <summary>
    /// The red wave a burst high in the air sends out (<see cref="RedWave"/>). <c>FireSun.w</c> is the
    /// altitude the red survives above.
    /// </summary>
    public const float RedWave = 3f;

    /// <summary>
    /// How many of one kind a frame draws: as many as it pays for. A glow, a shell and a wave cost
    /// 0.04-0.15 ms from orbit. Curtains are the dear kind, and four -- both ends of two bursts' field
    /// lines -- cost 2.2 ms from orbit, what two did, since one off the screen costs next to nothing.
    /// </summary>
    public static int MostOf(float kind) => kind == Aurora ? 4 : kind == RedWave ? 2 : 3;

    /// <summary>
    /// Which of the sky dispatches asked for are drawn: of each kind, up to <see cref="MostOf"/>, the
    /// NEWEST first -- the order they were asked for in, which is the order the bursts went off in.
    /// Newest rather than brightest, because brightness moves: two bursts' curtains cross as one fades
    /// and the other arrives, and ranked by it the one drawn jumped between them from frame to frame.
    /// Fills <paramref name="keep"/> with indices into <paramref name="kinds"/>, in order.
    /// </summary>
    public static void Choose(IReadOnlyList<float> kinds, List<int> keep)
    {
        keep.Clear();
        for (int i = 0; i < kinds.Count; i++)
        {
            int newer = 0;
            for (int j = i + 1; j < kinds.Count; j++) if (kinds[j] == kinds[i]) newer++;
            if (newer < MostOf(kinds[i])) keep.Add(i);
        }
    }
}
