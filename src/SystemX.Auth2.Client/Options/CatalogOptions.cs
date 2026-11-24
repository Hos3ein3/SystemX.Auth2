using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Client.Authorization;

namespace SystemX.Auth2.Client.Options;

/// <summary>How an enum member name becomes a key.</summary>
public enum KeyStyle
{
    /// <summary><c>OrdersRead</c> becomes <c>orders.read</c>. The default.</summary>
    DotCase,
    /// <summary>The member name as it is: <c>OrdersRead</c>.</summary>
    AsIs,
    /// <summary><c>OrdersRead</c> becomes <c>orders_read</c>.</summary>
    SnakeCase,
    /// <summary><c>OrdersRead</c> becomes <c>orders-read</c>.</summary>
    KebabCase,
}

/// <summary>What happens when two sources give the same key with different data.</summary>
public enum DuplicatePolicy
{
    /// <summary>The application does not start. The message names both sources. The default.</summary>
    Fail,
    /// <summary>The first source wins. Later definitions of the same key are ignored.</summary>
    FirstWins,
}

/// <summary>Overrides the key of an enum member (<c>[Auth2Key("orders.read")]</c>). Without it the key comes from the member name and the <see cref="KeyStyle"/>.</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class Auth2KeyAttribute(string key) : Attribute
{
    /// <summary>The key.</summary>
    public string Key { get; } = key;
}

/// <summary>Overrides the category of the constants of a class (the default is the class name). Used for the group label of permissions.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
public sealed class Auth2CategoryAttribute(string category) : Attribute
{
    /// <summary>The category.</summary>
    public string Category { get; } = category;
}

/// <summary>Something is wrong with a catalog source. The message names the source, class, field or part that is wrong. The application does not start.</summary>
public sealed class Auth2CatalogException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>Gives permissions, resolved from DI when the host starts (just before the first sync). Register it with <c>AddProvider&lt;T&gt;()</c>.</summary>
public interface IPermissionProvider : ICatalogProvider<SystemX.Auth2.Contracts.Access.PermissionDefinition>;

/// <summary>Gives roles, resolved from DI when the host starts.</summary>
public interface IRoleProvider : ICatalogProvider<SystemX.Auth2.Contracts.Access.RoleDefinition>;

/// <summary>Gives custom scopes, resolved from DI when the host starts.</summary>
public interface IScopeProvider : ICatalogProvider<SystemX.Auth2.Contracts.Access.ScopeDefinition>;

/// <summary>Gives groups, resolved from DI when the host starts.</summary>
public interface IGroupProvider : ICatalogProvider<SystemX.Auth2.Contracts.Access.GroupDefinition>;

/// <summary>A source of catalog definitions that needs services (a database, a file, another service). Async on purpose.</summary>
public interface ICatalogProvider<TDefinition> where TDefinition : class
{
    /// <summary>Returns the definitions.</summary>
    Task<IEnumerable<TDefinition>> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// The sources of one catalog (permissions, roles, scopes or groups). All four catalogs share these methods and these rules:
/// definitions from all sources are merged by key (ordinal, case-sensitive); the same key with the same data is ignored; the same key with
/// different data stops the start unless <see cref="DuplicatePolicy"/> is <see cref="Options.DuplicatePolicy.FirstWins"/>.
/// Keys are checked when a source is added (static sources) or when the host starts (providers and factories). A wrong key names its source.
/// Sync never deletes anything on the server: a key that is not listed any more is marked obsolete there.
/// </summary>
public abstract class CatalogOptions<TDefinition> where TDefinition : class
{
    private readonly List<Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<(TDefinition Definition, string Source)>>>> _lateSources = [];
    private readonly List<(TDefinition Definition, string Source)> _entries = [];
    private IReadOnlyList<(TDefinition Definition, string Source)>? _lateResolved;

    /// <summary>What happens when two sources give the same key with different data. Default <see cref="Options.DuplicatePolicy.Fail"/>.</summary>
    public DuplicatePolicy DuplicatePolicy { get; set; } = DuplicatePolicy.Fail;

    // ---------------------------------------------------------------- what a catalog has to know about its own kind

    /// <summary>"permission", "role", "scope" or "group" (used in messages).</summary>
    protected abstract string Kind { get; }
    /// <summary>The key of a definition.</summary>
    protected abstract string KeyOf(TDefinition definition);
    /// <summary>Makes a definition from a key only (name = key, description empty).</summary>
    protected abstract TDefinition FromKey(string key, string? category);
    /// <summary>Makes a definition from a key and the attribute of a field or an enum member (display name, description, ...).</summary>
    protected abstract TDefinition FromMember(string key, string defaultDisplayName, MemberInfo member, string? category);
    /// <summary>Makes a definition from a configuration section (a plain value is a key, a section has the fields).</summary>
    protected abstract TDefinition FromConfiguration(IConfigurationSection section);
    /// <summary>Returns a message when the key is not valid for this catalog, else null.</summary>
    protected abstract string? CheckKey(string key);
    /// <summary>True when two definitions with the same key carry the same data.</summary>
    protected abstract bool SameData(TDefinition a, TDefinition b);

    // ---------------------------------------------------------------- sources

    /// <summary>Adds one full definition.</summary>
    public CatalogOptions<TDefinition> Add(TDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        AddChecked(definition, $"Add({KeyOf(definition)})");
        return this;
    }

    /// <summary>
    /// Adds the <c>public const string</c> and <c>public static readonly string</c> fields of a class, nested classes included. The <b>value</b> is the key.
    /// Display name and description come from <c>[Auth2Permission]</c>, <c>[Auth2Role]</c>, <c>[Auth2Scope]</c> or <c>[Auth2Group]</c> on the field;
    /// without it the display name is made from the field name. The category is the class name (<see cref="Auth2CategoryAttribute"/> on the class overrides it).
    /// You pass the type: nothing scans the whole assembly (trimming friendly).
    /// </summary>
    public CatalogOptions<TDefinition> AddFromClass(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var found = 0;
        void Walk(Type t)
        {
            var category = t.GetCustomAttribute<Auth2CategoryAttribute>()?.Category ?? t.Name;
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (f.FieldType != typeof(string)) continue;
                if (!(f.IsLiteral || f.IsInitOnly)) continue;   // const (literal) or static readonly
                var value = f.IsLiteral ? (string?)f.GetRawConstantValue() : (string?)f.GetValue(null);
                var source = $"{t.FullName?.Replace('+', '.')}.{f.Name}";
                if (string.IsNullOrWhiteSpace(value)) throw new Auth2CatalogException($"The {Kind} field {source} is empty. The value of the field is the key.");
                AddChecked(FromMember(value, Humanize(f.Name), f, category), $"class {source}");
                found++;
            }
            foreach (var nested in t.GetNestedTypes(BindingFlags.Public)) Walk(nested);
        }
        Walk(type);
        if (found == 0) throw new Auth2CatalogException($"The class {type.FullName} has no public const string or static readonly string fields to read {Kind} keys from.");
        return this;
    }

    /// <summary>Adds the members of an enum. The key is <c>[Auth2Key]</c> when present, else the member name in the given <see cref="KeyStyle"/>. A <c>[Flags]</c> enum or two members with the same key stop the start.</summary>
    public CatalogOptions<TDefinition> AddFromEnum<TEnum>(KeyStyle keyStyle = KeyStyle.DotCase) where TEnum : struct, Enum => AddFromEnum(typeof(TEnum), keyStyle);

    /// <summary>Adds the members of an enum (see the generic overload).</summary>
    public CatalogOptions<TDefinition> AddFromEnum(Type enumType, KeyStyle keyStyle = KeyStyle.DotCase)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        if (!enumType.IsEnum) throw new Auth2CatalogException($"{enumType.FullName} is not an enum.");
        if (enumType.IsDefined(typeof(FlagsAttribute), inherit: false))
            throw new Auth2CatalogException($"The enum {enumType.FullName} has [Flags]. Its members are bit values, not separate {Kind}s. Use an enum without [Flags].");
        var category = enumType.GetCustomAttribute<Auth2CategoryAttribute>()?.Category ?? enumType.Name;
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var key = f.GetCustomAttribute<Auth2KeyAttribute>()?.Key ?? ConvertName(f.Name, keyStyle);
            var source = $"enum {enumType.FullName?.Replace('+', '.')}.{f.Name}";
            if (seen.TryGetValue(key, out var other))
                throw new Auth2CatalogException($"The enum {enumType.FullName} gives the {Kind} key '{key}' twice: {other} and {f.Name}. Rename one, or set [Auth2Key].");
            seen[key] = f.Name;
            AddChecked(FromMember(key, Humanize(f.Name), f, category), source);
        }
        if (seen.Count == 0) throw new Auth2CatalogException($"The enum {enumType.FullName} has no members.");
        return this;
    }

    /// <summary>
    /// Adds keys from a text such as <c>"orders.read, orders.write"</c>: split by the separator, trimmed, empty parts dropped, duplicates merged.
    /// Key only: the name is the key and the description is empty. A part that is not a valid key stops the start and the message names it.
    /// </summary>
    public CatalogOptions<TDefinition> AddFromString(string keys, char separator = ',', string? category = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in keys.Split(separator))
        {
            var part = raw.Trim();
            if (part.Length == 0 || !seen.Add(part)) continue;
            AddChecked(FromKey(part, category), $"string part '{part}'");
        }
        return this;
    }

    /// <summary>Adds keys from a list (the same rules as <see cref="AddFromString"/>). A key that contains the separator <c>,</c> is an error: it is most likely a joined text in one element.</summary>
    public CatalogOptions<TDefinition> AddFromListString(IEnumerable<string> keys, string? category = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in keys)
        {
            var part = (raw ?? "").Trim();
            if (part.Length == 0 || !seen.Add(part)) continue;
            if (part.Contains(',')) throw new Auth2CatalogException($"The list part '{part}' contains the separator ','. Give one key per element, or use AddFromString for a joined text.");
            AddChecked(FromKey(part, category), $"list part '{part}'");
        }
        return this;
    }

    /// <summary>Adds a provider that is resolved from DI when the host starts (just before the first sync).</summary>
    public CatalogOptions<TDefinition> AddProvider<TProvider>() where TProvider : class, ICatalogProvider<TDefinition>
    {
        _lateSources.Add(async (sp, ct) =>
        {
            var provider = ActivatorUtilities.GetServiceOrCreateInstance<TProvider>(sp);
            return Check(await provider.GetAsync(ct), $"provider {typeof(TProvider).FullName}");
        });
        return this;
    }

    /// <summary>Adds a factory that is called with the service provider when the host starts.</summary>
    public CatalogOptions<TDefinition> AddFactory(Func<IServiceProvider, IEnumerable<TDefinition>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _lateSources.Add((sp, _) => Task.FromResult(Check(factory(sp), "factory")));
        return this;
    }

    /// <summary>Adds an async factory that is called with the service provider when the host starts.</summary>
    public CatalogOptions<TDefinition> AddFactory(Func<IServiceProvider, CancellationToken, Task<IEnumerable<TDefinition>>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _lateSources.Add(async (sp, ct) => Check(await factory(sp, ct), "factory"));
        return this;
    }

    /// <summary>
    /// Adds definitions from a configuration section. Each child is a plain value (the key) or a section with the fields
    /// (<c>Key</c>/<c>Name</c>, <c>DisplayName</c>, <c>Description</c>, <c>Permissions</c> for roles, <c>Group</c> for permissions).
    /// </summary>
    public CatalogOptions<TDefinition> AddFromConfiguration(IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        foreach (var child in section.GetChildren())
            AddChecked(FromConfiguration(child), $"configuration {child.Path}");
        return this;
    }

    // ---------------------------------------------------------------- resolve

    /// <summary>
    /// Merges the static sources (already checked) with the providers and factories. Called when the host starts. A provider that throws gives an
    /// <see cref="Auth2CatalogException"/> with its name; two sources with different data for one key do too (unless <see cref="Options.DuplicatePolicy.FirstWins"/>).
    /// </summary>
    public async Task<IReadOnlyList<TDefinition>> ResolveAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var late = new List<(TDefinition Definition, string Source)>();
        foreach (var source in _lateSources)
        {
            try { late.AddRange(await source(services, ct)); }
            catch (Auth2CatalogException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { throw new Auth2CatalogException($"A {Kind} source failed while the application was starting: {ex.GetType().Name}: {ex.Message}", ex); }
        }
        _lateResolved = late;
        return Merge();
    }

    /// <summary>The merged definitions of the static sources and of the providers resolved so far.</summary>
    public IReadOnlyList<TDefinition> Merge()
    {
        var result = new Dictionary<string, (TDefinition Definition, string Source)>(StringComparer.Ordinal);
        foreach (var entry in _entries.Concat(_lateResolved ?? []))
        {
            var key = KeyOf(entry.Definition);
            if (!result.TryGetValue(key, out var first)) { result[key] = entry; continue; }
            if (SameData(first.Definition, entry.Definition) || DuplicatePolicy == DuplicatePolicy.FirstWins) continue;
            throw new Auth2CatalogException($"The {Kind} key '{key}' has different data in {first.Source} and in {entry.Source}. Make them equal, remove one, or set DuplicatePolicy = FirstWins.");
        }
        return [.. result.Values.Select(v => v.Definition).OrderBy(KeyOf, StringComparer.Ordinal)];
    }

    /// <summary>True when providers or factories were added.</summary>
    public bool HasLateSources => _lateSources.Count > 0;

    private IReadOnlyList<(TDefinition, string)> Check(IEnumerable<TDefinition>? definitions, string source)
    {
        var list = new List<(TDefinition, string)>();
        foreach (var d in definitions ?? throw new Auth2CatalogException($"The {Kind} {source} returned null."))
        {
            if (d is null) throw new Auth2CatalogException($"The {Kind} {source} returned a null definition.");
            var problem = CheckKey(KeyOf(d));
            if (problem is not null) throw new Auth2CatalogException($"The {Kind} key '{KeyOf(d)}' from {source} is not valid: {problem}");
            list.Add((d, source));
        }
        return list;
    }

    private void AddChecked(TDefinition definition, string source)
    {
        var key = KeyOf(definition);
        var problem = CheckKey(key);
        if (problem is not null) throw new Auth2CatalogException($"The {Kind} key '{key}' from {source} is not valid: {problem}");
        _entries.Add((definition, source));
    }

    // ---------------------------------------------------------------- names

    private static string ConvertName(string name, KeyStyle style)
    {
        if (style == KeyStyle.AsIs) return name;
        var words = SplitWords(name);
        return style switch
        {
            KeyStyle.SnakeCase => string.Join('_', words),
            KeyStyle.KebabCase => string.Join('-', words),
            _ => string.Join('.', words),
        };
    }

    private static List<string> SplitWords(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '_' or '-' or ' ' or '.') { Flush(); continue; }
            var startsWord = i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1])));
            if (startsWord) Flush();
            current.Append(char.ToLowerInvariant(c));
        }
        Flush();
        return words;
        void Flush() { if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); } }
    }

    /// <summary>"OrdersRead" gives "Orders read".</summary>
    protected static string Humanize(string name)
    {
        var words = SplitWords(name);
        return words.Count == 0 ? name : char.ToUpperInvariant(words[0][0]) + words[0][1..] + (words.Count > 1 ? " " + string.Join(' ', words.Skip(1)) : "");
    }
}

/// <summary>The four catalogs: what each one knows about its own keys and definitions.</summary>
internal static class CatalogRules
{
    public const string KeyFormat = "use lowercase letters, digits and . _ : - (1 to 100 characters), starting and ending with a letter or digit";

    public static string? CheckLowerKey(string key) => SystemX.Auth2.Client.Sync.Auth2Keys.NormalizePermissionKey(key) == key ? null : KeyFormat;

    public static string? CheckRoleName(string key) =>
        SystemX.Auth2.Client.Sync.Auth2Keys.NormalizeRoleName(key) == key ? null : "use letters, digits, space and . _ : - (1 to 100 characters)";

    /// <summary>OAuth scope rules (no space, double quote or backslash, no control characters), then the format of this server.</summary>
    public static string? CheckScopeName(string key)
    {
        if (key.Contains(' ')) return "a scope must not contain a space (the scope parameter of OAuth is a space-separated list)";
        if (key.Contains('"')) return "a scope must not contain a double quote";
        if (key.Contains('\\')) return "a scope must not contain a backslash";
        if (key.Any(char.IsControl)) return "a scope must not contain control characters";
        return CheckLowerKey(key);
    }
}
