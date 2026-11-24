using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Client.Sync;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Enums;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SystemX.Auth2.Client.Tests;

// ---- fixtures: the same constants are used in [Authorize(Policy = ...)], WithPermission(...) and the catalog

/// <summary>Constants with and without attributes, nested classes, static readonly fields.</summary>
public static class OrderKeys
{
    public const string Read = "orders.read";

    [Auth2Permission(DisplayName = "Write orders", Description = "Create and change orders", Group = "Sales")]
    [Auth2Role(DisplayName = "Order writer", Description = "Can write orders", Permissions = ["orders.read", "orders.write"])]
    [Auth2Scope(DisplayName = "Orders scope", Description = "Read and write orders")]
    [Auth2Group(DisplayName = "Order team")]
    public const string Write = "orders.write";

    public static readonly string Delete = "orders.delete";

    public static string NotAField => "ignored.because.it.is.a.property";

    public static class Admin
    {
        public const string Refund = "orders.refund";
    }
}

/// <summary>The category comes from the attribute, not from the class name.</summary>
[Auth2Category("Billing")]
public static class BillingKeys
{
    public const string Pay = "billing.pay";
}

/// <summary>Role names may have capitals and spaces.</summary>
public static class RoleNames
{
    public const string Editor = "Editor";
    public const string Shop = "Shop Manager";
}

public enum ReportKeys
{
    OrdersRead,
    OrdersWrite,
    [Auth2Key("custom.key")] Special,
}

public enum RoleEnum { Editor, Viewer }

[Flags]
public enum FlagKeys { A = 1, B = 2 }

public enum DuplicateEnum
{
    OrdersRead,
    [Auth2Key("orders.read")] Other,
}

public static class BadKeys
{
    public const string Upper = "Orders.Read";
}

public static class EmptyKeys
{
    public const int Number = 5;
}

public class CatalogSourceTests
{
    private static string[] Keys(Auth2PermissionOptions o) => [.. o.Merge().Select(d => d.Key)];

    // ------------------------------------------------------------------ AddFromClass

    [Fact]
    public void AddFromClass_ReadsConstAndStaticReadonly_AlsoInNestedClasses_AndNotProperties()
    {
        var o = new Auth2PermissionOptions();
        o.AddFromClass(typeof(OrderKeys));
        Keys(o).ShouldBe(["orders.delete", "orders.read", "orders.refund", "orders.write"]);
    }

    [Fact]
    public void AddFromClass_MetadataFromTheAttribute_ElseFromTheFieldName_AndTheCategoryIsTheClassName()
    {
        var o = new Auth2PermissionOptions();
        o.AddFromClass(typeof(OrderKeys)).AddFromClass(typeof(BillingKeys));
        var all = o.Merge().ToDictionary(d => d.Key);
        all["orders.write"].ShouldBe(new PermissionDefinition("orders.write", "Write orders", "Create and change orders", "Sales"));
        all["orders.read"].ShouldBe(new PermissionDefinition("orders.read", "Read", null, "OrderKeys"));          // name from the field, no description
        all["orders.refund"].ShouldBe(new PermissionDefinition("orders.refund", "Refund", null, "Admin"));        // nested: the class it is in
        all["billing.pay"].Group.ShouldBe("Billing");                                                            // [Auth2Category] wins
    }

    [Fact]
    public void AddFromClass_ForRoles_Scopes_AndGroups_UsesTheirOwnAttribute()
    {
        var roles = new Auth2RoleOptions();
        roles.AddFromClass(typeof(RoleNames)).AddFromClass(typeof(OrderKeys));
        var role = roles.Merge().Single(d => d.Name == "orders.write");
        (role.Name, role.DisplayName, role.Description).ShouldBe(("orders.write", "Order writer", "Can write orders"));
        role.Permissions.ShouldBe(["orders.read", "orders.write"]);
        roles.Merge().Select(d => d.Name).ShouldContain("Shop Manager");   // capitals and spaces are fine for roles

        var scopes = new Auth2ScopeOptions().AddFromClass(typeof(OrderKeys)) as Auth2ScopeOptions;
        scopes!.Merge().Single(d => d.Name == "orders.write").ShouldBe(new ScopeDefinition("orders.write", "Orders scope", "Read and write orders"));

        var groups = new Auth2GroupOptions();
        groups.AddFromClass(typeof(OrderKeys));
        groups.Merge().Single(d => d.Name == "orders.write").ShouldBe(new GroupDefinition("orders.write", "Order team"));
    }

    [Fact]
    public void AddFromClass_WrongKeys_NameTheClassAndTheField()
    {
        var upper = Should.Throw<Auth2CatalogException>(() => new Auth2PermissionOptions().AddFromClass(typeof(BadKeys)));
        upper.Message.ShouldContain("BadKeys.Upper");
        upper.Message.ShouldContain("Orders.Read");
        Should.Throw<Auth2CatalogException>(() => new Auth2PermissionOptions().AddFromClass(typeof(EmptyKeys))).Message.ShouldContain("EmptyKeys");
    }

    // ------------------------------------------------------------------ AddFromEnum

    [Theory]
    [InlineData(KeyStyle.DotCase, "orders.read", "orders.write")]
    [InlineData(KeyStyle.AsIs, "OrdersRead", "OrdersWrite")]
    [InlineData(KeyStyle.SnakeCase, "orders_read", "orders_write")]
    [InlineData(KeyStyle.KebabCase, "orders-read", "orders-write")]
    public void AddFromEnum_KeyStyles(KeyStyle style, string read, string write)
    {
        var o = new Auth2ScopeOptions();   // scopes accept every one of these styles ... except upper case below
        if (style == KeyStyle.AsIs) { Should.Throw<Auth2CatalogException>(() => new Auth2PermissionOptions().AddFromEnum<ReportKeys>(style)); return; }
        var p = new Auth2PermissionOptions();
        p.AddFromEnum<ReportKeys>(style);
        Keys(p).ShouldBe(new[] { read, write, "custom.key" }.Order(StringComparer.Ordinal));
        o.AddFromEnum<ReportKeys>(style).ShouldBeSameAs(o);
    }

    [Fact]
    public void AddFromEnum_AsIsKeepsTheNameForRoles_AndTheAuth2KeyAttributeWins_AndTheCategoryIsTheEnumName()
    {
        var roles = new Auth2RoleOptions();
        roles.AddFromEnum<RoleEnum>(KeyStyle.AsIs);
        roles.Merge().Select(d => d.Name).ShouldBe(["Editor", "Viewer"]);

        var p = new Auth2PermissionOptions();
        p.AddFromEnum<ReportKeys>();
        var special = p.Merge().Single(d => d.Key == "custom.key");
        special.DisplayName.ShouldBe("Special");
        special.Group.ShouldBe("ReportKeys");
        p.AddFromEnum(typeof(ReportKeys));   // the non-generic overload; the same data twice is merged
        Keys(p).Length.ShouldBe(3);
    }

    [Fact]
    public void AddFromEnum_FlagsEnum_FailsWithItsName_AndTwoMembersWithTheSameKeyFail()
    {
        Should.Throw<Auth2CatalogException>(() => new Auth2PermissionOptions().AddFromEnum<FlagKeys>()).Message.ShouldContain("FlagKeys");
        var dup = Should.Throw<Auth2CatalogException>(() => new Auth2PermissionOptions().AddFromEnum<DuplicateEnum>());
        dup.Message.ShouldContain("orders.read");
        dup.Message.ShouldContain("OrdersRead");
        dup.Message.ShouldContain("Other");
    }

    // ------------------------------------------------------------------ AddFromString / AddFromListString

    [Fact]
    public void AddFromString_SplitsTrims_DropsEmptyParts_AndMergesDuplicates()
    {
        var o = new Auth2PermissionOptions();
        o.AddFromString(" orders.read , ,orders.write,,orders.read,  ");
        Keys(o).ShouldBe(["orders.read", "orders.write"]);
        o.Merge().ShouldAllBe(d => d.DisplayName == null && d.Description == null);   // key only
    }

    [Fact]
    public void AddFromString_CustomSeparator_AndCategory()
    {
        var o = new Auth2PermissionOptions();
        o.AddFromString("a.read;b.read; c.read", ';', "Reports");
        Keys(o).ShouldBe(["a.read", "b.read", "c.read"]);
        o.Merge().ShouldAllBe(d => d.Group == "Reports");
    }

    [Fact]
    public void AddFromString_APartThatIsNotAValidKey_IsNamed_AndEmptyTextAddsNothing()
    {
        var ex = Should.Throw<Auth2CatalogException>(() => new Auth2PermissionOptions().AddFromString("orders.read,Bad Key,orders.write"));
        ex.Message.ShouldContain("'Bad Key'");
        var empty = new Auth2PermissionOptions().AddFromString("  , ,, ") as Auth2PermissionOptions;
        empty!.Merge().ShouldBeEmpty();
    }

    [Fact]
    public void AddFromListString_SameRules_AndAPartWithTheSeparatorIsAnError()
    {
        var o = new Auth2GroupOptions();
        o.AddFromListString(["finance", " support ", "", "finance"]);
        o.Merge().Select(d => d.Name).ShouldBe(["finance", "support"]);
        var ex = Should.Throw<Auth2CatalogException>(() => new Auth2GroupOptions().AddFromListString(["finance,support"]));
        ex.Message.ShouldContain("'finance,support'");
        ex.Message.ShouldContain("separator");
    }

    [Fact]
    public void Scope_WithASpace_IsRejected_AndNamesThePart()
    {
        var ex = Should.Throw<Auth2CatalogException>(() => new Auth2ScopeOptions().AddFromString("reports.read,reports write"));
        ex.Message.ShouldContain("'reports write'");
        ex.Message.ShouldContain("space");
        Should.Throw<Auth2CatalogException>(() => new Auth2ScopeOptions().AddFromListString(["a\"b"])).Message.ShouldContain("double quote");
        Should.Throw<Auth2CatalogException>(() => new Auth2ScopeOptions().AddFromListString(["a\\b"])).Message.ShouldContain("backslash");
    }

    // ------------------------------------------------------------------ Add, configuration, factories, providers

    [Fact]
    public void Add_AFullDefinition_AndTheListShortcut_AreBothUsedByTheScanner()
    {
        var o = new Auth2RoleOptions { ScanningPolicy = ScanningPolicy.Manual };
        o.RoleList.Add(new RoleDefinition("Listed"));
        o.Add(new RoleDefinition("Added", "Added role", null, ["orders.read"]));
        o.AddFromString("Stringed");
        new RoleScanner([], MsOptions.Create(o), NullLogger<RoleScanner>.Instance).Scan().Select(r => r.Name).ShouldBe(["Added", "Listed", "Stringed"]);
    }

    [Fact]
    public void AddFromConfiguration_ReadsPlainValuesAndSections_ForEveryCatalog()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Perm:0"] = "orders.read",
            ["Perm:1:Key"] = "orders.write", ["Perm:1:DisplayName"] = "Write", ["Perm:1:Description"] = "Writes", ["Perm:1:Group"] = "Sales",
            ["Roles:0:Name"] = "Editor", ["Roles:0:Permissions:0"] = "orders.read", ["Roles:0:Permissions:1"] = "orders.write",
            ["Roles:1"] = "Viewer",
            ["Scopes:0"] = "reports.read",
            ["Groups:0:Name"] = "finance", ["Groups:0:DisplayName"] = "Finance team",
        }).Build();

        var p = new Auth2PermissionOptions();
        p.AddFromConfiguration(config.GetSection("Perm"));
        p.Merge().ShouldBe([new PermissionDefinition("orders.read"), new PermissionDefinition("orders.write", "Write", "Writes", "Sales")]);

        var r = new Auth2RoleOptions();
        r.AddFromConfiguration(config.GetSection("Roles"));
        r.Merge().ShouldBe([new RoleDefinition("Editor", null, null, ["orders.read", "orders.write"]), new RoleDefinition("Viewer", null, null, [])], comparer: new RoleComparer());

        var s = new Auth2ScopeOptions();
        s.AddFromConfiguration(config.GetSection("Scopes"));
        s.Merge().ShouldBe([new ScopeDefinition("reports.read")]);

        var g = new Auth2GroupOptions();
        g.AddFromConfiguration(config.GetSection("Groups"));
        g.Merge().ShouldBe([new GroupDefinition("finance", "Finance team")]);
    }

    private sealed class RoleComparer : IEqualityComparer<RoleDefinition>
    {
        public bool Equals(RoleDefinition? a, RoleDefinition? b) => a!.Name == b!.Name && (a.Permissions ?? []).SequenceEqual(b.Permissions ?? []);
        public int GetHashCode(RoleDefinition d) => d.Name.GetHashCode();
    }

    private sealed class StaticProvider : IPermissionProvider
    {
        public Task<IEnumerable<PermissionDefinition>> GetAsync(CancellationToken ct = default) =>
            Task.FromResult<IEnumerable<PermissionDefinition>>([new PermissionDefinition("provided.read", "From provider")]);
    }

    private sealed class DependencyProvider(ProviderDependency dependency) : IRoleProvider
    {
        public async Task<IEnumerable<RoleDefinition>> GetAsync(CancellationToken ct = default)
        {
            await Task.Yield();
            return dependency.Names.Select(n => new RoleDefinition(n));
        }
    }

    private sealed class ProviderDependency { public string[] Names { get; } = ["FromDi"]; }

    private sealed class ThrowingProvider : IScopeProvider
    {
        public Task<IEnumerable<ScopeDefinition>> GetAsync(CancellationToken ct = default) => throw new InvalidOperationException("the database is down");
    }

    [Fact]
    public async Task Providers_AreResolvedFromDi_WhenTheHostStarts_AndFactoriesToo_Async()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ProviderDependency>();
        var sp = services.BuildServiceProvider();

        var p = new Auth2PermissionOptions();
        p.AddFromString("manual.read").AddProvider<StaticProvider>()
            .AddFactory(_ => [new PermissionDefinition("factory.read")])
            .AddFactory(async (_, ct) => { await Task.Delay(1, ct); return [new PermissionDefinition("async.read")]; });
        (await p.ResolveAsync(sp)).Select(d => d.Key).ShouldBe(["async.read", "factory.read", "manual.read", "provided.read"]);

        var r = new Auth2RoleOptions();
        r.AddProvider<DependencyProvider>();   // needs a service: resolved through DI
        (await r.ResolveAsync(sp)).Select(d => d.Name).ShouldBe(["FromDi"]);
    }

    [Fact]
    public async Task AProviderThatThrows_GivesAClearStartupError_WithItsKind()
    {
        var o = new Auth2ScopeOptions { SyncPolicy = ScopeSyncPolicy.Push };
        o.AddProvider<ThrowingProvider>();
        var startup = new Auth2CatalogStartupService(new ServiceCollection().BuildServiceProvider(), MsOptions.Create(new Auth2PermissionOptions()),
            MsOptions.Create(new Auth2RoleOptions()), MsOptions.Create(new Auth2GroupOptions()), MsOptions.Create(o));
        var ex = await Should.ThrowAsync<Auth2CatalogException>(() => startup.StartAsync(default));
        ex.Message.ShouldContain("scope");
        ex.Message.ShouldContain("the database is down");
    }

    [Fact]
    public async Task AProviderWithAWrongKey_NamesTheProvider()
    {
        var o = new Auth2PermissionOptions();
        o.AddFactory(_ => [new PermissionDefinition("Bad Key")]);
        var ex = await Should.ThrowAsync<Auth2CatalogException>(() => o.ResolveAsync(new ServiceCollection().BuildServiceProvider()));
        ex.Message.ShouldContain("'Bad Key'");
        ex.Message.ShouldContain("factory");
    }

    // ------------------------------------------------------------------ merging

    [Fact]
    public void Merge_MixedSources_AreCombined_AndTheFirstSourceWinsWhenAsked()
    {
        var o = new Auth2PermissionOptions { DuplicatePolicy = DuplicatePolicy.FirstWins };
        o.AddFromClass(typeof(OrderKeys));
        o.AddFromEnum<ReportKeys>();                 // gives orders.read and orders.write again, with other names
        o.AddFromString("extra.read,orders.read");   // and once more
        Keys(o).ShouldBe(["custom.key", "extra.read", "orders.delete", "orders.read", "orders.refund", "orders.write"]);
        o.Merge().Single(d => d.Key == "orders.read").DisplayName.ShouldBe("Read");   // the class came first
    }

    [Fact]
    public void Merge_IdenticalDefinitions_AreFine_WhileDifferentOnesFailAndNameBothSources()
    {
        var same = new Auth2PermissionOptions();
        same.Add(new PermissionDefinition("orders.read", "Read", null, null));
        same.Add(new PermissionDefinition("orders.read", "Read", null, null));
        same.Merge().Count.ShouldBe(1);

        var conflict = new Auth2PermissionOptions();
        conflict.AddFromClass(typeof(OrderKeys));   // orders.read: name "Read", category OrderKeys
        conflict.Add(new PermissionDefinition("orders.read", "Read orders", "other text", null));
        var ex = Should.Throw<Auth2CatalogException>(() => conflict.Merge());
        ex.Message.ShouldContain("'orders.read'");
        ex.Message.ShouldContain("OrderKeys.Read");
        ex.Message.ShouldContain("Add(orders.read)");
        ex.Message.ShouldContain("FirstWins");
    }

    [Fact]
    public void Merge_FirstWins_AcceptsTheConflict_AndTheFirstSourceStays()
    {
        var o = new Auth2PermissionOptions { DuplicatePolicy = DuplicatePolicy.FirstWins };
        o.Add(new PermissionDefinition("orders.read", "First"));
        o.Add(new PermissionDefinition("orders.read", "Second"));
        o.Merge().Single().DisplayName.ShouldBe("First");
    }

    [Fact]
    public void Merge_IsCaseSensitive_SoTwoCasesOfARoleAreTwoRoles()
    {
        var o = new Auth2RoleOptions();
        o.AddFromString("Editor,editor");
        o.Merge().Select(d => d.Name).ShouldBe(["Editor", "editor"]);
    }

    // ------------------------------------------------------------------ the scanner and the policies

    [Fact]
    public void ScanningPolicy_Manual_AndBoth_UseTheSources_AssemblyScan_DoesNot()
    {
        PermissionScanner Scanner(ScanningPolicy policy)
        {
            var o = new Auth2PermissionOptions { ScanningPolicy = policy };
            o.AddFromClass(typeof(OrderKeys));
            return new PermissionScanner([], MsOptions.Create(o), NullLogger<PermissionScanner>.Instance);
        }
        Scanner(ScanningPolicy.Manual).Scan().Count.ShouldBe(4);
        Scanner(ScanningPolicy.Both).Scan().Count.ShouldBe(4);
        Scanner(ScanningPolicy.AssemblyScan).Scan().ShouldBeEmpty();
    }

    [Fact]
    public void ScopeScanner_StillSkipsReservedScopes_FromTheNewSources()
    {
        var o = new Auth2ScopeOptions { ScanningPolicy = ScanningPolicy.Manual };
        o.AddFromString("reports.read,auth2.me,openid");
        new ScopeScanner([], MsOptions.Create(o), NullLogger<ScopeScanner>.Instance).Scan().Select(s => s.Name).ShouldBe(["reports.read"]);
    }

    [Fact]
    public void TheSameConstants_WorkInTheAttributeAndInTheCatalog()
    {
        // the field value is the key everywhere: [Auth2Permission(OrderKeys.Read)] on an endpoint, WithPermission(OrderKeys.Read), and the catalog
        new Auth2PermissionAttribute(OrderKeys.Read).Permissions.ShouldBe(["orders.read"]);
        var o = new Auth2PermissionOptions().AddFromClass(typeof(OrderKeys)) as Auth2PermissionOptions;
        o!.Merge().Select(d => d.Key).ShouldContain(OrderKeys.Read);
    }
}
