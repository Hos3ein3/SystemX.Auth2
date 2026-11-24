using System.Reflection;
using System.Text;

var dirs = args.Skip(1).ToArray();
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var n = new AssemblyName(e.Name).Name + ".dll";
    foreach (var d in dirs) { var f = Path.Combine(d, n); if (File.Exists(f)) return Assembly.LoadFrom(f); }
    return null;
};
var asm = Assembly.LoadFrom(args[0]);
Type[] types;
try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

string TypeId(Type t)
{
    if (t.IsByRef) return TypeId(t.GetElementType()!) + "@";
    if (t.IsPointer) return TypeId(t.GetElementType()!) + "*";
    if (t.IsArray) return TypeId(t.GetElementType()!) + "[" + (t.GetArrayRank() > 1 ? string.Join(",", Enumerable.Repeat("0:", t.GetArrayRank())) : "") + "]";
    if (t.IsGenericParameter) return (t.DeclaringMethod is not null ? "``" : "`") + t.GenericParameterPosition;
    var sb = new StringBuilder();
    if (t.IsGenericType && !t.IsGenericTypeDefinition)
    {
        var def = t.GetGenericTypeDefinition();
        var name = (def.FullName ?? def.Name).Replace('+', '.');
        name = name[..name.IndexOf('`')];
        sb.Append(name).Append('{').Append(string.Join(",", t.GetGenericArguments().Select(TypeId))).Append('}');
        return sb.ToString();
    }
    return (t.FullName ?? t.Name).Replace('+', '.');
}

string Params(MethodBase m)
{
    var ps = m.GetParameters();
    return ps.Length == 0 ? "" : "(" + string.Join(",", ps.Select(p => TypeId(p.ParameterType))) + ")";
}

foreach (var t in types.Where(t => t.IsPublic || t.IsNestedPublic))
{
    Console.WriteLine("T:" + (t.FullName ?? t.Name).Replace('+', '.'));
    foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
    {
        var tn = (t.FullName ?? t.Name).Replace('+', '.');
        switch (m)
        {
            case ConstructorInfo c when c.IsPublic || c.IsFamily:
                Console.WriteLine("M:" + tn + ".#ctor" + Params(c)); break;
            case MethodInfo mi when (mi.IsPublic || mi.IsFamily) && !mi.IsSpecialName:
                var gen = mi.IsGenericMethodDefinition ? "``" + mi.GetGenericArguments().Length : "";
                Console.WriteLine("M:" + tn + "." + mi.Name + gen + Params(mi));
                if (mi.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)) Console.WriteLine("X:" + tn + "." + mi.Name + gen + Params(mi));
                break;
            case PropertyInfo p when (p.GetMethod?.IsPublic ?? false) || (p.GetMethod?.IsFamily ?? false) || (p.SetMethod?.IsPublic ?? false):
                Console.WriteLine("P:" + tn + "." + p.Name); break;
            case FieldInfo f when f.IsPublic || f.IsFamily:
                Console.WriteLine("F:" + tn + "." + f.Name); break;
            case EventInfo e:
                Console.WriteLine("E:" + tn + "." + e.Name); break;
        }
    }
}
