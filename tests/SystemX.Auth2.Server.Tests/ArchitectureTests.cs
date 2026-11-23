using System.Reflection;
using System.Text.RegularExpressions;

namespace SystemX.Auth2.Server.Tests;

public class ArchitectureTests
{
    private static string ServerDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SystemX.Auth2.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "SystemX.Auth2.Server");
    }

    private static IEnumerable<(string File, string Text)> Sources(string folder) =>
        Directory.EnumerateFiles(Path.Combine(ServerDir(), folder), "*.cs", SearchOption.AllDirectories)
            .Select(f => (f, File.ReadAllText(f)));

    [Fact]
    public void Domain_DoesNotUseAspNetCoreOrEfCore_InSource()
    {
        // Microsoft.AspNetCore.Identity lives in Microsoft.Extensions.Identity.Stores (no ASP.NET dependency), so it is allowed.
        var bad = new Regex(@"using\s+Microsoft\.(EntityFrameworkCore|AspNetCore\.(?!Identity;))", RegexOptions.None, TimeSpan.FromSeconds(1));
        Sources("Domain").Where(s => bad.IsMatch(s.Text)).Select(s => s.File).ShouldBeEmpty();
    }

    [Fact]
    public void Domain_Types_DoNotReferenceAspNetCoreOrEfCoreAssemblies()
    {
        var types = typeof(Program).Assembly.GetTypes().Where(t => t.Namespace?.StartsWith("SystemX.Auth2.Server.Domain") == true);
        var offenders = new List<string>();
        foreach (var t in types)
        {
            var used = new List<Type>();
            if (t.BaseType is not null) used.Add(t.BaseType);
            used.AddRange(t.GetInterfaces());
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                switch (m)
                {
                    case PropertyInfo p: used.Add(p.PropertyType); break;
                    case FieldInfo f: used.Add(f.FieldType); break;
                    case MethodBase mb:
                        used.AddRange(mb.GetParameters().Select(x => x.ParameterType));
                        if (mb is MethodInfo mi) used.Add(mi.ReturnType);
                        break;
                }
            }
            foreach (var u in used.SelectMany(Flatten))
            {
                var name = u.Assembly.GetName().Name ?? "";
                if (name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
                    offenders.Add($"{t.Name} -> {u.FullName}");
            }
        }
        offenders.ShouldBeEmpty();
    }

    private static IEnumerable<Type> Flatten(Type t)
    {
        yield return t;
        if (t.HasElementType) foreach (var x in Flatten(t.GetElementType()!)) yield return x;
        if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) foreach (var x in Flatten(a)) yield return x;
    }

    [Theory]
    [InlineData("Domain", new[] { "Application", "Persistence", "Infrastructure", "Endpoints", "Pages" })]
    [InlineData("Application", new[] { "Persistence", "Infrastructure", "Endpoints", "Pages" })]
    [InlineData("Persistence", new[] { "Endpoints", "Pages" })]
    [InlineData("Infrastructure", new[] { "Endpoints", "Pages" })]
    public void Layers_DoNotReferenceOuterLayers(string layer, string[] forbidden)
    {
        var pattern = new Regex(@"using\s+SystemX\.Auth2\.Server\.(" + string.Join("|", forbidden) + @")\b", RegexOptions.None, TimeSpan.FromSeconds(1));
        Sources(layer).Where(s => pattern.IsMatch(s.Text)).Select(s => s.File).ShouldBeEmpty();
    }
}
