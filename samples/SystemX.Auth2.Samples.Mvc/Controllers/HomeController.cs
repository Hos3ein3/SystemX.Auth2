using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using SystemX.Auth2.Client.Authentication;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Samples.Mvc.Models;

namespace SystemX.Auth2.Samples.Mvc.Controllers;

/// <summary>
/// Normal MVC controller: demonstrates public, authenticated, role-gated, and permission-gated views, and below them one action for every kind of rule
/// of the Client package: permissions (any, all, only from a role, only direct), roles, groups, scopes, rules that combine kinds (AND by two attributes,
/// OR across kinds with the "any of" family), the audit hook, the resources of scopes in the token, and checks written in code. The Showcase page lists them;
/// docs/sample-mvc.md says who passes which one with the demo users.
/// </summary>
public class HomeController(ICurrentUser currentUser, ICurrentClient currentClient,ILogger<HomeController> logger) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {

        ViewBag.IsAuthenticated = currentUser.IsAuthenticated;
        ViewBag.UserName = currentUser.UserName;
        ViewBag.Roles = currentUser.IsAuthenticated ? await currentUser.GetRolesAsync(ct) : [];
        ViewBag.Permissions = currentUser.IsAuthenticated ? await currentUser.GetPermissionsAsync(ct) : [];
        return View();
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        ViewBag.UserName = currentUser.UserName;
        ViewBag.FullName = currentUser.FullName;
        ViewBag.Roles = await currentUser.GetRolesAsync(ct);
        ViewBag.CanReadOrders = await currentUser.HasPermissionAsync("orders.read", ct);
        ViewBag.CanWriteOrders = await currentUser.HasPermissionAsync("orders.write", ct);
        ViewBag.IsAdmin = await currentUser.HasRoleAsync("Admin", ct);
        return View();
    }

    [Auth2Permission("orders.read")]
    [HttpGet]
    public async Task<IActionResult> Orders(CancellationToken ct)
    {
        logger.LogInformation("User {UserName} accessed Orders view.", currentUser.UserName);
        ViewBag.CanWrite = await currentUser.HasPermissionAsync("orders.write", ct);
        return View();
    }

    [Auth2Role("Admin")]
    [HttpGet]
    public IActionResult AdminPanel()
    {

        logger.LogInformation("Admin user {UserName} accessed AdminPanel view.", currentUser.UserName);
        return View();
    }

    // ---------------------------------------------------------------- the showcase

    /// <summary>The list of every demo action below, with the rule of each and what the signed-in person has.</summary>
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Showcase(CancellationToken ct)
    {
        ViewBag.IsAuthenticated = currentUser.IsAuthenticated;
        ViewBag.UserName = currentUser.UserName;
        ViewBag.Roles = currentUser.IsAuthenticated ? await currentUser.GetRolesAsync(ct) : [];
        ViewBag.Groups = currentUser.IsAuthenticated ? await currentUser.GetGroupsAsync(ct) : [];
        ViewBag.Permissions = currentUser.IsAuthenticated ? await currentUser.GetPermissionsAsync(ct) : [];
        ViewBag.Scopes = currentClient.Scopes;
        return View();
    }

    private IActionResult Rule(string title, string rule, string explanation) => View("Rule", new RuleViewModel(title, rule, explanation, currentUser.UserName));

    // ---- permissions

    /// <summary>One permission.</summary>
    [Auth2Permission("orders.write")]
    [HttpGet]
    public IActionResult OrdersWrite() => Rule("Write orders", """[Auth2Permission("orders.write")]""", "You have the permission orders.write: given to you directly, by one of your roles, or by one of your groups.");

    /// <summary>Several permissions in one attribute: any of them.</summary>
    [Auth2Permission("orders.write", "orders.delete")]
    [HttpGet]
    public IActionResult OrdersWriteOrDelete() => Rule("Write or delete orders", """[Auth2Permission("orders.write", "orders.delete")]""", "You have at least ONE of the permissions orders.write and orders.delete (several keys in one attribute mean any).");

    /// <summary>Several permissions, all of them.</summary>
    [Auth2Permission(true, "orders.read", "orders.write", "orders.delete")]
    [HttpGet]
    public IActionResult OrdersFullControl() => Rule("Full control of orders", """[Auth2Permission(true, "orders.read", "orders.write", "orders.delete")]""", "You have ALL three permissions: the first argument true means all.");

    /// <summary>The permission must come from a role (or the role of a group).</summary>
    [Auth2Permission("orders.read")]
    [Auth2OnlyRolePermission]
    [HttpGet]
    public IActionResult OrdersThroughARole() => Rule("Orders, only through a role", """[Auth2Permission("orders.read")] [Auth2OnlyRolePermission]""", "You have orders.read, and it comes from a role (yours, or the role of one of your groups), not from a direct grant and not from a permission of a group.");

    /// <summary>The permission must be a direct grant to the person.</summary>
    [Auth2Permission("orders.read")]
    [Auth2OnlyUserPermission]
    [HttpGet]
    public IActionResult OrdersGrantedToYou() => Rule("Orders, only granted to you", """[Auth2Permission("orders.read")] [Auth2OnlyUserPermission]""", "You have orders.read as a direct grant to you. A role or a group is not enough here.");

    /// <summary>A permission and the audit hook: every try, allowed or refused, is written to the audit sink.</summary>
    [Auth2Permission("orders.export")]
    [Auth2Audit("orders.export")]
    [HttpGet]
    public IActionResult OrdersExport()
    {
        logger.LogInformation("{UserName} exported the orders.", currentUser.UserName);
        return Rule("Export orders", """[Auth2Permission("orders.export")] [Auth2Audit("orders.export")]""", "You have orders.export. Allowed or refused, this call is reported to IAuth2AuditSink (the log, by default): look for \"Auth2 audit orders.export\".");
    }

    // ---- roles

    /// <summary>Several roles: any of them.</summary>
    [Auth2Role("Manager", "Admin")]
    [HttpGet]
    public IActionResult ManagersArea() => Rule("Managers area", """[Auth2Role("Manager", "Admin")]""", "You have the role Manager OR the role Admin (the role check ignores case; the roles are in your token).");

    /// <summary>Several roles: all of them.</summary>
    [Auth2Role(true, "Admin", "Manager")]
    [HttpGet]
    public IActionResult AdminAndManager() => Rule("Admin and Manager", """[Auth2Role(true, "Admin", "Manager")]""", "You have BOTH roles. With the demo users nobody has both, so you are always refused: it shows what \"all\" means.");

    // ---- groups

    /// <summary>A group.</summary>
    [Auth2Group("finance")]
    [HttpGet]
    public IActionResult FinanceTeam() => Rule("Finance team", """[Auth2Group("finance")]""", "You are a member of the group finance. The group, and not you, carries the roles and permissions that come with it.");

    /// <summary>Several groups: any of them.</summary>
    [Auth2Group("finance-eu", "finance-us")]
    [HttpGet]
    public IActionResult FinanceRegions() => Rule("Finance, Europe or US", """[Auth2Group("finance-eu", "finance-us")]""", "You are in the group finance-eu OR finance-us (groups of the demo data: william.hernandez, benjamin.gonzalez, logan.wright).");

    /// <summary>All of several groups (the "all" form of the group rule).</summary>
    [Auth2Group(true, "finance", "finance-eu")]
    [HttpGet]
    public IActionResult FinanceEurope() => Rule("Finance AND Europe", """[Auth2Group(true, "finance", "finance-eu")]""", "You are in the group finance AND in the group finance-eu. One of the two is not enough.");

    // ---- how the person signed in (the amr claim of the token)

    /// <summary>Signed in with a password (a passkey sign-in has no <c>pwd</c>).</summary>
    [Auth2SignInMethod("pwd")]
    [HttpGet]
    public IActionResult SignedInWithPassword() => Rule("Signed in with a password", """[Auth2SignInMethod("pwd")]""", "Your sign-in used a password (amr has pwd). If you signed in with a passkey you get 403 here.");

    /// <summary>More than one factor: a verified passkey, or a password plus a one-time code.</summary>
    [Auth2MultiFactor]
    [HttpGet]
    public IActionResult MultiFactorOnly() => Rule("More than one factor", "[Auth2MultiFactor]", "Your sign-in used more than one factor: a passkey unlocked with a fingerprint, face or PIN, or a password plus a one-time code (two-factor sign-in). A password alone gets 403 here. Look at the amr values on the Inspect page.");

    // ---- hosted UI: the Auth2 server asks for the code on its own page

    /// <summary>
    /// <b>The hosted UI asks for the code on the Server's page.</b> This application signs in with <c>UseHostedUi()</c>, so a person who reaches a <c>[Auth2Totp]</c> action without a fresh
    /// code is sent to the Auth2 server (<c>/connect/authorize</c> with the step-up value, then the page <c>/Account/VerifyTotp</c>) and comes back here with the proof in the cookie
    /// (claim <c>step_up_at</c>). A person who has not turned on two-factor sees the server page that tells how to turn it on. Nothing in this action handles a code.
    /// </summary>
    [Auth2Totp]
    [HttpGet]
    public IActionResult SecureArea() => Rule("A secure area (a code of the authenticator app)", "[Auth2Totp]",
        "You are here because the Auth2 server checked a code of your authenticator app a moment ago (the claim step_up_at in your session). For the next minutes you are not asked again; after that the browser goes to the server's page once more. Try it with a person who has not turned on two-factor: the server page explains how to turn it on.");

    /// <summary>A page with a form. The page itself is open to everybody who is signed in; the <b>post</b> is the protected action (<see cref="Transfer"/>).</summary>
    [Authorize]
    [HttpGet]
    public IActionResult TransferForm() => View("TransferForm");

    /// <summary>
    /// A <b>POST</b> that needs the code: the first time the browser is sent to the Auth2 server and comes back to <see cref="TransferForm"/> (a form cannot be sent again from a redirect),
    /// where you press the button again; now the proof is in the session and the post goes through.
    /// </summary>
    [Auth2Totp]
    [HttpPost]
    public IActionResult Transfer(decimal amount) => Rule($"Transfer of {amount:0.##} sent", "[Auth2Totp]", "The post went through because your session carries a fresh proof of the authenticator code.");

    /// <summary>The form that asks for a code of the authenticator app, then posts it to <see cref="TotpAction"/>.</summary>
    [HttpGet]
    public IActionResult TotpForm() => View("TotpForm");

    /// <summary>
    /// An action that needs a <b>fresh code of the authenticator app</b> every time (<c>[Auth2Totp]</c>): the person must have turned on two-factor in
    /// <c>/Manage/TwoFactor/Enable</c> of the Auth2 server. The form posts the code in the field <c>auth2_totp</c>; an API sends the header <c>X-Auth2-Totp</c>.
    /// Without a code or with a wrong one the answer is 403 and the header <c>X-Auth2-Totp-Status</c> says why. A code works once.
    /// </summary>
    [Auth2Totp]
    [HttpPost]
    public IActionResult TotpAction() => Rule("Needs a code of the authenticator app", "[Auth2Totp]", "The Server checked the code of your authenticator app just now. A code works once: to run this again you need the next code (about 30 seconds later).");

    /// <summary>
    /// The same, but the code is <b>optional</b> for people who have not turned on two-factor (<c>[Auth2OptionalTotp]</c>): they pass without a code; a person who has two-factor must
    /// give one. Compare with <see cref="TotpAction"/>, where a person without two-factor is refused with <c>totp_setup_required</c>.
    /// </summary>
    [Auth2OptionalTotp]
    [HttpPost]
    public IActionResult OptionalTotpAction() => Rule("A code only if you use two-factor", "[Auth2OptionalTotp]", "If you have two-factor turned on, the Server checked the code of your authenticator app just now. If you have not, there was nothing to check and you came in without a code.");

    // ---- scopes (what the CLIENT may do)

    /// <summary>A scope of the token.</summary>
    [Auth2Scope("reports.read")]
    [HttpGet]
    public IActionResult ReportsScope() => Rule("Reports scope", """[Auth2Scope("reports.read")]""", "The token of this application has the scope reports.read. A scope belongs to the APPLICATION: everybody who signs in here has it, whatever their roles are. This page checks the client, not you.");

    /// <summary>Several scopes: any of them.</summary>
    [Auth2Scope("orders.read", "orders.write")]
    [HttpGet]
    public IActionResult OrdersScope() => Rule("Orders scopes", """[Auth2Scope("orders.read", "orders.write")]""", "The token has the scope orders.read OR orders.write.");

    /// <summary>
    /// A scope this application does NOT have (<c>payments.refund</c> is not registered, so no token of this application can carry it). The rule fails for everybody, whatever
    /// their roles or permissions: open <c>/Home/ScopeFails</c> after signing in and you get <b>403 Forbidden</b> (the access-denied page of the Client package).
    /// </summary>
    [Auth2Scope("payments.refund")]
    [HttpGet]
    public IActionResult ScopeFails() => Rule("Scope that fails", """[Auth2Scope("payments.refund")]""", "You should never see this page: the token of this application does not have the scope payments.refund, so the rule refuses the request with 403.");

    /// <summary>
    /// The same failure written in code: the page decides itself and answers a JSON error, like an API would. <c>/Home/ScopeFailsInCode</c> returns 403 with the scope that is missing
    /// and the scopes the token really has.
    /// </summary>
    [HttpGet]
    [Auth2Scope("scope.test")]
    public IActionResult ScopeFailsInCode()
    {
        const string required = "scope.test";
        if (currentClient.HasScope(required)) return Content("The token has the scope " + required);
        return StatusCode(StatusCodes.Status403Forbidden, new { error = "insufficient_scope", required, granted = currentClient.Scopes });
    }
    /// <summary>
    /// The same failure written in code: the page decides itself and answers a JSON error, like an API would. <c>/Home/ScopeFailsInCode</c> returns 403 with the scope that is missing
    /// and the scopes the token really has.
    /// </summary>
    [HttpGet]
    [Auth2Scope("scope.test2")]
    public IActionResult ScopeFailsInCode2()
    {
        const string required = "scope.test2";
        if (currentClient.HasScope(required)) return Content("The token has the scope " + required);
        return StatusCode(StatusCodes.Status403Forbidden, new { error = "insufficient_scope", required, granted = currentClient.Scopes });
    }

    /// <summary>A scope AND a permission: two attributes are two rules, both must pass.</summary>
    [Auth2Scope("reports.read")]
    [Auth2Permission("reports.read")]
    [HttpGet]
    public IActionResult ReportsScopeAndPermission() => Rule("Reports: scope and permission", """[Auth2Scope("reports.read")] [Auth2Permission("reports.read")]""", "Two rules, both must pass: the TOKEN has the scope (the application may use the reports API) AND YOU have the permission (you may read reports). Chaining is AND.");

    /// <summary>Only people (a person's token, not a program's).</summary>
    [Auth2UserOnly]
    [HttpGet]
    public IActionResult PeopleOnly() => Rule("People only", "[Auth2UserOnly]", "The token belongs to a person. A program (client credentials) would be refused here; [Auth2MachineOnly] is the opposite.");

    // ---- OR across kinds: the "any of" family

    /// <summary>A role or a permission.</summary>
    [Auth2RoleOrPermission(Roles = new[] { "Admin" }, Permissions = new[] { "orders.delete" })]
    [HttpGet]
    public IActionResult DeleteOrders() => Rule("Delete orders", """[Auth2RoleOrPermission(Roles = new[] { "Admin" }, Permissions = new[] { "orders.delete" })]""", "You are an Admin OR you have orders.delete: one rule, two kinds. Chaining two attributes could not say OR.");

    /// <summary>A role or a group.</summary>
    [Auth2RoleOrGroup(Roles = new[] { "Manager" }, Groups = new[] { "finance" })]
    [HttpGet]
    public IActionResult BudgetReview() => Rule("Budget review", """[Auth2RoleOrGroup(Roles = new[] { "Manager" }, Groups = new[] { "finance" })]""", "You have the role Manager OR you are in the group finance.");

    /// <summary>A permission or a group.</summary>
    [Auth2PermissionOrGroup(Permissions = new[] { "orders.approve" }, Groups = new[] { "finance" })]
    [HttpGet]
    public IActionResult ApproveOrders() => Rule("Approve orders", """[Auth2PermissionOrGroup(Permissions = new[] { "orders.approve" }, Groups = new[] { "finance" })]""", "You have orders.approve OR you are in the group finance.");

    /// <summary>A scope or a role.</summary>
    [Auth2ScopeOrRole(Scopes = new[] { "reports.read" }, Roles = new[] { "Admin" })]
    [HttpGet]
    public IActionResult ReportsForProgramsOrAdmins() => Rule("Reports for programs or admins", """[Auth2ScopeOrRole(Scopes = new[] { "reports.read" }, Roles = new[] { "Admin" })]""", "The token has the scope reports.read OR you are an Admin. In this application every token has the scope, so everybody passes; a program with the scope would pass too, with no person at all.");

    /// <summary>All four kinds in one rule.</summary>
    [Auth2AnyOf(Roles = new[] { "Admin" }, Permissions = new[] { "orders.delete" }, Groups = new[] { "support" }, Scopes = new[] { "orders.write" })]
    [HttpGet]
    public IActionResult AnythingGoes() => Rule("Any of four kinds", """[Auth2AnyOf(Roles = ..., Permissions = ..., Groups = ..., Scopes = ...)]""", "One match of any of the four kinds is enough: the role Admin, the permission orders.delete, the group support, or the scope orders.write.");

    // ---- in code, and the resources of scopes

    /// <summary>The same checks written in code with ICurrentUser and ICurrentClient, for a screen that must show or hide things instead of refusing the page.</summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Inspect(CancellationToken ct)
    {
        var checks = new List<(string Check, bool Result)>
        {
            ("HasPermissionAsync(\"orders.write\")", await currentUser.HasPermissionAsync("orders.write", ct)),
            ("HasPermissionAsync(\"orders.delete\")", await currentUser.HasPermissionAsync("orders.delete", ct)),
            ("HasRoleAsync(\"Admin\")", await currentUser.HasRoleAsync("Admin", ct)),
            ("HasRoleAsync(\"Manager\")", await currentUser.HasRoleAsync("Manager", ct)),
            ("groups contain \"finance\"", (await currentUser.GetGroupsAsync(ct)).Contains("finance")),
            ("HasScope(\"reports.read\")", currentClient.HasScope("reports.read")),
            ("HasScope(\"api.reports.read\")", currentClient.HasScope("api.reports.read")),
            ("IsMachine", currentClient.IsMachine),
        };

        // The resources of the scopes are the audiences of the access token: this is the token the cookie keeps.
        string[] audiences = [];
        var token = await HttpContext.GetTokenAsync(Auth2AuthSchemes.Cookie, "access_token");
        if (!string.IsNullOrEmpty(token)) audiences = [.. new JsonWebToken(token).Audiences];

        return View(new InspectViewModel(currentUser.UserName, currentClient.ClientId, [.. await currentUser.GetRolesAsync(ct)], [.. await currentUser.GetGroupsAsync(ct)],
            [.. await currentUser.GetPermissionsAsync(ct)], [.. currentClient.Scopes], audiences, [.. currentClient.Amr], checks));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [Auth2Authorize]
    public async Task<IActionResult> AfterLoginRegisterRedirect(
        [FromServices] IAuth2ApiClientService auth,
        CancellationToken ct)
    {
        var me = await auth.GetMeAsync(ct);                      // GET /api/auth2/me with the token the cookie keeps (invalid_token when nobody is signed in)
        if (!me.IsSuccess)
        {
            // me.Error.Code, for example "invalid_token" (the token expired) or "server_unavailable"
            return me.Error!.Code == "invalid_token" ? Challenge() : StatusCode(502);
        }

        var user = me.Value!;                                    // CurrentUserInfo
        ViewBag.UserName = user.UserName;
        ViewBag.Roles = user.Roles;
        ViewBag.Permissions = user.Permissions;
        ViewBag.Groups = user.Groups;
        return View();
    }


}
