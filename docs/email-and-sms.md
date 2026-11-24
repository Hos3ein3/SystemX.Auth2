# Email and SMS

## What this is

The server sends emails (confirm email, password reset, one-time codes) and SMS (one-time codes). **How** it sends them is a setting, because one server has many projects and many applications, and each may have its own mail account.

Two factories choose the provider for every message: `IEmailFactory` and `ISmsFactory`. The default `IEmailSender` and `ISmsSender` ask them, so all the server code that sends a message uses them.

Providers that ship with the server:

| Channel | Name | What it does |
|---|---|---|
| Email | `logging` | Writes a log line with a masked address. Nothing leaves the server. This is the default. |
| Email | `smtp` | Any SMTP server (MailKit). |
| Email | `google` | Gmail / Google Workspace (`smtp.gmail.com`, port 587, STARTTLS). |
| SMS | `logging` | Writes a log line. Nothing leaves the server. |

## When to use it

Use `logging` while you develop. Switch to `smtp` or `google` before real users register.

## How to use

### Which provider counts

The first level that names a provider wins:

1. the **application** (its own messaging settings),
2. the **project**,
3. the **server default** (`Auth2:Messaging` in the configuration),
4. `logging`.

An empty provider name means "use the next level". So you can set one mail account for a whole project and give only one application its own.

### Send through Google (Gmail)

1. In the Google account turn on **2-step verification**, then create an **app password** (Google account, Security, App passwords). A normal password does not work.
2. As a **global admin** (the developer) set the project:

```http
PUT /admin/projects/{projectId}/messaging
{ "email": { "provider": "google", "userName": "mail@yourcompany.com", "password": "abcd efgh ijkl mnop", "fromName": "PaperTrade" } }
```

The From address is the Gmail address unless you set `fromAddress` (Google only accepts an alias you configured in Gmail). The password is encrypted with the server's Data Protection keys. It is never returned: reading the settings shows only `passwordIsSet`.

### Send through another SMTP server

```http
PUT /admin/projects/{projectId}/messaging
{ "email": { "provider": "smtp", "host": "smtp.example.com", "port": 587, "security": "StartTls",
             "userName": "apikey", "password": "…", "fromAddress": "noreply@example.com", "fromName": "PaperTrade" } }
```

`security` is `StartTls` (port 587), `SslOnConnect` (port 465) or `None` (a trusted local relay only).

### One application with its own account

```http
PUT    /admin/projects/{projectId}/applications/{applicationId}/messaging   (same body)
GET    /admin/projects/{projectId}/applications/{applicationId}/messaging   ("inherited": true when it has none)
DELETE /admin/projects/{projectId}/applications/{applicationId}/messaging   (back to the project)
```

### Change one field

Fields you leave out keep their value. `password`: leave it out to keep the saved password, send `""` to remove it, send a text to replace it.

### The server default

For one mail account for the whole server (for example in Docker), use the configuration. Keep the password in an environment variable or a secret store, never in a file in the repository:

```json
"Auth2": { "Messaging": { "Email": { "Provider": "google", "UserName": "mail@yourcompany.com", "FromName": "My Auth" } } }
```
```bash
Auth2__Messaging__Email__Password="abcd efgh ijkl mnop"
```

### Who may change it

Only a **global admin**. A project admin gets `403`. The settings are `GlobalOnly` because they hold a password and decide where user data (email addresses, codes, links) is sent. The generic settings routes never change them.

## How to implement / extend

### Your own email provider (for example a mail API)

```csharp
public sealed class SendGridTransport : IEmailTransport
{
    public string Name => "sendgrid";
    public Task SendAsync(EmailTransportConfig config, EmailMessage message, CancellationToken ct = default) { /* call the API */ }
}
// Program.cs
builder.Services.AddScoped<IEmailTransport, SendGridTransport>();
```

An admin then selects it with `"provider": "sendgrid"`. `EmailTransportConfig` carries the decrypted fields of the settings (host, user, password, from). SMS works the same with `ISmsTransport` and `SmsTransportConfig`. A provider name that is not registered is refused when it is saved.

### Replace everything

Register your own `IEmailSender` (or `ISmsSender`) before the server: `TryAdd` leaves yours in place and the factories are not used.

### Where the code is

| Part | Place |
|---|---|
| Settings (`MessagingSettings`, project and application) | `Domain/Settings/MessagingSettings.cs` |
| Which provider counts | `EffectiveSettings.EmailProvider`, `SmsProvider` |
| Factories | `Infrastructure/Messaging/MessagingFactories.cs` |
| SMTP and Google | `Infrastructure/Messaging/SmtpEmailTransports.cs` |
| Admin routes | `Endpoints/AdminEndpoints.cs`, `Infrastructure/Admin/AdminMessagingService.cs` |

## Risks

- The SMTP host is chosen by a global admin. It is not blocked from private addresses, because a local relay is a normal choice. Do not give the global admin role to people you do not trust.
- A failed send throws, and the request that sent it fails. The log has the error type, the masked address and the host, never the message text, the link, the code or the password.
- `security: None` sends mail and the SMTP password in clear text. Use it only on a trusted network.

## Common problems

| Problem | Cause |
|---|---|
| `535 Username and Password not accepted` (Google) | A normal password was used. Create an app password (2-step verification must be on). |
| `403` on the messaging route | The caller is a project admin. Only a global admin may change it. |
| `400` `Unknown provider` | The name is not registered. The answer lists the known names. |
| Nothing arrives and the log says `Email to s***@…` | The provider is still `logging` (nothing is set at any level). |

## Related pages

- [server-settings.md](server-settings.md)
- [admin-api.md](admin-api.md)
- [security.md](security.md)
- [decisions.md](decisions.md)
