using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Pages.Admin.Applications;

public sealed class MessagingModel : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public ApplicationDto? App { get; private set; }
    public MessagingDto? Messaging { get; private set; }
    public IReadOnlyCollection<string> AvailableEmailProviders { get; private set; } = [];
    public IReadOnlyCollection<string> AvailableSmsProviders { get; private set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        // Email Settings
        public string Provider { get; set; } = EmailProviders.Inherit;
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public bool ClearPassword { get; set; }
        public string? FromAddress { get; set; }
        public string? FromName { get; set; }
        public int TimeoutSeconds { get; set; } = 15;

        // SMS Settings
        public string SmsProvider { get; set; } = SmsProviders.Inherit;
        public string? Sender { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        var appSvc = Svc<IAdminApplicationService>();
        App = await appSvc.GetAsync(CurrentProject!.Id, Id, HttpContext.RequestAborted);
        if (App is null) return NotFound();

        var messagingSvc = Svc<IAdminMessagingService>();
        Messaging = await messagingSvc.GetApplicationAsync(CurrentProject!.Id, Id, HttpContext.RequestAborted);
        if (Messaging is not null)
        {
            AvailableEmailProviders = Messaging.EmailProviders;
            AvailableSmsProviders = Messaging.SmsProviders;

            Input = new InputModel
            {
                Provider = Messaging.Email.Provider,
                Host = Messaging.Email.Host,
                Port = Messaging.Email.Port,
                Security = Messaging.Email.Security,
                UserName = Messaging.Email.UserName,
                FromAddress = Messaging.Email.FromAddress,
                FromName = Messaging.Email.FromName,
                TimeoutSeconds = Messaging.Email.TimeoutSeconds,
                SmsProvider = Messaging.Sms.Provider,
                Sender = Messaging.Sms.Sender
            };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        if (!IsGlobalAdmin)
        {
            TempData["Error"] = "Only Global Administrators or Developers can modify delivery credentials.";
            return RedirectToPage(new { projectKey = ProjectKey, id = Id });
        }

        var appSvc = Svc<IAdminApplicationService>();
        App = await appSvc.GetAsync(CurrentProject!.Id, Id, HttpContext.RequestAborted);
        if (App is null) return NotFound();

        var messagingSvc = Svc<IAdminMessagingService>();

        string? passwordToSet = null;
        if (Input.ClearPassword)
        {
            passwordToSet = "";
        }
        else if (!string.IsNullOrWhiteSpace(Input.Password))
        {
            passwordToSet = Input.Password.Trim();
        }

        var emailUpdate = new EmailProviderUpdate(
            Provider: Input.Provider,
            Host: Input.Host,
            Port: Input.Port,
            Security: Input.Security,
            UserName: Input.UserName,
            Password: passwordToSet,
            FromAddress: Input.FromAddress,
            FromName: Input.FromName,
            TimeoutSeconds: Input.TimeoutSeconds);

        var smsUpdate = new SmsProviderSettings
        {
            Provider = (Input.SmsProvider ?? "").Trim().ToLowerInvariant(),
            Sender = string.IsNullOrWhiteSpace(Input.Sender) ? null : Input.Sender.Trim()
        };

        var update = new MessagingUpdate(emailUpdate, smsUpdate);
        var actorUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var uid) ? uid : (Guid?)null;

        var result = await messagingSvc.UpdateApplicationAsync(CurrentProject!.Id, Id, update, actorUserId, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            var msg = result.Errors?.Count > 0
                ? string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
                : (result.ErrorCode ?? "Failed to save application messaging settings.");
            TempData["Error"] = msg;
            return RedirectToPage(new { projectKey = ProjectKey, id = Id });
        }

        TempData["Success"] = "Application messaging delivery settings saved successfully.";
        return RedirectToPage(new { projectKey = ProjectKey, id = Id });
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        if (!await AuthorizeAdminAsync()) return Page();

        if (!IsGlobalAdmin)
        {
            TempData["Error"] = "Only Global Administrators or Developers can clear delivery credentials.";
            return RedirectToPage(new { projectKey = ProjectKey, id = Id });
        }

        var appSvc = Svc<IAdminApplicationService>();
        App = await appSvc.GetAsync(CurrentProject!.Id, Id, HttpContext.RequestAborted);
        if (App is null) return NotFound();

        var messagingSvc = Svc<IAdminMessagingService>();
        var actorUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var uid) ? uid : (Guid?)null;

        var result = await messagingSvc.ClearApplicationAsync(CurrentProject!.Id, Id, actorUserId, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            TempData["Error"] = "Failed to reset application messaging settings.";
            return RedirectToPage(new { projectKey = ProjectKey, id = Id });
        }

        TempData["Success"] = "Application messaging settings reset. Application now inherits project messaging settings.";
        return RedirectToPage(new { projectKey = ProjectKey, id = Id });
    }
}
