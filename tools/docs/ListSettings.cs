using System.Collections;
using System.Reflection;

// Prints every setting of a settings class: path, type, default value and (for the project settings) who may edit it.
// Usage: dotnet run ListSettings.cs -- <dll> <type full name> <folder with the dependencies>...
var dirs = args.Skip(2).ToArray();
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var n = new AssemblyName(e.Name).Name + ".dll";
    foreach (var d in dirs) { var f = Path.Combine(d, n); if (File.Exists(f)) return Assembly.LoadFrom(f); }
    return null;
};
var asm = Assembly.LoadFrom(args[0]);
var root = asm.GetType(args[1]) ?? throw new Exception("type not found: " + args[1]);
Walk(root, Activator.CreateInstance(root)!, "", 0);

static string Show(object? v) => v switch
{
    null => "null",
    string s => s.Length == 0 ? "\"\"" : "\"" + s + "\"",
    bool b => b ? "true" : "false",
    TimeSpan t => t.ToString(),
    IEnumerable e when v is not string => "[" + string.Join(", ", e.Cast<object?>().Select(Show)) + "]",
    _ => v.ToString() ?? "null",
};

static void Walk(Type t, object o, string prefix, int depth)
{
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.DeclaringType == t ? 1 : 0))
    {
        if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
        var editor = p.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "SettingEditabilityAttribute");
        var ed = editor?.GetType().GetProperty("Editor")?.GetValue(editor)?.ToString() ?? "";
        object? v = null;
        try { v = p.GetValue(o); } catch { }
        var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        var isSection = pt.IsClass && pt != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(pt) && pt.Namespace is { } ns && ns.StartsWith(t.Namespace ?? "?") && depth < 3;
        if (isSection && v is not null) { Walk(pt, v, prefix + p.Name + ".", depth + 1); continue; }
        if (isSection && v is null) { Console.WriteLine($"{prefix}{p.Name}\t{Pretty(p.PropertyType)}\tnull (section)\t{ed}"); continue; }
        Console.WriteLine($"{prefix}{p.Name}\t{Pretty(p.PropertyType)}\t{Show(v)}\t{ed}");
    }
}

static string Pretty(Type t)
{
    var u = Nullable.GetUnderlyingType(t);
    if (u is not null) return Pretty(u) + "?";
    if (t.IsGenericType) return t.Name[..t.Name.IndexOf('`')] + "<" + string.Join(", ", t.GetGenericArguments().Select(Pretty)) + ">";
    return t.Name switch { "String" => "string", "Int32" => "int", "Boolean" => "bool", "Int64" => "long", _ => t.Name };
}
