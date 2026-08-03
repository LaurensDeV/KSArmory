// Reads the exact external API surface a compiled assembly depends on, straight out of its
// metadata tables.
//
//   apisurface <AirDefence.dll> [--assemblies KSA,Brutal,Bepu,StarMap]
//
// Why not grep the C#: `using KSA;` plus a call to `vehicle.Parts` tells you nothing about
// which overload bound, what the parameter types were, or that `double3`'s constructor is part
// of your surface. The compiler already resolved all of that and wrote it into the TypeRef and
// MemberRef tables. Reading those gives the real answer, and it cannot drift from the code.
//
// Why it matters: KSA is pre-release and its API moves between builds. After an update the
// decompiled corpus is hundreds of thousands of lines, and diffing it wholesale is unreadable.
// This list is what turns that diff into a checklist of the few dozen members we actually touch.
//
// The output is names and signatures of *our* dependencies - it says nothing about how KSA
// implements them - so it is safe to commit to a public repository, the same way
// ksa-assemblies.lock holds hashes and names only.

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

var path = args.FirstOrDefault(a => !a.StartsWith("--"));
if (path is null || !File.Exists(path))
{
    Console.Error.WriteLine("usage: apisurface <assembly.dll> [--assemblies A,B,C]");
    return 2;
}

var prefixes = GetOption("--assemblies")?.Split(',', StringSplitOptions.RemoveEmptyEntries)
               ?? ["KSA", "Brutal", "Bepu", "StarMap"];

string? GetOption(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

using var stream = File.OpenRead(path);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
var provider = new Signatures();

// assembly ref handle -> simple name
var assemblyOf = new Dictionary<AssemblyReferenceHandle, string>();
foreach (var handle in md.AssemblyReferences)
    assemblyOf[handle] = md.GetString(md.GetAssemblyReference(handle).Name);

bool Wanted(string assembly) => prefixes.Any(p => assembly.StartsWith(p, StringComparison.Ordinal));

// Walk out to the assembly that owns a type reference. A nested type's scope is its declaring
// TypeRef rather than an AssemblyRef, so this has to chase the chain.
(string Assembly, string Name)? ResolveType(TypeReferenceHandle handle)
{
    var parts = new List<string>();
    while (true)
    {
        var tr = md.GetTypeReference(handle);
        var ns = md.GetString(tr.Namespace);
        var name = md.GetString(tr.Name);
        parts.Insert(0, string.IsNullOrEmpty(ns) ? name : ns + "." + name);

        var scope = tr.ResolutionScope;
        if (scope.Kind == HandleKind.AssemblyReference)
        {
            var asm = assemblyOf[(AssemblyReferenceHandle)scope];
            return (asm, string.Join("+", parts));
        }
        if (scope.Kind == HandleKind.TypeReference) { handle = (TypeReferenceHandle)scope; continue; }
        return null;    // ModuleRef or the current module: not an external dependency
    }
}

// assembly -> type -> set of members
var surface = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.Ordinal);

void Touch(string assembly, string type, string? member = null)
{
    if (!Wanted(assembly)) return;
    if (!surface.TryGetValue(assembly, out var types))
        surface[assembly] = types = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
    if (!types.TryGetValue(type, out var members))
        types[type] = members = new SortedSet<string>(StringComparer.Ordinal);
    if (member is not null) members.Add(member);
}

// Every type we name at all, even if we never call a member on it (a parameter type, a cast).
foreach (var handle in md.TypeReferences)
    if (ResolveType(handle) is { } t)
        Touch(t.Assembly, t.Name);

// Every member we actually bind to.
foreach (var handle in md.MemberReferences)
{
    var mr = md.GetMemberReference(handle);
    if (mr.Parent.Kind != HandleKind.TypeReference) continue;   // generic instantiations: below
    if (ResolveType((TypeReferenceHandle)mr.Parent) is not { } t) continue;

    var name = md.GetString(mr.Name);
    var text = mr.GetKind() switch
    {
        MemberReferenceKind.Method => FormatMethod(name, mr.DecodeMethodSignature(provider, null)),
        MemberReferenceKind.Field => $"{mr.DecodeFieldSignature(provider, null)} {name}",
        _ => name,
    };
    Touch(t.Assembly, t.Name, text);
}

static string FormatMethod(string name, MethodSignature<string> sig)
{
    var generic = sig.GenericParameterCount > 0 ? $"<{sig.GenericParameterCount}>" : "";
    return $"{sig.ReturnType} {name}{generic}({string.Join(", ", sig.ParameterTypes)})";
}

var sb = new StringBuilder();
sb.AppendLine("# KSA API surface");
sb.AppendLine();
sb.AppendLine($"Every external type and member `{Path.GetFileName(path)}` binds to, read out of its");
sb.AppendLine("metadata tables by `tools/api-surface.sh`. **Generated - do not edit.**");
sb.AppendLine();
sb.AppendLine("This is the checklist for a KSA update: anything here that changed shape in the new");
sb.AppendLine("build is a breaking change for this mod, and anything not here cannot be. See the");
sb.AppendLine("`upgrade-ksa` skill, which diffs the decompiled sources against exactly this list.");

var totalTypes = surface.Sum(a => a.Value.Count);
var totalMembers = surface.Sum(a => a.Value.Sum(t => t.Value.Count));
sb.AppendLine();
sb.AppendLine($"{totalTypes} types and {totalMembers} members across {surface.Count} assemblies.");

foreach (var (assembly, types) in surface)
{
    sb.AppendLine();
    sb.AppendLine($"## {assembly}");
    foreach (var (type, members) in types)
    {
        sb.AppendLine();
        sb.AppendLine($"### {type}");
        if (members.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("*referenced as a type only*");
            continue;
        }
        sb.AppendLine();
        foreach (var m in members) sb.AppendLine($"- `{m}`");
    }
}

Console.Out.Write(sb.ToString());
return 0;

/// <summary>
/// Turns metadata signature blobs into readable type names. Only the shapes that actually occur
/// in this mod's references are spelled out; anything else degrades to a plain name rather than
/// throwing, because an unreadable signature is not worth failing a diagnostic tool over.
/// </summary>
file sealed class Signatures : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.IntPtr => "nint",
        PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.TypedReference => "TypedReference",
        _ => code.ToString(),
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawKind)
    {
        var td = reader.GetTypeDefinition(handle);
        var ns = reader.GetString(td.Namespace);
        var name = reader.GetString(td.Name);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawKind)
    {
        var tr = reader.GetTypeReference(handle);
        var ns = reader.GetString(tr.Namespace);
        var name = reader.GetString(tr.Name);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext,
                                           TypeSpecificationHandle handle, byte rawKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => "ref " + elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPinnedType(string elementType) => elementType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> args)
        => $"{genericType}<{string.Join(", ", args)}>";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetFunctionPointerType(MethodSignature<string> signature)
        => $"delegate*<{string.Join(", ", signature.ParameterTypes)}, {signature.ReturnType}>";
}
