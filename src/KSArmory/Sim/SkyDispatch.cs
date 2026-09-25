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

    private static readonly float[] Kinds = [Glow, Aurora, Debris];

    /// <summary>
    /// Which of the sky dispatches asked for are drawn, at most <paramref name="most"/>: each kind's
    /// brightest in turn, then each kind's next. Brightness is in each kind's own units, so a plain
    /// sort kept four debris shells and dropped every glow and aurora a bus had lit. Fills
    /// <paramref name="keep"/> with indices into <paramref name="wanted"/>.
    /// </summary>
    public static void Choose(IReadOnlyList<(float Kind, double Brightness)> wanted, int most, List<int> keep)
    {
        keep.Clear();
        if (wanted.Count <= most)
        {
            for (int i = 0; i < wanted.Count; i++) keep.Add(i);
            return;
        }

        List<int> order = [.. Enumerable.Range(0, wanted.Count)
                              .OrderByDescending(i => wanted[i].Brightness)];

        for (int rank = 0; keep.Count < most; rank++)
        {
            bool any = false;
            foreach (float kind in Kinds)
            {
                int seen = 0;
                foreach (int i in order)
                {
                    if (wanted[i].Kind != kind || seen++ != rank) continue;

                    any = true;
                    if (keep.Count < most) keep.Add(i);
                    break;
                }
            }

            if (!any) break;
        }
    }
}
