using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Server.Application;

namespace SystemX.Auth2.Server.Pages.Admin.Reports;

public sealed class IndexModel : AdminPageModel
{
    public IReadOnlyList<ReportRow> ReportData { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string ReportType { get; set; } = "logins_by_method";
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 30;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var reportSvc = Svc<IAdminReportService>();
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-Days);

        ReportData = await reportSvc.GetReportDataAsync(CurrentProject!.Id, ReportType, from, to, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostExportCsvAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var reportSvc = Svc<IAdminReportService>();
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-Days);

        var bytes = await reportSvc.ExportCsvAsync(CurrentProject!.Id, ReportType, from, to, HttpContext.RequestAborted);
        var filename = $"{ProjectKey}-{ReportType}-{DateTime.UtcNow:yyyyMMdd}.csv";

        return File(bytes, "text/csv", filename);
    }
}
