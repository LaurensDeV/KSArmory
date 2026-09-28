namespace KSArmory;

/// <summary>
/// Which falling stores a new designation reaches.
///
/// <para>A store already sent somewhere keeps going there, so marking a second target never
/// swings a whole stick onto it. Three exceptions: a store dropped with nothing marked takes the
/// first mark it is given; the one the weapon released last can be re-aimed as often as the
/// operator likes; and so can the one the chase camera is riding, which is the one they are
/// looking at.</para>
/// </summary>
internal static class StoreRetarget
{
    public static bool Takes(AimpointKind current, bool releasedLast, bool beingChased)
        => current == AimpointKind.None || releasedLast || beingChased;
}
