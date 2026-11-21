#!/usr/bin/env python3
"""Reference tables for Sim/Lambert.cs and Sim/Kepler.cs, from solvers that share none of their code.

    ./tests/KSArmory.Tests/Reference/make_reference.py           # rewrite lambert.csv and kepler.csv
    ./tests/KSArmory.Tests/Reference/make_reference.py --check   # fail if either would change

Lambert's problem is solved twice -- Izzo's Householder iteration on x (2015), and Lagrange's
time equation bisected on the reciprocal semi-major axis -- and a case is written only when the
two agree and a third, separate propagator flies the answer onto the target. Kepler's problem is
solved by the difference in eccentric (or hyperbolic) anomaly, and checked by RK4 stepped in
time scaled by r^1.5. Standard library only, and seeded, so the tables are reproducible.

Cases outside what the C# claims are not written: exactly half a turn and multi-revolution
transfers, and Lambert cases whose universal variable psi lies outside Lambert.cs's bracket
of +-4 pi^2.
"""

import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
MU_EARTH = 3.986004418e14
MU_MOON = 4.9048695e12
R_EARTH = 6_371_000.0
TWO_PI = 2.0 * math.pi
PSI_BOUND = 4.0 * math.pi * math.pi


def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul(a, k): return (a[0] * k, a[1] * k, a[2] * k)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def norm(a): return math.sqrt(dot(a, a))
def unit(a): return mul(a, 1.0 / norm(a))


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def x_minus_sin(x):
    """x - sin x, by series where the closed form cancels."""
    if abs(x) > 0.5:
        return x - math.sin(x)
    term, total, k = x ** 3 / 6.0, 0.0, 0
    while abs(term) > 1e-300 and abs(term) > abs(total) * 1e-18:
        total += term
        term *= -x * x / ((2 * k + 4) * (2 * k + 5))
        k += 1
    return total


def sinh_minus_x(x):
    if abs(x) > 0.5:
        return math.sinh(x) - x
    term, total, k = x ** 3 / 6.0, 0.0, 0
    while abs(term) > 1e-300 and abs(term) > abs(total) * 1e-18:
        total += term
        term *= x * x / ((2 * k + 4) * (2 * k + 5))
        k += 1
    return total


def geometry(r1v, r2v, long_way):
    r1, r2 = norm(r1v), norm(r2v)
    theta = math.atan2(norm(cross(r1v, r2v)), dot(r1v, r2v))
    if long_way:
        theta = TWO_PI - theta
    c = norm(sub(r2v, r1v))
    s = 0.5 * (r1 + r2 + c)
    return r1, r2, theta, c, s


# --- Izzo, "Revisiting Lambert's problem", Celest. Mech. Dyn. Astr. 121 (2015), single revolution.

def izzo_hypergeometric(z):
    sj, cj, j = 1.0, 1.0, 0
    while True:
        cj = cj * (3.0 + j) * (1.0 + j) / (2.5 + j) * z / (j + 1.0)
        sj += cj
        j += 1
        if abs(cj) < 1e-16:
            return sj


def izzo_tof(x, lam):
    """Non-dimensional time of flight T(x)."""
    dist = abs(x - 1.0)
    if 0.01 < dist < 0.2:
        a = 1.0 / (1.0 - x * x)
        if a > 0:
            alfa = 2.0 * math.acos(x)
            beta = 2.0 * math.asin(math.sqrt(lam * lam / a))
            if lam < 0:
                beta = -beta
            return a * math.sqrt(a) * ((alfa - math.sin(alfa)) - (beta - math.sin(beta))) / 2.0
        alfa = 2.0 * math.acosh(x)
        beta = 2.0 * math.asinh(math.sqrt(-lam * lam / a))
        if lam < 0:
            beta = -beta
        return -a * math.sqrt(-a) * ((beta - math.sinh(beta)) - (alfa - math.sinh(alfa))) / 2.0
    k = lam * lam
    e = x * x - 1.0
    z = math.sqrt(1.0 + k * e)
    if dist < 0.01:
        eta = z - lam * x
        s1 = 0.5 * (1.0 - lam - x * eta)
        q = 4.0 / 3.0 * izzo_hypergeometric(s1)
        return (eta ** 3 * q + 4.0 * lam * eta) / 2.0
    y = math.sqrt(abs(e))
    g = x * z - lam * e
    d = math.acos(g) if e < 0 else math.log(y * (z - lam * x) + g)
    return (x - lam * z - d / y) / e


def izzo_derivatives(x, t, lam):
    umx2 = 1.0 - x * x
    y = math.sqrt(1.0 - lam * lam * umx2)
    l2, l3 = lam * lam, lam ** 3
    dt = (3.0 * t * x - 2.0 + 2.0 * l3 * x / y) / umx2
    ddt = (3.0 * t + 5.0 * x * dt + 2.0 * (1.0 - l2) * l3 / y ** 3) / umx2
    dddt = (7.0 * x * ddt + 8.0 * dt - 6.0 * (1.0 - l2) * lam ** 5 * x / y ** 5) / umx2
    return dt, ddt, dddt


def izzo(r1v, r2v, tof, mu, long_way):
    r1, r2, _, c, s = geometry(r1v, r2v, long_way)
    lam = math.sqrt(max(0.0, 1.0 - c / s))
    ir1, ir2 = unit(r1v), unit(r2v)
    ih = unit(cross(ir1, ir2))
    it1, it2 = cross(ih, ir1), cross(ih, ir2)
    if long_way:
        lam, it1, it2 = -lam, mul(it1, -1.0), mul(it2, -1.0)
    big_t = math.sqrt(2.0 * mu / s ** 3) * tof

    t00 = math.acos(lam) + lam * math.sqrt(1.0 - lam * lam)
    t1 = 2.0 / 3.0 * (1.0 - lam ** 3)
    if big_t >= t00:
        x = (t00 / big_t) ** (2.0 / 3.0) - 1.0
    elif big_t < t1:
        x = 2.5 * t1 / big_t * (t1 - big_t) / (1.0 - lam ** 5) + 1.0
    else:
        x = (big_t / t00) ** (math.log(2.0) / math.log(t1 / t00)) - 1.0

    for _ in range(50):
        f = izzo_tof(x, lam) - big_t
        d1, d2, d3 = izzo_derivatives(x, f + big_t, lam)
        step = f * (d1 * d1 - f * d2 / 2.0) / (d1 * (d1 * d1 - f * d2) + d3 * f * f / 6.0)
        x -= step
        if abs(step) < 1e-14:
            break
    else:
        raise RuntimeError("Izzo did not converge")

    gamma = math.sqrt(mu * s / 2.0)
    rho = (r1 - r2) / c
    sigma = math.sqrt(1.0 - rho * rho)
    y = math.sqrt(1.0 - lam * lam + lam * lam * x * x)
    vr1 = gamma * ((lam * y - x) - rho * (lam * y + x)) / r1
    vr2 = -gamma * ((lam * y - x) + rho * (lam * y + x)) / r2
    vt = gamma * sigma * (y + lam * x)
    v1 = add(mul(ir1, vr1), mul(it1, vt / r1))
    v2 = add(mul(ir2, vr2), mul(it2, vt / r2))
    return v1, v2, x


# --- Lagrange's time equation in alpha = 1/a, bisected; Prussing & Conway, Orbital Mechanics, ch. 4.

def lagrange_angles(alpha, s, c, theta):
    """The two Lagrange angles, signed for the transfer angle; hyperbolic ones for alpha < 0."""
    if alpha > 0:
        a_ang = 2.0 * math.asin(min(1.0, math.sqrt(s * alpha / 2.0)))
        b_ang = 2.0 * math.asin(min(1.0, math.sqrt((s - c) * alpha / 2.0)))
    else:
        a_ang = 2.0 * math.asinh(math.sqrt(-s * alpha / 2.0))
        b_ang = 2.0 * math.asinh(math.sqrt(-(s - c) * alpha / 2.0))
    if theta > math.pi:
        b_ang = -b_ang
    return a_ang, b_ang


def lagrange_time(q, s, c, theta, mu):
    """Flight time on a parameter that rises with it: alpha < 0, then 0..2/s, then the long branch."""
    am = 2.0 / s
    if q <= 0:
        alpha = q
        a_ang, b_ang = lagrange_angles(alpha, s, c, theta)
        if alpha == 0:
            return parabolic_time(s, c, theta, mu)
        return (sinh_minus_x(a_ang) - sinh_minus_x(b_ang)) / ((-alpha) ** 1.5 * math.sqrt(mu))
    if q <= am:
        alpha, long_branch = q, False
    else:
        alpha, long_branch = 2.0 * am - q, True
    a_ang, b_ang = lagrange_angles(alpha, s, c, theta)
    if long_branch:
        # 2 pi - A, and x - sin x of it is 2 pi - (A - sin A).
        first = TWO_PI - x_minus_sin(a_ang)
    else:
        first = x_minus_sin(a_ang)
    return (first - x_minus_sin(b_ang)) / (alpha ** 1.5 * math.sqrt(mu))


def parabolic_time(s, c, theta, mu):
    sign = 1.0 if theta <= math.pi else -1.0
    return math.sqrt(2.0) / 3.0 * (s ** 1.5 - sign * (s - c) ** 1.5) / math.sqrt(mu)


def lagrange(r1v, r2v, tof, mu, long_way):
    r1, r2, theta, c, s = geometry(r1v, r2v, long_way)
    am = 2.0 / s
    lo, hi = -am, 2.0 * am
    while lagrange_time(lo, s, c, theta, mu) > tof:
        lo *= 2.0
    for _ in range(400):
        mid = 0.5 * (lo + hi)
        if mid == lo or mid == hi:
            break
        if lagrange_time(mid, s, c, theta, mu) < tof:
            lo = mid
        else:
            hi = mid
    q = 0.5 * (lo + hi)
    long_branch = q > am
    alpha = 2.0 * am - q if long_branch else q
    a_ang, b_ang = lagrange_angles(alpha, s, c, theta)

    # Semi-latus rectum, written as sin^2/alpha so it stays finite through the parabola.
    k = 4.0 * (s - r1) * (s - r2) / (c * c)
    if alpha > 0:
        half = 0.5 * ((TWO_PI - a_ang if long_branch else a_ang) + b_ang)
        p = k * math.sin(half) ** 2 / alpha
        d_anomaly = (TWO_PI - a_ang if long_branch else a_ang) - b_ang
    elif alpha < 0:
        half = 0.5 * (a_ang + b_ang)
        p = k * math.sinh(half) ** 2 / -alpha
        d_anomaly = -(a_ang - b_ang)
    else:
        raise RuntimeError("exactly parabolic")

    one_minus_cos = 2.0 * math.sin(theta / 2.0) ** 2
    f = 1.0 - r2 / p * one_minus_cos
    g = r1 * r2 * math.sin(theta) / math.sqrt(mu * p)
    g_dot = 1.0 - r1 / p * one_minus_cos
    v1 = mul(sub(r2v, mul(r1v, f)), 1.0 / g)
    v2 = mul(sub(mul(r2v, g_dot), r1v), 1.0 / g)
    psi = d_anomaly * abs(d_anomaly)
    return v1, v2, psi


# --- Kepler by the change in anomaly, and RK4 to check it.

def solve_increasing(fn, guess):
    """Root of a function rising everywhere: bracketed, then Newton held inside the bracket."""
    lo, hi = guess - 1.0, guess + 1.0
    while fn(lo)[0] > 0:
        lo -= 2.0 * (hi - lo)
    while fn(hi)[0] < 0:
        hi += 2.0 * (hi - lo)
    x = min(max(guess, lo), hi)
    for _ in range(300):
        val, der = fn(x)
        if val == 0:
            return x
        if val < 0:
            lo = x
        else:
            hi = x
        nxt = x - val / der
        if not lo < nxt < hi:
            nxt = 0.5 * (lo + hi)
        if abs(nxt - x) <= 1e-15 * max(1.0, abs(x)) or hi - lo <= 1e-15 * max(1.0, abs(x)):
            return nxt
        x = nxt
    raise RuntimeError("Kepler's equation did not converge")


def kepler_anomaly(mu, r0v, v0v, t):
    r0 = norm(r0v)
    sigma0 = dot(r0v, v0v) / math.sqrt(mu)
    alpha = 2.0 / r0 - dot(v0v, v0v) / mu
    a = 1.0 / alpha
    if alpha > 0:
        n_t = math.sqrt(mu / a ** 3) * t
        k = sigma0 / math.sqrt(a)

        def fn(de):
            return (de - (1.0 - r0 / a) * math.sin(de) + k * (1.0 - math.cos(de)) - n_t,
                    1.0 - (1.0 - r0 / a) * math.cos(de) + k * math.sin(de))

        de = solve_increasing(fn, n_t)
        f = 1.0 - a / r0 * (1.0 - math.cos(de))
        g = t - math.sqrt(a ** 3 / mu) * x_minus_sin(de)
        rv = add(mul(r0v, f), mul(v0v, g))
        r = norm(rv)
        f_dot = -math.sqrt(mu * a) / (r * r0) * math.sin(de)
        g_dot = 1.0 - a / r * (1.0 - math.cos(de))
    else:
        n_t = math.sqrt(mu / (-a) ** 3) * t
        k = sigma0 / math.sqrt(-a)

        def fn(dh):
            return (-dh + (1.0 - r0 / a) * math.sinh(dh) + k * (math.cosh(dh) - 1.0) - n_t,
                    -1.0 + (1.0 - r0 / a) * math.cosh(dh) + k * math.sinh(dh))

        dh = solve_increasing(fn, 0.0)
        f = 1.0 - a / r0 * (1.0 - math.cosh(dh))
        g = t - math.sqrt((-a) ** 3 / mu) * sinh_minus_x(dh)
        rv = add(mul(r0v, f), mul(v0v, g))
        r = norm(rv)
        f_dot = -math.sqrt(-mu * a) / (r * r0) * math.sinh(dh)
        g_dot = 1.0 - a / r * (1.0 - math.cosh(dh))
    return rv, add(mul(r0v, f_dot), mul(v0v, g_dot))


def rk4(mu, r0v, v0v, t, k=4e-4):
    def accel(r):
        return mul(r, -mu / norm(r) ** 3)

    r, v, elapsed = r0v, v0v, 0.0
    while elapsed < t:
        h = min(k * norm(r) ** 1.5 / math.sqrt(mu), t - elapsed)
        k1r, k1v = v, accel(r)
        k2r, k2v = add(v, mul(k1v, h / 2)), accel(add(r, mul(k1r, h / 2)))
        k3r, k3v = add(v, mul(k2v, h / 2)), accel(add(r, mul(k2r, h / 2)))
        k4r, k4v = add(v, mul(k3v, h)), accel(add(r, mul(k3r, h)))
        r = add(r, mul(add(add(k1r, mul(k2r, 2)), add(mul(k3r, 2), k4r)), h / 6))
        v = add(v, mul(add(add(k1v, mul(k2v, 2)), add(mul(k3v, 2), k4v)), h / 6))
        elapsed += h
    return r, v


# --- Cases.

def random_direction(rng):
    while True:
        p = (rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))
        if 0.1 < norm(p) <= 1.0:
            return unit(p)


def rotate_towards(a, b, angle):
    """a turned by angle in the plane containing b."""
    perp = unit(sub(b, mul(a, dot(a, b))))
    return add(mul(a, math.cos(angle)), mul(perp, math.sin(angle)))


def lambert_cases(rng):
    """(mu, r1, r2, x, long_way, label): x is Izzo's variable, which fixes the conic."""
    out = []
    shapes = [("elliptic", -0.9, 0.85), ("near-parabolic", 0.97, 1.03), ("hyperbolic", 1.2, 3.0)]
    angles = [1.0, 2.0, 15.0, 45.0, 90.0, 135.0, 170.0, 178.0, 179.0]
    for deg in angles:
        for long_way in (False, True):
            for label, xlo, xhi in shapes:
                for scale in ("icbm", "leo-geo"):
                    if scale == "icbm":
                        mu = MU_EARTH
                        r1 = R_EARTH + rng.uniform(0, 1.2e5)
                        r2 = R_EARTH + rng.uniform(0, 2e4)
                    else:
                        mu = MU_EARTH if rng.random() < 0.8 else MU_MOON
                        base = R_EARTH if mu == MU_EARTH else 1.7374e6
                        r1 = base + rng.uniform(2e5, 2e6)
                        r2 = base * rng.uniform(1.05, 6.6)
                    u1 = random_direction(rng)
                    u2 = rotate_towards(u1, random_direction(rng), math.radians(deg))
                    out.append((mu, mul(u1, r1), mul(u2, r2), rng.uniform(xlo, xhi), long_way,
                                f"{label} {deg:g}deg {'long' if long_way else 'short'} {scale}"))
    return out


def tof_from_x(r1v, r2v, x, mu, long_way):
    _, _, _, c, s = geometry(r1v, r2v, long_way)
    lam = math.sqrt(max(0.0, 1.0 - c / s))
    if long_way:
        lam = -lam
    return izzo_tof(x, lam) / math.sqrt(2.0 * mu / s ** 3)


def build_lambert(rng):
    rows, worst = [], (0.0, 0.0)
    skipped = 0
    for mu, r1v, r2v, x, long_way, label in lambert_cases(rng):
        tof = tof_from_x(r1v, r2v, x, mu, long_way)
        v1, v2, _ = izzo(r1v, r2v, tof, mu, long_way)
        w1, w2, psi = lagrange(r1v, r2v, tof, mu, long_way)
        if abs(psi) > 0.95 * PSI_BOUND:
            skipped += 1
            continue
        dv = max(norm(sub(v1, w1)), norm(sub(v2, w2)))
        flown, flown_v = kepler_anomaly(mu, r1v, v1, tof)
        dr = norm(sub(flown, r2v))
        dv = max(dv, norm(sub(flown_v, v2)))
        if dv > 1e-6 or dr > 1e-5 * max(1.0, norm(r2v) * 1e-6):
            raise RuntimeError(f"{label}: the two solvers disagree by {dv:.3g} m/s, {dr:.3g} m")
        worst = (max(worst[0], dv), max(worst[1], dr))
        rows.append((label, mu, *r1v, *r2v, tof, int(long_way), *v1, *v2, psi))
    return rows, worst, skipped


def kepler_cases(rng):
    out = []
    eccentricities = [0.0, 0.0, 0.05, 0.2, 0.4, 0.6, 0.8, 0.9, 0.95, 0.99, 0.999, 1.001, 1.01, 1.05, 1.2, 1.5]
    while len(out) < 100:
        e = eccentricities[len(out) % len(eccentricities)]
        mu = MU_EARTH if rng.random() < 0.85 else MU_MOON
        rp = (R_EARTH if mu == MU_EARTH else 1.7374e6) + rng.uniform(1e5, 3e6)
        p = rp * (1.0 + e)
        nu = rng.uniform(-math.pi, math.pi)
        if e >= 1.0:
            limit = math.acos(-1.0 / e) * 0.8
            nu = rng.uniform(-limit, limit)
        r = p / (1.0 + e * math.cos(nu))
        h = math.sqrt(mu * p)
        p_hat = random_direction(rng)
        w_hat = unit(cross(p_hat, random_direction(rng)))
        q_hat = cross(w_hat, p_hat)
        r0v = add(mul(p_hat, r * math.cos(nu)), mul(q_hat, r * math.sin(nu)))
        v0v = add(mul(p_hat, -mu / h * math.sin(nu)), mul(q_hat, mu / h * (e + math.cos(nu))))
        n = math.sqrt(mu / (rp / abs(1.0 - e)) ** 3) if e != 1.0 else math.sqrt(mu / rp ** 3)
        span = min(TWO_PI / n, 4 * 3600.0) if e < 1.0 else 3 * 3600.0
        t = rng.uniform(0.02, 1.0) * span
        out.append((e, mu, r0v, v0v, t))
    return out


def build_kepler(rng):
    rows, worst = [], (0.0, 0.0)
    for e, mu, r0v, v0v, t in kepler_cases(rng):
        r1, v1 = kepler_anomaly(mu, r0v, v0v, t)
        r2, v2 = rk4(mu, r0v, v0v, t)
        dr, dv = norm(sub(r1, r2)), norm(sub(v1, v2))
        if dr > 1e-4 or dv > 1e-7:
            raise RuntimeError(f"e={e}: Kepler and RK4 disagree by {dr:.3g} m, {dv:.3g} m/s")
        worst = (max(worst[0], dr), max(worst[1], dv))
        rows.append((f"e={e:g}", mu, *r0v, *v0v, t, *r1, *v1))
    return rows, worst


def render(header, rows):
    lines = ["# generated by make_reference.py -- do not edit", ",".join(header)]
    for row in rows:
        lines.append(",".join(row[0:1] + tuple(repr(float(v)) if isinstance(v, float) else str(v)
                                                for v in row[1:])))
    return "\n".join(lines) + "\n"


def main():
    check = "--check" in sys.argv[1:]
    rng = random.Random(20261101)
    lambert_rows, lw, skipped = build_lambert(rng)
    kepler_rows, kw = build_kepler(rng)

    files = {
        "lambert.csv": render(("case", "mu", "r1x", "r1y", "r1z", "r2x", "r2y", "r2z", "tof", "long",
                               "v1x", "v1y", "v1z", "v2x", "v2y", "v2z", "psi"), lambert_rows),
        "kepler.csv": render(("case", "mu", "rx", "ry", "rz", "vx", "vy", "vz", "t",
                              "rx1", "ry1", "rz1", "vx1", "vy1", "vz1"), kepler_rows),
    }
    stale = []
    for name, text in files.items():
        path = HERE / name
        if check:
            if not path.exists() or path.read_text() != text:
                stale.append(name)
        else:
            path.write_text(text)
    print(f"lambert: {len(lambert_rows)} cases ({skipped} outside the psi bracket), "
          f"solvers agree to {lw[0]:.2g} m/s, flown onto the target to {lw[1]:.2g} m")
    print(f"kepler: {len(kepler_rows)} cases, RK4 agrees to {kw[0]:.2g} m, {kw[1]:.2g} m/s")
    if stale:
        print("stale: " + ", ".join(stale), file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
