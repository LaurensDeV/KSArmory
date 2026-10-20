using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A cloud layer's coverage map as KSA's cloud renderer samples it: a BC4 cubemap read along a
/// cloud-space direction, faces in Vulkan's order and orientation, kept compressed and decoded per
/// texel. Bilinear within a face and clamped at its edges, which no beam can see at km-scale texels.
/// </summary>
public sealed class CoverageCube
{
    private const int BlockBytes = 8;

    private readonly byte[][] _faces;

    public int Size { get; }

    private CoverageCube(byte[][] faces, int size)
    {
        _faces = faces;
        Size = size;
    }

    public static CoverageCube? FromBc4(byte[][] faces, int size)
    {
        if (faces.Length != 6 || size < 4 || size % 4 != 0) return null;

        int bytes = (size / 4) * (size / 4) * BlockBytes;
        foreach (byte[] face in faces)
        {
            if (face is null || face.Length < bytes) return null;
        }

        return new CoverageCube(faces, size);
    }

    /// <summary>Every texel the one value, for a test.</summary>
    public static CoverageCube Uniform(int size, byte value) => PerFace(size, [value, value, value, value, value, value]);

    /// <summary>Each face one value, in the order +X, -X, +Y, -Y, +Z, -Z, for a test.</summary>
    public static CoverageCube PerFace(int size, byte[] values)
    {
        int blocks = (size / 4) * (size / 4);
        byte[][] faces = new byte[6][];

        for (int f = 0; f < 6; f++)
        {
            faces[f] = new byte[blocks * BlockBytes];
            for (int b = 0; b < blocks; b++)
            {
                faces[f][b * BlockBytes] = values[f];
                faces[f][(b * BlockBytes) + 1] = values[f];
            }
        }

        return new CoverageCube(faces, size);
    }

    public float Sample(double3 direction)
    {
        if (!Face(direction, out int face, out double s, out double t)) return 0f;

        double x = (s * Size) - 0.5;
        double y = (t * Size) - 0.5;
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;

        double top = Lerp(Texel(face, x0, y0), Texel(face, x0 + 1, y0), fx);
        double bottom = Lerp(Texel(face, x0, y0 + 1), Texel(face, x0 + 1, y0 + 1), fx);
        return (float)(Lerp(top, bottom, fy) / 255.0);
    }

    private static double Lerp(double a, double b, double f) => a + ((b - a) * f);

    // Vulkan's cube face selection: the major axis picks the face, and sc, tc its texel coordinates.
    public static bool Face(double3 r, out int face, out double s, out double t)
    {
        face = 0;
        s = t = 0.0;

        double ax = Math.Abs(r.X), ay = Math.Abs(r.Y), az = Math.Abs(r.Z);
        double sc, tc, ma;

        if (ax >= ay && ax >= az)
        {
            ma = ax;
            if (r.X >= 0) { face = 0; sc = -r.Z; tc = -r.Y; }
            else { face = 1; sc = r.Z; tc = -r.Y; }
        }
        else if (ay >= az)
        {
            ma = ay;
            if (r.Y >= 0) { face = 2; sc = r.X; tc = r.Z; }
            else { face = 3; sc = r.X; tc = -r.Z; }
        }
        else
        {
            ma = az;
            if (r.Z >= 0) { face = 4; sc = r.X; tc = -r.Y; }
            else { face = 5; sc = -r.X; tc = -r.Y; }
        }

        if (!(ma > 0.0)) return false;

        s = 0.5 * ((sc / ma) + 1.0);
        t = 0.5 * ((tc / ma) + 1.0);
        return true;
    }

    private int Texel(int face, int x, int y)
    {
        x = Math.Clamp(x, 0, Size - 1);
        y = Math.Clamp(y, 0, Size - 1);

        int block = (((y >> 2) * (Size >> 2)) + (x >> 2)) * BlockBytes;
        return Bc4(_faces[face].AsSpan(block, BlockBytes), x & 3, y & 3);
    }

    /// <summary>One texel of a BC4 block: two end points, and a 3-bit index into the palette between them.</summary>
    public static int Bc4(ReadOnlySpan<byte> block, int x, int y)
    {
        int r0 = block[0], r1 = block[1];

        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)block[2 + i] << (8 * i);
        int index = (int)((bits >> (3 * ((4 * y) + x))) & 7);

        if (index == 0) return r0;
        if (index == 1) return r1;

        if (r0 > r1) return (((8 - index) * r0) + ((index - 1) * r1)) / 7;
        if (index == 6) return 0;
        if (index == 7) return 255;
        return (((6 - index) * r0) + ((index - 1) * r1)) / 5;
    }
}
