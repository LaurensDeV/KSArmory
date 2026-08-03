using System.Reflection;

// usage: dumper <asmDir> <mode> [filter]
//   types            -> list all public types
//   grep <text>      -> list types whose full name contains text
//   members <text>   -> dump members of matching types

var dir = args[0];
var mode = args[1];
var filter = args.Length > 2 ? args[2] : "";

var paths = Directory.GetFiles(dir, "*.dll").ToList();
var rt = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
paths.AddRange(Directory.GetFiles(rt, "*.dll"));

var resolver = new PathAssemblyResolver(paths.Distinct());
using var mlc = new MetadataLoadContext(resolver, typeof(object).Assembly.GetName().Name);

IEnumerable<Type> AllTypes(string asmFile)
{
    Assembly a;
    try { a = mlc.LoadFromAssemblyPath(asmFile); } catch { return Array.Empty<Type>(); }
    try { return a.GetTypes().Where(t => t != null)!; }
    catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
}

string Sig(Type? t) => t?.FullName?.Replace("System.", "") ?? "?";

void DumpType(Type t)
{
    var kind = t.IsInterface ? "interface" : t.IsEnum ? "enum" : t.IsValueType ? "struct" : "class";
    var bas = t.BaseType != null && t.BaseType.Name != "Object" && t.BaseType.Name != "ValueType" ? $" : {t.BaseType.Name}" : "";
    var ifaces = "";
    try { var i = t.GetInterfaces(); if (i.Length > 0) ifaces = " impl " + string.Join(", ", i.Select(x => x.Name)); } catch { }
    Console.WriteLine($"\n=== {kind} {t.FullName}{bas}{ifaces}  [{t.Assembly.GetName().Name}]");
    if (t.IsEnum)
    {
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
            Console.WriteLine($"    {f.Name} = {f.GetRawConstantValue()}");
        return;
    }
    const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    try
    {
        foreach (var f in t.GetFields(BF))
            Console.WriteLine($"  F {(f.IsPublic ? "pub" : "prv")} {(f.IsStatic ? "static " : "")}{Sig(f.FieldType)} {f.Name}");
        foreach (var p in t.GetProperties(BF))
            Console.WriteLine($"  P {Sig(p.PropertyType)} {p.Name} {{{(p.CanRead ? " get;" : "")}{(p.CanWrite ? " set;" : "")} }}");
        foreach (var m in t.GetMethods(BF))
        {
            if (m.IsSpecialName) continue;
            var ps = string.Join(", ", m.GetParameters().Select(p => $"{Sig(p.ParameterType)} {p.Name}"));
            Console.WriteLine($"  M {(m.IsPublic ? "pub" : "prv")} {(m.IsStatic ? "static " : "")}{Sig(m.ReturnType)} {m.Name}({ps})");
        }
        foreach (var c in t.GetConstructors(BF))
        {
            var ps = string.Join(", ", c.GetParameters().Select(p => $"{Sig(p.ParameterType)} {p.Name}"));
            Console.WriteLine($"  C .ctor({ps})");
        }
    }
    catch (Exception e) { Console.WriteLine($"  !! {e.GetType().Name}"); }
}

if (mode == "types")
{
    foreach (var f in Directory.GetFiles(dir, "*.dll"))
        foreach (var t in AllTypes(f).Where(t => t!.IsPublic))
            Console.WriteLine(t!.FullName);
}
else if (mode == "grep")
{
    foreach (var f in Directory.GetFiles(dir, "*.dll"))
        foreach (var t in AllTypes(f))
            if ((t!.FullName ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"{t.FullName}  [{Path.GetFileNameWithoutExtension(f)}]");
}
else if (mode == "members")
{
    foreach (var f in Directory.GetFiles(dir, "*.dll"))
        foreach (var t in AllTypes(f))
        {
            string name;
            try { name = t!.FullName ?? ""; } catch { continue; }
            if (!name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            try { DumpType(t!); } catch (Exception e) { Console.WriteLine($"\n=== !! {name}: {e.GetType().Name}"); }
        }
}
