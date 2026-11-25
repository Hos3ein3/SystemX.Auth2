using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Pages.Admin.Settings;

public sealed class MessagingModel : AdminPageModel
{
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

        var messagingSvc = Svc<IAdminMessagingService>();
        Messaging = await messagingSvc.GetProjectAsync(CurrentProject!.Id, HttpContext.RequestAborted);
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
            TempData["Error"] = "Only Global Administrators or Developers can modify delivery credentials and messaging providers.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        var messagingSvc = Svc<IAdminMessagingService>();
        var current = await messagingSvc.GetProjectAsync(CurrentProject!.Id, HttpContext.RequestAborted);

        // Determine password update logic:
        // - ClearPassword == true -> "" (clears password)
        // - Non-empty Input.Password -> Input.Password.Trim() (updates password)
        // - Empty Input.Password and PasswordIsSet == true -> null (keeps existing saved password)
        // - Otherwise -> null
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

        var result = await messagingSvc.UpdateProjectAsync(CurrentProject!.Id, update, actorUserId, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            var msg = result.Errors?.Count > 0
                ? string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
                : (result.ErrorCode ?? "Failed to save messaging settings.");
            TempData["Error"] = msg;
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        TempData["Success"] = "Email and SMS delivery settings saved successfully.";
        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostTestEmailAsync([FromForm] string testEmail)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        if (string.IsNullOrWhiteSpace(testEmail))
        {
            TempData["Error"] = "Please provide a valid destination email address for testing.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        try
        {
            var sender = Svc<IEmailSender>();
            var subject = $"SystemX Auth2 Test Email - {CurrentProject?.Name ?? ProjectKey}";
            var body = $"This is a live test email sent from the SystemX Auth2 administration console at {DateTimeOffset.UtcNow:u}.\n\nIf you received this message, your email delivery provider configuration is working correctly.";
            await sender.SendAsync(new EmailMessage(testEmail.Trim(), subject, body), HttpContext.RequestAborted);
            TempData["Success"] = $"Test email sent successfully to {testEmail.Trim()}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to send test email: {ex.Message}";
        }

        return RedirectToPage(new { projectKey = ProjectKey });
    }

    public async Task<IActionResult> OnPostTestSmsAsync([FromForm] string testPhone)
    {
        if (!await AuthorizeAdminAsync()) return Page();

        if (string.IsNullOrWhiteSpace(testPhone))
        {
            TempData["Error"] = "Please provide a valid destination phone number for testing.";
            return RedirectToPage(new { projectKey = ProjectKey });
        }

        try
        {
            var sender = Svc<ISmsSender>();
            var message = $"Auth2 test SMS for {CurrentProject?.Name ?? ProjectKey} at {DateTimeOffset.UtcNow:u}.";
            await sender.SendAsync(new SmsMessage(testPhone.Trim(), message), HttpContext.RequestAborted);
            TempData["Success"] = $"Test SMS sent successfully to {testPhone.Trim()}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to send test SMS: {ex.Message}";
        }

        return RedirectToPage(new { projectKey = ProjectKey });
    }
}
