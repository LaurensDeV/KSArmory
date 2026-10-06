using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace KSArmory;

/// <summary>
/// A <c>&lt;Bodies&gt;</c> file read into <see cref="BodyTraits"/> -- text in, no file access, so every
/// refusal is testable here. It sits in a mod's <c>KSArmory/</c> folder beside weapon packs and is told
/// from one by its root element (<see cref="IsBodies"/>): handed to the weapon-pack reader it would be
/// refused as a pack with the wrong root, on every load.
/// </summary>
internal static class BodyReader
{
    /// <summary>The root element of a bodies file.</summary>
    public const string Root = "Bodies";

    /// <summary>Whether a definitions file is a bodies file rather than a weapon pack.</summary>
    public static bool IsBodies(string xml)
    {
        try
        {
            using XmlReader reader = XmlReader.Create(new StringReader(xml),
                                                      new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            return reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == Root;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Every body a file describes, and every entry refused with its reason. A bad attribute costs that
    /// body its entry, never the file's other bodies.
    /// </summary>
    public static (List<(string Id, BodyTraits Traits)> Bodies, List<PackFault> Faults) Read(string xml, string source)
    {
        List<(string, BodyTraits)> bodies = [];
        List<PackFault> faults = [];

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException e)
        {
            faults.Add(new PackFault(source, Root, "", $"not well-formed XML: {e.Message}"));
            return (bodies, faults);
        }

        if (doc.Root?.Name.LocalName != Root)
        {
            faults.Add(new PackFault(source, Root, "", $"the root element is not <{Root}>"));
            return (bodies, faults);
        }

        foreach (XElement body in doc.Root.Elements())
        {
            string id = (string?)body.Attribute("Id") ?? "";
            if (body.Name.LocalName != "Body")
            {
                faults.Add(new PackFault(source, body.Name.LocalName, id, "not a <Body>"));
                continue;
            }

            if (id.Length == 0)
            {
                faults.Add(new PackFault(source, "Body", "", "has no Id"));
                continue;
            }

            try
            {
                bodies.Add((id, Traits(body)));
            }
            catch (FormatException e)
            {
                faults.Add(new PackFault(source, "Body", id, e.Message));
            }
        }

        return (bodies, faults);
    }

    private static BodyTraits Traits(XElement body)
    {
        BodyTraits t = BodyTraits.Default;
        foreach (XAttribute a in body.Attributes())
        {
            t = a.Name.LocalName switch
            {
                "Id" => t,
                "FieldTesla" => t with { FieldTesla = NonNegative(a) },
                "FieldTiltDeg" => t with { FieldTiltDeg = Number(a) },
                "FieldAzimuthDeg" => t with { FieldAzimuthDeg = Number(a) },
                "Airglow" => t with { Airglow = Enum.TryParse(a.Value, out Airglow g)
                                                    ? g
                                                    : throw new FormatException($"Airglow '{a.Value}' is not one of {string.Join(", ", Enum.GetNames<Airglow>())}") },
                "Condensation" => t with { Condensation = Bool(a) },
                "Gamma" => t with { Gamma = Number(a) is > 1.0 and < 2.0 and var g2 ? g2 : throw new FormatException($"Gamma {a.Value} is not between 1 and 2") },
                "Surface" => t with { HasSurface = Bool(a) },
                "XRayOpacity" => t with { XRayOpacity = NonNegative(a) },
                _ => throw new FormatException($"'{a.Name.LocalName}' is not an attribute a Body takes"),
            };
        }

        return t;
    }

    private static double Number(XAttribute a)
        => double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v)
               ? v
               : throw new FormatException($"{a.Name.LocalName} '{a.Value}' is not a number");

    private static double NonNegative(XAttribute a)
        => Number(a) is >= 0.0 and var v ? v : throw new FormatException($"{a.Name.LocalName} {a.Value} is negative");

    private static bool Bool(XAttribute a)
        => bool.TryParse(a.Value, out bool v) ? v : throw new FormatException($"{a.Name.LocalName} '{a.Value}' is not true or false");
}
