using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Client.Authorization;
using SystemX.Auth2.Client.CurrentUser;

namespace SystemX.Auth2.Samples.Mvc.Controllers.Api;

/// <summary>
/// Normal API controller: demonstrates public, protected, permission-gated, role-gated, scope-gated, and token-kind endpoints.
/// </summary>
[ApiController]
[Route("api/data")]
public class DataApiController(ICurrentUser currentUser, ILogger<DataApiController> logger) : ControllerBase
{
    private static readonly List<OrderDto> SampleOrders =
    [
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "ORD-1001", "Widget A", 19.99m, "Shipped"),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "ORD-1002", "Gadget B", 49.50m, "Processing"),
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "ORD-1003", "Component C", 120.00m, "Delivered")
    ];

    /// <summary>
    /// Public data endpoint accessible without authentication.
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    public IActionResult GetPublicData()
    {
        return Ok(new
        {
            Message = "This is public data accessible to anyone without authentication.",
            TimestampUtc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Protected endpoint requiring any authenticated user or token.
    /// </summary>
    [HttpGet("protected")]
    [Auth2Authorize]
    public IActionResult GetProtectedData()
    {
        return Ok(new
        {
            Message = "This is protected data. You are authenticated!",
            currentUser.UserId,
            currentUser.UserName,
            TimestampUtc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Permission-gated endpoint requiring the 'orders.read' permission.
    /// </summary>
    [HttpGet("orders")]
    [Auth2Permission("orders.read")]
    [Auth2Audit("orders.read")]
    public IActionResult GetOrders()
    {
        logger.LogInformation("Orders fetched by user {UserName}", currentUser.UserName);
        return Ok(SampleOrders);
    }

    /// <summary>
    /// Permission-gated endpoint requiring the 'orders.write' permission.
    /// </summary>
    [HttpPost("orders")]
    [Auth2Permission("orders.write")]
    [Auth2Audit("orders.create")]
    public IActionResult CreateOrder([FromBody] CreateOrderRequest request)
    {
        var newOrder = new OrderDto(Guid.NewGuid(), $"ORD-{Random.Shared.Next(1004, 9999)}", request.Item, request.Amount, "Created");
        SampleOrders.Add(newOrder);
        logger.LogInformation("Order {OrderNumber} created by user {UserName}", newOrder.OrderNumber, currentUser.UserName);
        return CreatedAtAction(nameof(GetOrders), new { id = newOrder.Id }, newOrder);
    }

    /// <summary>
    /// Scope-gated endpoint requiring the 'reports.read' scope in the token.
    /// </summary>
    [HttpGet("reports")]
    [Auth2Scope("reports.read")]
    public IActionResult GetReports()
    {
        return Ok(new
        {
            ReportName = "Annual Sales Report",
            TotalOrders = SampleOrders.Count,
            TotalRevenue = SampleOrders.Sum(o => o.Amount),
            GeneratedAtUtc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Endpoint accessible only with machine-to-machine client credentials token.
    /// </summary>
    [HttpGet("machine-only")]
    [Auth2MachineOnly]
    public IActionResult GetMachineOnlyData()
    {
        return Ok(new
        {
            Message = "This endpoint is restricted strictly to Machine (Client Credentials) tokens.",
            TimestampUtc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Endpoint accessible only with user (person) tokens.
    /// </summary>
    [HttpGet("user-only")]
    [Auth2UserOnly]
    public IActionResult GetUserOnlyData()
    {
        return Ok(new
        {
            Message = "This endpoint is restricted strictly to User (Person) tokens.",
            currentUser.UserName,
            currentUser.Email,
            TimestampUtc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Role-gated endpoint requiring the 'Admin' role.
    /// </summary>
    [HttpGet("admin-only")]
    [Auth2Role("Admin")]
    public IActionResult GetAdminData()
    {
        return Ok(new
        {
            Message = "This endpoint requires the 'Admin' role.",
            currentUser.UserName,
            TimestampUtc = DateTimeOffset.UtcNow
        });
    }
}

public sealed record OrderDto(Guid Id, string OrderNumber, string Item, decimal Amount, string Status);
public sealed record CreateOrderRequest(string Item, decimal Amount);
