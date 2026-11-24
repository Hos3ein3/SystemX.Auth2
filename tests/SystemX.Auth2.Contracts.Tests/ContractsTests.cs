using System.Reflection;
using System.Text.Json;
using SystemX.Auth2.Contracts;
using SystemX.Auth2.Contracts.Access;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Common;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Contracts.Users;

namespace SystemX.Auth2.Contracts.Tests;

public class DtoSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static TheoryData<object> Samples() => new()
    {
        new PermissionDefinition("orders.read", "Read orders", "d", "Orders"),
        new RoleDefinition("Editor", "Editor", null, ["orders.read"]),
        new GroupDefinition("Finance", "Finance team"),
        new SyncResponse(1, 2, 3, ["a.b"]),
        new PermissionSyncRequest([new PermissionDefinition("a.b")]),
        new RoleSyncRequest([new RoleDefinition("Admin")]),
        new GroupSyncRequest(["g"]),
        new CurrentUserInfo(Guid.NewGuid(), "sara", "s@e.com", "+31600000000", "Sara", "K", "Sara K",
            ["Editor"], ["orders.read"], ["Finance"], ["openid"], 7),
        new UserDto(Guid.NewGuid(), "u", "e@e.com", true, null, false, "F", "L", "D", true, false, true, DateTimeOffset.UnixEpoch, null),
        new ClientHandshakeRequest("papertrade", "PaperTrade.Api", "1.0.0"),
        new ClientHandshakeRequest(null, "App", "1.0.0"),
        new ClientHandshakeResponse(Guid.NewGuid(), Guid.NewGuid(), null, "App", ServerMode.SingleProject, "https://auth", 3, TimeSpan.FromMinutes(30), true),
        new ProjectDto(Guid.NewGuid(), "pk", "N", null, true, 1, DateTimeOffset.UnixEpoch),
        new ApplicationDto(Guid.NewGuid(), Guid.NewGuid(), "api", "pk.api", true, ["https://a/cb"], [], ["openid"], 1, DateTimeOffset.UnixEpoch),
        new CreateApplicationRequest("api"),
        new UpdateApplicationRequest("api", true),
        new CreateProjectRequest("papertrade", "PaperTrade"),
        new UpdateProjectRequest("N", "D", false),
        new LoginRequest("sara", "pw", ["openid"]),
        new LoginResponse("a", "r", null, 900),
        new LoginResponse(null, null, null, null, true, "2ft"),
        new RegisterRequest("u", "e@e.com", null, "pw"),
        new RegisterResponse(Guid.NewGuid(), true, true, false),
        new TwoFactorRequest("t", "123456"),
        new ForgotPasswordRequest("sara"),
        new ResetPasswordRequest("sara", "tok", "new"),
        new ChangePasswordRequest("old", "new"),
        new ApiError("invalid_token", "msg", new Dictionary<string, string[]> { ["f"] = ["e"] }),
        new PagedResult<string>(["a"], 1, 10, 1),
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Serialize_RoundTrip_PreservesValue(object sample)
    {
        var type = sample.GetType();
        var json = JsonSerializer.Serialize(sample, type, Options);
        var back = JsonSerializer.Deserialize(json, type, Options);
        // Compare by re-serializing: records with lists do not have structural equality.
        JsonSerializer.Serialize(back, type, Options).ShouldBe(json);
    }

    [Fact]
    public void Serialize_Handshake_UsesStringEnum()
    {
        var r = new ClientHandshakeResponse(Guid.Empty, Guid.Empty, null, "A", ServerMode.MultiProject, "i", 1, null, false);
        JsonSerializer.Serialize(r, Options).ShouldContain("\"serverMode\":\"MultiProject\"");
    }

    [Fact]
    public void Result_SuccessAndFailure_SetFlags()
    {
        Result<int>.Success(5).IsSuccess.ShouldBeTrue();
        var f = Result<int>.Failure("x", "m");
        f.IsSuccess.ShouldBeFalse();
        f.Error!.Code.ShouldBe("x");
        Result.Success().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void PagedResult_TotalPages_RoundsUp()
    {
        new PagedResult<int>([], 1, 10, 21).TotalPages.ShouldBe(3);
        new PagedResult<int>([], 1, 0, 5).TotalPages.ShouldBe(0);
    }
}

public class EnumTests
{
    private static IEnumerable<Type> Enums() => typeof(ContractsAssemblyMarker).Assembly.GetTypes()
        .Where(t => t.IsEnum && t.Namespace == "SystemX.Auth2.Contracts.Enums");

    [Fact]
    public void Enums_AllExpectedTypesExist() => Enums().Count().ShouldBe(16);

    [Fact]
    public void Enums_EveryValue_RoundTripsAsString()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var type in Enums())
        foreach (var value in Enum.GetValues(type))
        {
            var json = JsonSerializer.Serialize(value, type, options);
            json.ShouldBe($"\"{value}\"");
            JsonSerializer.Deserialize(json, type, options).ShouldBe(value);
        }
    }

    [Fact]
    public void GroupSyncPolicy_HasPushAndBoth_ButNoneAndPullKeepTheirNumbers() { Enum.GetNames<GroupSyncPolicy>().ShouldBe(["None", "Pull", "Push", "Both"]); ((int)GroupSyncPolicy.Pull).ShouldBe(1); }

    [Fact]
    public void DefaultValues_AreSafe()
    {
        default(PermissionSyncPolicy).ShouldBe(PermissionSyncPolicy.None);
        default(UiMode).ShouldBe(UiMode.None);
    }
}

public class ConstantsTests
{
    private static IEnumerable<string> Values(Type t) => t
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!);

    [Theory]
    [InlineData(typeof(Auth2Scopes))]
    [InlineData(typeof(Auth2Claims))]
    [InlineData(typeof(Auth2ErrorCodes))]
    [InlineData(typeof(Auth2Roles))]
    public void Constants_AreUnique(Type type)
    {
        var values = Values(type).ToList();
        values.Count.ShouldBeGreaterThan(0);
        values.Distinct(StringComparer.Ordinal).Count().ShouldBe(values.Count);
    }

    [Fact]
    public void Routes_AreUniqueAndRooted()
    {
        var values = Values(typeof(Auth2Routes)).ToList();
        values.Distinct().Count().ShouldBe(values.Count);
        values.ShouldAllBe(v => v.StartsWith('/'));
    }

    [Fact]
    public void Scopes_All_ContainsEveryScopeAndStartsWithPrefix()
    {
        Auth2Scopes.All.OrderBy(x => x).ShouldBe(Values(typeof(Auth2Scopes)).OrderBy(x => x));
        Auth2Scopes.All.ShouldAllBe(s => s.StartsWith("auth2."));
    }

    [Fact]
    public void ErrorCodes_AreSnakeCase_AndAllListed()
    {
        Auth2ErrorCodes.All.OrderBy(x => x).ShouldBe(Values(typeof(Auth2ErrorCodes)).OrderBy(x => x));
        Auth2ErrorCodes.All.ShouldAllBe(c => c.All(ch => char.IsLower(ch) || ch == '_'));
    }

    [Fact]
    public void Routes_UserAccessFor_ReplacesId()
    {
        var id = Guid.NewGuid();
        Auth2Routes.UserAccessFor(id).ShouldBe($"/api/auth2/users/{id}/access");
    }
}

public class ContractsIndependenceTests
{
    [Fact]
    public void Contracts_DoesNotReferenceAspNetEfOrOpenIddict()
    {
        var refs = typeof(ContractsAssemblyMarker).Assembly.GetReferencedAssemblies().Select(a => a.Name!);
        refs.ShouldNotContain(n => n.StartsWith("Microsoft.AspNetCore") || n.StartsWith("Microsoft.EntityFrameworkCore")
            || n.StartsWith("OpenIddict"));
    }
}
