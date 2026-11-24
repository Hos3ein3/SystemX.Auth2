using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SystemX.Auth2.Server.IntegrationTests;

/// <summary>
/// A WebAuthn authenticator in software (ES256, attestation "none"). It makes the JSON that a browser would send for
/// <c>navigator.credentials.create</c> and <c>.get</c>, from the options JSON of the Server.
/// </summary>
public sealed class SoftwareAuthenticator(string origin = "http://localhost")
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public byte[] CredentialId { get; set; } = RandomNumberGenerator.GetBytes(32);
    public uint Counter { get; set; }
    public bool UserVerified { get; set; } = true;
    public bool BackupEligible { get; set; } = true;
    public bool BackedUp { get; set; }
    public Guid Aaguid { get; set; } = Guid.Parse("11111111-2222-3333-4444-555555555555");
    public string? UserHandle { get; private set; }

    private static string B64(byte[] b) => Base64Url.EncodeToString(b);

    private static JsonNode Options(string json)
    {
        var root = JsonNode.Parse(json)!;
        return root["publicKey"] ?? root;
    }

    private string ClientData(string type, string challenge) =>
        JsonSerializer.Serialize(new { type, challenge, origin, crossOrigin = false });

    private byte Flags(bool attested) =>
        (byte)(0x01 | (UserVerified ? 0x04 : 0) | (BackupEligible ? 0x08 : 0) | (BackedUp ? 0x10 : 0) | (attested ? 0x40 : 0));

    private byte[] AuthData(string rpId, bool attested)
    {
        using var ms = new MemoryStream();
        ms.Write(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        ms.WriteByte(Flags(attested));
        var counter = BitConverter.GetBytes(Counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        ms.Write(counter);
        if (attested)
        {
            ms.Write(Aaguid.ToByteArray(bigEndian: true));
            ms.WriteByte((byte)(CredentialId.Length >> 8)); ms.WriteByte((byte)(CredentialId.Length & 0xff));
            ms.Write(CredentialId);
            var p = _key.ExportParameters(false);
            var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
            cose.WriteStartMap(5);
            cose.WriteInt32(1); cose.WriteInt32(2);
            cose.WriteInt32(3); cose.WriteInt32(-7);
            cose.WriteInt32(-1); cose.WriteInt32(1);
            cose.WriteInt32(-2); cose.WriteByteString(p.Q.X!);
            cose.WriteInt32(-3); cose.WriteByteString(p.Q.Y!);
            cose.WriteEndMap();
            ms.Write(cose.Encode());
        }
        return ms.ToArray();
    }

    /// <summary>The answer to <c>navigator.credentials.create</c>.</summary>
    public string Create(string optionsJson)
    {
        var o = Options(optionsJson);
        var rpId = o["rp"]!["id"]?.GetValue<string>() ?? new Uri(origin).Host;
        UserHandle = o["user"]!["id"]!.GetValue<string>();
        var attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
        attestation.WriteTextString("authData"); attestation.WriteByteString(AuthData(rpId, attested: true));
        attestation.WriteEndMap();
        return JsonSerializer.Serialize(new
        {
            id = B64(CredentialId), rawId = B64(CredentialId), type = "public-key", authenticatorAttachment = "platform",
            response = new { clientDataJSON = B64(Encoding.UTF8.GetBytes(ClientData("webauthn.create", o["challenge"]!.GetValue<string>()))), attestationObject = B64(attestation.Encode()), transports = new[] { "internal" } },
            clientExtensionResults = new { },
        });
    }

    /// <summary>The answer to <c>navigator.credentials.get</c>. The counter goes up by one.</summary>
    public string Get(string optionsJson, bool withUserHandle = true)
    {
        var o = Options(optionsJson);
        var rpId = o["rpId"]?.GetValue<string>() ?? new Uri(origin).Host;
        Counter++;
        var clientData = Encoding.UTF8.GetBytes(ClientData("webauthn.get", o["challenge"]!.GetValue<string>()));
        var authData = AuthData(rpId, attested: false);
        var signature = _key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.Serialize(new
        {
            id = B64(CredentialId), rawId = B64(CredentialId), type = "public-key", authenticatorAttachment = "platform",
            response = new { clientDataJSON = B64(clientData), authenticatorData = B64(authData), signature = B64(signature), userHandle = withUserHandle ? UserHandle : null },
            clientExtensionResults = new { },
        });
    }
}
