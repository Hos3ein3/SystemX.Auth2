using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;
using SystemX.Auth2.Samples.Mvc.Models;

namespace SystemX.Auth2.Samples.Mvc.Controllers;

/// <summary>
/// Normal MVC controller: demonstrates public, authenticated, role-gated, and permission-gated views.
/// </summary>
public class HomeController(ICurrentUser currentUser, ILogger<HomeController> logger) : Controller
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

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [Auth2Role("Oskol")]
    public IActionResult WithRole()
    {
        return View();
    }
    [Auth2Role("Oskol2")]
    public IActionResult WithRole2()
    {
        return View();
    }
}
