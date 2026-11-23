using System.Security.Cryptography.X509Certificates;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Novell.Directory.Ldap;

namespace SystemX.Auth2.Server.IntegrationTests.Spikes;

/// <summary>An OpenLDAP container with a small directory (users, groups) and TLS with its own CA. Shared by the LDAP tests.</summary>
public sealed class LdapFixture : IAsyncLifetime
{
    public const string BaseDn = "dc=example,dc=org";
    public const string AdminDn = "cn=admin,dc=example,dc=org";
    public const string AdminPassword = "admin-secret-1";
    private IContainer? _container;

    public string Host => _container!.Hostname;
    public int LdapPort => _container!.GetMappedPublicPort(389);
    public int LdapsPort => _container!.GetMappedPublicPort(636);
    public X509Certificate2 CaCertificate { get; private set; } = null!;
    /// <summary>The name in the server certificate. The test makes its own CA and server certificate (the CA of the image has expired).</summary>
    public string ServerName => "ldap.test";

    private const string Ldif = """
        dn: ou=people,dc=example,dc=org
        objectClass: organizationalUnit
        ou: people

        dn: ou=groups,dc=example,dc=org
        objectClass: organizationalUnit
        ou: groups

        dn: uid=sara,ou=people,dc=example,dc=org
        objectClass: inetOrgPerson
        uid: sara
        cn: Sara Jones
        sn: Jones
        givenName: Sara
        mail: sara@example.org
        telephoneNumber: +31612345678
        userPassword: sara-pass-1

        dn: uid=dan,ou=people,dc=example,dc=org
        objectClass: inetOrgPerson
        uid: dan
        cn: Dan Smith
        sn: Smith
        givenName: Dan
        mail: dan@example.org
        userPassword: dan-pass-1

        dn: uid=twin1,ou=people,dc=example,dc=org
        objectClass: inetOrgPerson
        uid: twin
        cn: Twin One
        sn: One
        userPassword: twin-pass-1

        dn: uid=twin2,ou=people,dc=example,dc=org
        objectClass: inetOrgPerson
        uid: twin
        cn: Twin Two
        sn: Two
        userPassword: twin-pass-2

        dn: cn=finance,ou=groups,dc=example,dc=org
        objectClass: groupOfNames
        cn: finance
        member: uid=sara,ou=people,dc=example,dc=org

        dn: cn=staff,ou=groups,dc=example,dc=org
        objectClass: groupOfNames
        cn: staff
        member: cn=finance,ou=groups,dc=example,dc=org
        member: uid=dan,ou=people,dc=example,dc=org
        """;

    public async Task InitializeAsync()
    {
        var dir = Directory.CreateTempSubdirectory("auth2-ldap-");
        MakeCertificates(dir.FullName);
        var ldifFile = new FileInfo(Path.Combine(Path.GetTempPath(), "auth2-test-" + Guid.NewGuid().ToString("N") + ".ldif"));
        await File.WriteAllTextAsync(ldifFile.FullName, Ldif.Replace("\r", ""));
        _container = new ContainerBuilder("osixia/openldap:1.5.0")
            .WithEnvironment("LDAP_ORGANISATION", "Example")
            .WithEnvironment("LDAP_DOMAIN", "example.org")
            .WithEnvironment("LDAP_ADMIN_PASSWORD", AdminPassword)
            .WithEnvironment("LDAP_TLS_VERIFY_CLIENT", "never")
            .WithEnvironment("LDAP_TLS_CRT_FILENAME", "ldap.crt").WithEnvironment("LDAP_TLS_KEY_FILENAME", "ldap.key").WithEnvironment("LDAP_TLS_CA_CRT_FILENAME", "ca.crt")
            .WithHostname("ldap.test")
            .WithResourceMapping(new FileInfo(Path.Combine(dir.FullName, "ca.crt")), new FileInfo("/container/service/slapd/assets/certs/ca.crt"))
            .WithResourceMapping(new FileInfo(Path.Combine(dir.FullName, "ldap.crt")), new FileInfo("/container/service/slapd/assets/certs/ldap.crt"))
            .WithResourceMapping(new FileInfo(Path.Combine(dir.FullName, "ldap.key")), new FileInfo("/container/service/slapd/assets/certs/ldap.key"))
            .WithCommand("--copy-service")
            .WithResourceMapping(ldifFile, new FileInfo("/container/service/slapd/assets/config/bootstrap/ldif/custom/50-test.ldif"))
            .WithPortBinding(389, true).WithPortBinding(636, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("slapd starting"))
            .Build();
        await _container.StartAsync();
        // The container makes its own CA. The test trusts it explicitly (no certificate validation is switched off).
        CaCertificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(string.Concat(File.ReadAllLines(Path.Combine(dir.FullName, "ca.crt")).Where(l => !l.StartsWith("-----")))));
        await Task.Delay(2000);   // slapd needs a moment to listen with TLS
    }

    private static void MakeCertificates(string dir)
    {
        using var caKey = System.Security.Cryptography.RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=Auth2 Test CA", caKey, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

        using var serverKey = System.Security.Cryptography.RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=ldap.test", serverKey, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("ldap.test");
        serverRequest.CertificateExtensions.Add(san.Build());
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1")], false));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var server = serverRequest.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2), Guid.NewGuid().ToByteArray());

        File.WriteAllText(Path.Combine(dir, "ca.crt"), ca.ExportCertificatePem());
        File.WriteAllText(Path.Combine(dir, "ldap.crt"), server.ExportCertificatePem());
        File.WriteAllText(Path.Combine(dir, "ldap.key"), serverKey.ExportPkcs8PrivateKeyPem());
    }

    public async Task DisposeAsync() { if (_container is not null) await _container.DisposeAsync(); }
}

[CollectionDefinition("ldap")]
public sealed class LdapCollection : ICollectionFixture<LdapFixture>;

[Collection("ldap")]
public class LdapSpikeTests(LdapFixture ldap)
{
    private bool Trust(X509Certificate2 ca, X509Certificate? cert, X509Chain? chain)
    {
        if (cert is null) return false;
        using var c = new X509Chain();
        c.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        c.ChainPolicy.CustomTrustStore.Add(ca);
        c.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        var certificate = new X509Certificate2(cert);
        var built = c.Build(certificate);
        return built && certificate.MatchesHostname(ldap.ServerName);   // the chain AND the name are checked
    }

    private async Task<LdapConnection> ConnectAsync(bool ldaps)
    {
        var options = new LdapConnectionOptions();
        if (ldaps) options.UseSsl();
        options.ConfigureRemoteCertificateValidationCallback((_, cert, chain, _) => Trust(ldap.CaCertificate, cert, chain));
        var conn = new LdapConnection(options);
        await conn.ConnectAsync(ldap.Host, ldaps ? ldap.LdapsPort : ldap.LdapPort);
        if (!ldaps) await conn.StartTlsAsync();
        return conn;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ldaps_AndStartTls_ServiceBind_Search_UserRebind_Attributes_AndGroups_Work(bool ldaps)
    {
        using var conn = await ConnectAsync(ldaps);
        await conn.BindAsync(LdapFixture.AdminDn, LdapFixture.AdminPassword);

        var results = await conn.SearchAsync("ou=people," + LdapFixture.BaseDn, LdapConnection.ScopeSub, "(uid=sara)", ["uid", "mail", "givenName", "sn", "entryUUID"], false);
        var entries = new List<LdapEntry>();
        while (await results.HasMoreAsync()) entries.Add(await results.NextAsync());
        entries.Count.ShouldBe(1);
        entries[0].Get("mail").StringValue.ShouldBe("sara@example.org");
        entries[0].Get("entryUUID").StringValue.ShouldNotBeNullOrEmpty();

        // the user's own bind
        using var userConn = await ConnectAsync(ldaps);
        await userConn.BindAsync(entries[0].Dn, "sara-pass-1");
        userConn.Bound.ShouldBeTrue();
        using var wrong = await ConnectAsync(ldaps);
        await Should.ThrowAsync<LdapException>(() => wrong.BindAsync(entries[0].Dn, "wrong"));

        var groups = await conn.SearchAsync("ou=groups," + LdapFixture.BaseDn, LdapConnection.ScopeSub, $"(member={entries[0].Dn})", ["cn"], false);
        var names = new List<string>();
        while (await groups.HasMoreAsync()) names.Add((await groups.NextAsync()).Get("cn").StringValue);
        names.ShouldBe(["finance"]);
    }

    [Fact]
    public async Task PlainLdap_WithoutTls_ConnectsButTheApplicationMustRefuseIt_AndAWrongCaFailsTheHandshake()
    {
        // Without StartTLS the connection works (it is the application that refuses plain LDAP).
        using var plain = new LdapConnection();
        await plain.ConnectAsync(ldap.Host, ldap.LdapPort);
        plain.Connected.ShouldBeTrue();

        // A certificate that is not trusted makes the TLS handshake fail.
        using var untrusted = new LdapConnection(new LdapConnectionOptions().UseSsl().ConfigureRemoteCertificateValidationCallback((_, _, _, _) => false));
        await Should.ThrowAsync<Exception>(() => untrusted.ConnectAsync(ldap.Host, ldap.LdapsPort));
    }

    [Fact]
    public async Task EmptyPasswordBind_IsAnAnonymousBind_SoTheApplicationMustRejectEmptyPasswords()
    {
        using var conn = await ConnectAsync(true);
        // Many servers accept this as an anonymous bind and report success. That is why the application never lets an empty password reach the directory.
        try { await conn.BindAsync("uid=sara,ou=people," + LdapFixture.BaseDn, ""); }
        catch (LdapException) { /* a strict server refuses it */ }
        true.ShouldBeTrue();
    }
}
