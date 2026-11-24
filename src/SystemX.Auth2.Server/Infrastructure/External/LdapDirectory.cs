using System.Security.Cryptography.X509Certificates;
using System.Text;
using Novell.Directory.Ldap;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Infrastructure.External;

/// <summary>A failed directory sign-in. <see cref="Subject"/> is set when the user entry was found (so a failed password can count toward the local lockout).</summary>
public sealed class LdapAuthException(string errorCode, string reason, string? subject = null, Exception? inner = null) : ExternalLoginException(errorCode, reason, inner)
{
    /// <summary>The stable id of the entry that was found, or null.</summary>
    public string? Subject { get; } = subject;
}

/// <summary>What a lookup of a user returns (the admin "test user lookup" shows it). Never contains a password.</summary>
public sealed record LdapLookupResult(string Dn, string Subject, IReadOnlyDictionary<string, string[]> Attributes, IReadOnlyList<string> Groups);

/// <summary>Talks to a directory (LDAP or Active Directory).</summary>
public interface ILdapDirectory
{
    /// <summary>Finds the user, checks the password with a bind as the user, reads attributes and groups. Throws <see cref="LdapAuthException"/>.</summary>
    Task<ExternalIdentity> AuthenticateAsync(RegisteredProvider provider, string userName, string password, CancellationToken ct);
    /// <summary>Finds a user and reads attributes and groups without any password (for the admin test). Throws <see cref="LdapAuthException"/>.</summary>
    Task<LdapLookupResult> LookupAsync(RegisteredProvider provider, string userName, CancellationToken ct);
    /// <summary>Opens a connection and binds as the service account. Throws <see cref="LdapAuthException"/> with <c>ldap_unavailable</c>.</summary>
    Task TestConnectionAsync(RegisteredProvider provider, CancellationToken ct);
}

/// <summary>LDAP filter and DN helpers.</summary>
public static class LdapText
{
    /// <summary>Escapes a value for use inside a search filter (RFC 4515): <c>\ * ( )</c> and NUL.</summary>
    public static string EscapeFilter(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
            sb.Append(c switch { '\\' => "\\5c", '*' => "\\2a", '(' => "\\28", ')' => "\\29", '\0' => "\\00", _ => c.ToString() });
        return sb.ToString();
    }

    /// <summary>Puts the escaped value into the place of <c>{0}</c>.</summary>
    public static string Build(string template, string value) => template.Replace("{0}", EscapeFilter(value), StringComparison.Ordinal);

    /// <summary>The common name inside a DN (<c>cn=finance,ou=groups,...</c> gives <c>finance</c>), or null.</summary>
    public static string? CommonName(string dn)
    {
        var first = dn.Split(',')[0];
        var eq = first.IndexOf('=');
        return eq > 0 && first[..eq].Trim().Equals("cn", StringComparison.OrdinalIgnoreCase) ? first[(eq + 1)..].Trim() : null;
    }
}

/// <inheritdoc />
public sealed class LdapDirectory(IExternalSecretProtector secrets, ILogger<LdapDirectory> log) : ILdapDirectory
{
    private const int MaxNestingDepth = 5;
    private const int MaxGroups = 500;

    private static LdapAuthException Unavailable(string reason, Exception? ex = null) => new(Auth2ErrorCodes.LdapUnavailable, reason, null, ex);
    private static LdapAuthException Invalid(string reason, string? subject = null) => new(Auth2ErrorCodes.InvalidCredentials, reason, subject);

    /// <inheritdoc />
    public async Task TestConnectionAsync(RegisteredProvider provider, CancellationToken ct)
    {
        var s = (LdapProviderSettings)provider.Settings;
        using var conn = await ServiceConnectionAsync(s, ct);
    }

    /// <inheritdoc />
    public async Task<LdapLookupResult> LookupAsync(RegisteredProvider provider, string userName, CancellationToken ct)
    {
        var s = (LdapProviderSettings)provider.Settings;
        using var conn = await ServiceConnectionAsync(s, ct);
        var user = await FindUserAsync(conn, s, userName, ct);
        return new LdapLookupResult(user.Dn, user.Subject, user.Attributes, await GroupsAsync(conn, s, user, ct));
    }

    /// <inheritdoc />
    public async Task<ExternalIdentity> AuthenticateAsync(RegisteredProvider provider, string userName, string password, CancellationToken ct)
    {
        var s = (LdapProviderSettings)provider.Settings;
        // An empty password can mean an anonymous (unauthenticated) bind that "succeeds" on some servers. It is never sent.
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password)) throw Invalid("empty_credentials");

        using var service = await ServiceConnectionAsync(s, ct);
        var user = await FindUserAsync(service, s, userName, ct);

        using (var userConn = await OpenAsync(s, ct))
        {
            try { await WithTimeout(s, ct, t => userConn.BindAsync(user.Dn, password)); }
            catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
            {
                // Active Directory tells the reason in the message: 533 account disabled.
                if (s.RevealDirectoryAccountState && (ex.LdapErrorMessage ?? ex.Message).Contains("data 533", StringComparison.Ordinal))
                    throw new LdapAuthException(Auth2ErrorCodes.LdapAccountDisabled, "account_disabled", user.Subject);
                throw Invalid("wrong_password", user.Subject);
            }
            catch (LdapException ex) { throw Unavailable("bind_failed", ex); }
        }
        if (IsDisabled(user.Attributes))
            throw s.RevealDirectoryAccountState ? new LdapAuthException(Auth2ErrorCodes.LdapAccountDisabled, "account_disabled", user.Subject) : Invalid("account_disabled", user.Subject);

        var groups = await GroupsAsync(service, s, user, ct);
        string? First(string? attr) => attr is not null && user.Attributes.TryGetValue(attr, out var v) && v.Length > 0 && v[0].Length > 0 ? v[0] : null;
        return new ExternalIdentity(provider.Name, user.Subject, "ldap://" + s.Host, First(s.EmailAttribute), First(s.EmailAttribute) is null ? null : true,
            First(s.FirstNameAttribute), First(s.LastNameAttribute), First(s.PhoneAttribute), groups, user.Attributes)
        { DisplayName = First("cn") ?? First("displayName") };
    }

    // ------------------------------------------------------------------------------------------------

    private sealed record FoundUser(string Dn, string Subject, Dictionary<string, string[]> Attributes);

    private async Task<FoundUser> FindUserAsync(LdapConnection conn, LdapProviderSettings s, string userName, CancellationToken ct)
    {
        var filter = LdapText.Build(s.UserSearchFilter, userName);   // the user input is escaped; it can never change the filter
        var wanted = new[] { s.UserNameAttribute, s.UniqueIdAttribute, s.EmailAttribute, s.FirstNameAttribute, s.LastNameAttribute, s.PhoneAttribute, s.MemberOfAttribute,
            "cn", "displayName", "userAccountControl", "pwdAccountLockedTime" }.Where(a => !string.IsNullOrEmpty(a)).Select(a => a!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var entries = new List<LdapEntry>();
        try
        {
            await WithTimeout(s, ct, async t =>
            {
                var results = await conn.SearchAsync(s.UserSearchBase, LdapConnection.ScopeSub, filter, wanted, false);
                while (entries.Count < 2 && await results.HasMoreAsync()) entries.Add(await results.NextAsync());
            });
        }
        catch (LdapException ex) { throw Unavailable("search_failed", ex); }
        // none or more than one: the same answer as a wrong password
        if (entries.Count != 1) throw Invalid(entries.Count == 0 ? "unknown_user" : "ambiguous_user");

        var entry = entries[0];
        var attributes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in wanted)
        {
            var attr = entry.GetOrDefault(name);
            if (attr is not null) attributes[name] = [.. attr.StringValueArray];
        }
        var subject = SubjectOf(entry, s.UniqueIdAttribute) ?? throw Unavailable("no_unique_id");
        return new FoundUser(entry.Dn, subject, attributes);
    }

    /// <summary>The unique id as text. A 16 byte binary value (<c>objectGUID</c>) becomes a GUID string. The DN is never used: it changes when a user is moved.</summary>
    private static string? SubjectOf(LdapEntry entry, string attribute)
    {
        var attr = entry.GetOrDefault(attribute);
        if (attr is null) return null;
        var bytes = attr.ByteValue;
        if (bytes is { Length: 16 } && !bytes.All(b => b is >= 0x20 and < 0x7f)) return new Guid(bytes).ToString();
        return attr.StringValue;
    }

    private static bool IsDisabled(Dictionary<string, string[]> a) =>
        (a.TryGetValue("userAccountControl", out var uac) && int.TryParse(uac[0], out var flags) && (flags & 0x2) != 0)   // Active Directory: ACCOUNTDISABLE
        || a.ContainsKey("pwdAccountLockedTime");                                                                         // OpenLDAP ppolicy

    private async Task<List<string>> GroupsAsync(LdapConnection conn, LdapProviderSettings s, FoundUser user, CancellationToken ct)
    {
        var dns = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.MemberOfAttribute) && user.Attributes.TryGetValue(s.MemberOfAttribute, out var memberOf)) dns.AddRange(memberOf);

        if (!string.IsNullOrWhiteSpace(s.GroupSearchBase) && !string.IsNullOrWhiteSpace(s.GroupFilter))
        {
            var seen = new HashSet<string>(dns, StringComparer.OrdinalIgnoreCase);
            var level = new List<string> { user.Dn };
            for (var depth = 0; depth < (s.NestedGroups ? MaxNestingDepth : 1) && level.Count > 0 && seen.Count < MaxGroups; depth++)
            {
                var next = new List<string>();
                foreach (var dn in level)
                {
                    foreach (var groupDn in await SearchDnsAsync(conn, s, LdapText.Build(s.GroupFilter, dn), ct))
                        if (seen.Add(groupDn)) { dns.Add(groupDn); next.Add(groupDn); }
                }
                level = next;
            }
        }
        // both the name (cn) and the DN are given, so a mapping can use either
        return [.. dns.SelectMany(d => LdapText.CommonName(d) is { } cn ? new[] { cn, d } : new[] { d }).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxGroups * 2)];
    }

    private async Task<List<string>> SearchDnsAsync(LdapConnection conn, LdapProviderSettings s, string filter, CancellationToken ct)
    {
        var found = new List<string>();
        try
        {
            await WithTimeout(s, ct, async t =>
            {
                var results = await conn.SearchAsync(s.GroupSearchBase!, LdapConnection.ScopeSub, filter, ["cn"], false);
                while (found.Count < MaxGroups && await results.HasMoreAsync()) found.Add((await results.NextAsync()).Dn);
            });
        }
        catch (LdapException ex) { throw Unavailable("group_search_failed", ex); }
        return found;
    }

    // ------------------------------------------------------------------------------------------------

    private async Task<LdapConnection> ServiceConnectionAsync(LdapProviderSettings s, CancellationToken ct)
    {
        var password = secrets.Unprotect(s.BindPasswordProtected);
        if (string.IsNullOrEmpty(s.BindDn) || string.IsNullOrEmpty(password)) throw Unavailable("no_service_account");
        var conn = await OpenAsync(s, ct);
        try
        {
            await WithTimeout(s, ct, t => conn.BindAsync(s.BindDn, password));
            return conn;
        }
        catch (LdapException ex)
        {
            conn.Dispose();
            log.LogWarning("The service account bind to {Host} failed (code {Code}).", s.Host, ex.ResultCode);   // never the password
            throw Unavailable("service_bind_failed", ex);
        }
    }

    private async Task<LdapConnection> OpenAsync(LdapProviderSettings s, CancellationToken ct)
    {
        var options = new LdapConnectionOptions();
        if (s.Security == LdapSecurity.Ldaps) options.UseSsl();
        options.ConfigureRemoteCertificateValidationCallback((_, cert, chain, _) => ValidateServerCertificate(s, cert));
        var conn = new LdapConnection(options);
        try
        {
            await WithTimeout(s, ct, async t =>
            {
                await conn.ConnectAsync(s.Host, s.Port);
                if (s.Security == LdapSecurity.StartTls) await conn.StartTlsAsync();   // required: a failed upgrade is an error, never plain text
            });
            return conn;
        }
        catch (Exception ex) when (ex is LdapException or System.Net.Sockets.SocketException or IOException or System.Security.Authentication.AuthenticationException or TimeoutException)
        {
            conn.Dispose();
            log.LogWarning("The directory {Host} cannot be reached: {Kind}.", s.Host, ex.GetType().Name);
            throw Unavailable("connect_failed", ex);
        }
    }

    /// <summary>Checks the chain (system store, or the CA from the settings) and the name. Only the setting <c>ValidateServerCertificate=false</c> skips it.</summary>
    private static bool ValidateServerCertificate(LdapProviderSettings s, System.Security.Cryptography.X509Certificates.X509Certificate? raw)
    {
        if (!s.ValidateServerCertificate) return true;
        if (raw is null) return false;
        using var certificate = X509CertificateLoader.LoadCertificate(raw.GetRawCertData());
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (!string.IsNullOrWhiteSpace(s.CustomCaCertificate))
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(SamlXml.LoadCertificate(s.CustomCaCertificate));
        }
        return chain.Build(certificate) && certificate.MatchesHostname(string.IsNullOrWhiteSpace(s.TlsServerName) ? s.Host : s.TlsServerName);
    }

    private static async Task WithTimeout(LdapProviderSettings s, CancellationToken ct, Func<CancellationToken, Task> work)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(s.TimeoutSeconds, 1, 60)));
        try { await work(cts.Token).WaitAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable("timeout"); }
    }
}
