using SystemX.Auth2.Contracts.Admin;
using SystemX.Auth2.Contracts.Applications;

namespace SystemX.Auth2.Server.Application;

/// <summary>Summary metrics for the project dashboard.</summary>
public sealed record AdminDashboardMetrics(
    int TotalUsers,
    int ActiveSessions,
    int Logins24h,
    int FailedLogins24h,
    int Lockouts24h,
    int Registrations24h,
    int Logins7d,
    int FailedLogins7d,
    IReadOnlyList<AuditEventDto> RecentAuditEvents,
    IReadOnlyList<ApplicationDto> Applications,
    IReadOnlyList<ProviderStatusDto> Providers,
    IReadOnlyList<ExpiringItemDto> ExpiringItems,
    JobHealthStatus? JobHealth);

/// <summary>Provider status item on the dashboard.</summary>
public sealed record ProviderStatusDto(string Name, string Type, bool IsEnabled);

/// <summary>Expiring key or certificate warning item.</summary>
public sealed record ExpiringItemDto(string Name, string Kind, DateTimeOffset ExpiresAtUtc, int DaysRemaining);

/// <summary>Job health summary for GlobalAdmin dashboard.</summary>
public sealed record JobHealthStatus(bool IsHealthy, int FailedCount, DateTimeOffset? LastFailureUtc);

/// <summary>Use case for generating the admin dashboard metrics.</summary>
public interface IAdminDashboardService
{
    /// <summary>Computes or loads cached dashboard metrics for a project.</summary>
    Task<AdminDashboardMetrics> GetMetricsAsync(Guid projectId, CancellationToken ct = default);
}

/// <summary>A row in an administrative report.</summary>
public sealed record ReportRow(string Label, long Count, DateTimeOffset? Timestamp = null, string? Category = null);

/// <summary>Use case for generating administrative reports.</summary>
public interface IAdminReportService
{
    /// <summary>Executes a standard report query.</summary>
    Task<IReadOnlyList<ReportRow>> GetReportDataAsync(Guid projectId, string reportType, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Exports report data as CSV bytes.</summary>
    Task<byte[]> ExportCsvAsync(Guid projectId, string reportType, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}
