namespace KSArmory;

/// <summary>
/// A body's air as KSA's <c>PhysicalAtmosphere</c> builds it, restated: temperature linear in geopotential
/// between the declared points and constant above the last, pressure hydrostatic through each layer, density
/// from the gas law. So the burst and the round's drag read the same air.
/// </summary>
internal sealed class AtmosphereProfile
{
    private readonly double _radius;
    private readonly double _surfaceGravity;
    private readonly double _gasConstant;
    private readonly double[] _geopotential;
    private readonly double[] _kelvin;
    private readonly double[] _pascals;
    private readonly double[] _lapse;
    private readonly double _lapseBelowSeaLevel;

    /// <param name="radiusMetres">The body's mean radius (m).</param>
    /// <param name="surfaceGravity">G·M over the mean radius squared (m/s²), as KSA takes it.</param>
    /// <param name="specificGasConstant">The mixture's R over its molar mass (J/kg/K).</param>
    /// <param name="seaLevelPascals">Pressure at the first point (Pa).</param>
    /// <param name="altitudes">The declared points (m), the first at zero and rising.</param>
    /// <param name="kelvin">The temperature at each point (K).</param>
    public AtmosphereProfile(double radiusMetres, double surfaceGravity, double specificGasConstant,
                             double seaLevelPascals, IReadOnlyList<double> altitudes, IReadOnlyList<double> kelvin)
    {
        int n = altitudes.Count;
        if (n == 0 || kelvin.Count != n) throw new ArgumentException("an atmosphere needs one temperature per point");

        _radius = radiusMetres;
        _surfaceGravity = surfaceGravity;
        _gasConstant = specificGasConstant;
        _geopotential = new double[n];
        _kelvin = kelvin.ToArray();
        _pascals = new double[n];
        _lapse = new double[n];

        for (int i = 0; i < n; i++) _geopotential[i] = Geopotential(altitudes[i]);
        for (int i = 0; i < n - 1; i++)
        {
            _lapse[i] = (_kelvin[i + 1] - _kelvin[i]) / (_geopotential[i + 1] - _geopotential[i]);
        }

        _lapseBelowSeaLevel = Math.Min(_lapse[0], 0.0);
        _pascals[0] = seaLevelPascals;
        for (int i = 1; i < n; i++) _pascals[i] = LayerPressure(i - 1, _lapse[i - 1], _geopotential[i] - _geopotential[i - 1], out _);
    }

    public double SurfaceKelvin => _kelvin[0];

    public double SpecificGasConstant => _gasConstant;

    /// <summary>Pressure (Pa), temperature (K) and density (kg/m³) at an altitude over the mean radius.</summary>
    public (double Pascals, double Kelvin, double Density) At(double altitude)
    {
        double z = Geopotential(altitude);
        int layer;
        double lapse;
        if (z < _geopotential[0])
        {
            layer = 0;
            lapse = _lapseBelowSeaLevel;
        }
        else
        {
            int found = Array.BinarySearch(_geopotential, z);
            layer = found >= 0 ? found : ~found - 1;
            lapse = _lapse[layer];
        }

        double pascals = LayerPressure(layer, lapse, z - _geopotential[layer], out double kelvin);
        return (pascals, kelvin, pascals / (_gasConstant * kelvin));
    }

    private double Geopotential(double altitude) => _radius * altitude / (_radius + altitude);

    private double LayerPressure(int layer, double lapse, double above, out double kelvin)
    {
        double baseKelvin = _kelvin[layer];
        kelvin = baseKelvin + (lapse * above);
        double gOverR = _surfaceGravity / _gasConstant;

        return lapse == 0.0
                   ? _pascals[layer] * Math.Exp(-gOverR * above / baseKelvin)
                   : _pascals[layer] * Math.Pow(kelvin / baseKelvin, -gOverR / lapse);
    }
}
