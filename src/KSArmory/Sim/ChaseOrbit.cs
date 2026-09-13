namespace KSArmory;

/// <summary>
/// The player looking around a chased round: a drag swings the camera round it, the wheel moves it
/// in or out, and letting go eases the view back behind the round.
///
/// <para>Mirrors KSA's own orbit camera — the right button, the same two pixels before a press
/// becomes a drag, the same radians per pixel and the same step per wheel notch — so the chase
/// answers the mouse the way the camera the player already knows does.</para>
///
/// <para>Eased on player time, not simulated: it answers a hand on the mouse, and at a tenth of
/// normal speed a simulated ease would leave the view off the round for ten times as long.</para>
/// </summary>
public sealed class ChaseOrbit
{
    /// <summary>What KSA's orbit camera turns per pixel dragged.</summary>
    public const double RadiansPerPixel = 0.003;

    /// <summary>A press becomes a drag past this squared distance, in pixels — KSA's own threshold.</summary>
    public const double DragStartsPastSquared = 2.0;

    /// <summary>The distance factor per wheel notch, as KSA's orbit camera steps it.</summary>
    public const double ZoomPerNotch = 1.1;

    /// <summary>Short of straight over or under, where a turn about the view's own right runs out of room.</summary>
    public static readonly double MaxPitchRad = double.DegreesToRadians(80.0);

    public const double MinZoom = 0.25;
    public const double MaxZoom = 6.0;

    /// <summary>How long a view is left where the player put it before it starts back.</summary>
    public const double HoldSeconds = 0.4;

    /// <summary>How fast it goes back, per second: about a second to settle behind the round.</summary>
    public const double ReturnRate = 3.0;

    // Below this an angle or a log-zoom is set to exactly zero, so a view that has come back is the
    // chase's own pose to the bit rather than a millionth of a radian off it for ever.
    private const double Settled = 1e-4;

    private bool _pressed;
    private bool _haveCursor;
    private double _x;
    private double _y;
    private double _startX;
    private double _startY;
    private double _dx;
    private double _dy;
    private double _notches;
    private double _logZoom;
    private double _sinceInput = double.PositiveInfinity;

    /// <summary>The turn about the vertical, in radians.</summary>
    public double Yaw { get; private set; }

    /// <summary>The turn about the view's right; positive raises the eye.</summary>
    public double Pitch { get; private set; }

    /// <summary>What the stand-off is multiplied by.</summary>
    public double Zoom => Math.Exp(_logZoom);

    /// <summary>True while the button is down and the cursor has moved far enough to be a drag.</summary>
    public bool Dragging { get; private set; }

    /// <summary>True when there is nothing to apply: the chase's own pose, exactly.</summary>
    public bool IsLevel => Yaw == 0.0 && Pitch == 0.0 && _logZoom == 0.0;

    public void Press()
    {
        _pressed = true;
        Dragging = false;
        _startX = _x;
        _startY = _y;
    }

    public void Release()
    {
        _pressed = false;
        Dragging = false;
    }

    /// <summary>Where the cursor is now, in screen pixels.</summary>
    public void Move(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;

        double dx = _haveCursor ? x - _x : 0.0;
        double dy = _haveCursor ? y - _y : 0.0;

        if (!_haveCursor && _pressed) (_startX, _startY) = (x, y);

        _x = x;
        _y = y;
        _haveCursor = true;

        double fromStartX = x - _startX;
        double fromStartY = y - _startY;
        if (_pressed && !Dragging && (fromStartX * fromStartX) + (fromStartY * fromStartY) > DragStartsPastSquared)
        {
            Dragging = true;
        }

        if (!Dragging) return;

        _dx += dx;
        _dy += dy;
    }

    /// <summary>Wheel notches; positive moves the camera in.</summary>
    public void Scroll(double notches)
    {
        if (double.IsFinite(notches)) _notches += notches;
    }

    /// <summary>One frame: take what the mouse did since the last, then ease back if it has been let go.</summary>
    /// <param name="buttonHeld">
    /// Whether the button is still down. The engine drops a release made with the cursor over a
    /// panel, so a drag has to be able to end from here or it never ends.
    /// </param>
    public void Advance(double seconds, bool buttonHeld)
    {
        if (_pressed && !buttonHeld) Release();

        bool moved = _dx != 0.0 || _dy != 0.0 || _notches != 0.0;

        Yaw = Wrap(Yaw - (_dx * RadiansPerPixel));
        Pitch = Math.Clamp(Pitch + (_dy * RadiansPerPixel), -MaxPitchRad, MaxPitchRad);
        _logZoom = Math.Clamp(_logZoom - (_notches * Math.Log(ZoomPerNotch)), Math.Log(MinZoom), Math.Log(MaxZoom));

        _dx = 0.0;
        _dy = 0.0;
        _notches = 0.0;

        if (moved || Dragging)
        {
            _sinceInput = 0.0;
            return;
        }

        double dt = double.IsFinite(seconds) ? Math.Max(0.0, seconds) : 0.0;
        _sinceInput += dt;
        if (_sinceInput < HoldSeconds) return;

        double keep = Math.Exp(-ReturnRate * dt);
        Yaw = Settle(Yaw * keep);
        Pitch = Settle(Pitch * keep);
        _logZoom = Settle(_logZoom * keep);
    }

    /// <summary>Back behind the round at once, with nothing held, for an engagement that is over.</summary>
    public void Reset()
    {
        Release();
        Yaw = 0.0;
        Pitch = 0.0;
        _logZoom = 0.0;
        _dx = 0.0;
        _dy = 0.0;
        _notches = 0.0;
        _haveCursor = false;
        _sinceInput = double.PositiveInfinity;
    }

    // Into (-pi, pi], so the way back is the short way round.
    private static double Wrap(double angle)
    {
        double wrapped = Math.IEEERemainder(angle, 2.0 * Math.PI);
        return wrapped <= -Math.PI ? wrapped + (2.0 * Math.PI) : wrapped;
    }

    private static double Settle(double value) => Math.Abs(value) < Settled ? 0.0 : value;
}
