using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SystemX.Auth2.Client.Machine;
using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Contracts.Applications;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.Handshake;

/// <summary>Result of one handshake attempt.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Response">The server answer (only for <see cref="HandshakeStatus.Ok"/>).</param>
/// <param name="Code">The stable error code, or null.</param>
/// <param name="Message">What is wrong and what to do. Never holds secrets.</param>
public sealed record HandshakeOutcome(HandshakeStatus Status, ClientHandshakeResponse? Response, string? Code, string? Message);

/// <summary>Makes one handshake attempt and turns every possible answer into a <see cref="HandshakeStatus"/>.</summary>
public sealed class HandshakeRunner(IHttpClientFactory http, IAuth2MachineTokenProvider tokens, IOptions<Auth2ClientOptions> options, IOptions<Auth2HealthCheckOptions>? health = null)
{
    /// <summary>Warning when the client has a project key but the server has a single project. The key is ignored.</summary>
    public const string SingleServerMessage = "The server has a single project. The ProjectKey is ignored. Remove ServerWithMultiProject.";

    /// <summary>Message when the client has no project key but the server serves many projects.</summary>
    public const string MultiServerMessage = "The server serves many projects. Call ServerWithMultiProject(projectKey) with the key of the project of this application.";

    /// <summary>Makes one attempt. Never throws (except when <paramref name="ct"/> is cancelled).</summary>
    public async Task<HandshakeOutcome> RunAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(o.HandshakeTimeout);
        try
        {
            var token = await tokens.GetTokenAsync([Auth2Scopes.ClientHandshake], timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, Auth2Routes.Handshake.TrimStart('/'))
            {
                Content = JsonContent.Create(new ClientHandshakeRequest(o.ProjectKey, o.ApplicationName, o.ClientVersion, health?.Value.HealthUrl)),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await http.CreateClient(Auth2ClientDefaults.ResilientHttpClientName).SendAsync(request, timeout.Token);
            return await MapAsync(response, o, timeout.Token);
        }
        catch (Auth2Exception ex) when (ex.IsTransient)
        {
            return new HandshakeOutcome(HandshakeStatus.ServerUnavailable, null, ex.ErrorCode, "The server could not be reached.");
        }
        catch (Auth2Exception ex)
        {
            // The token endpoint refused the client: wrong secret, unknown client id, or a scope the client may not use.
            return new HandshakeOutcome(HandshakeStatus.Rejected, null, ex.ErrorCode,
                "The server refused the client credentials. Check ClientId and ClientSecret, and that the client may use the scope 'auth2.client.handshake'.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException && !ct.IsCancellationRequested)
        {
            return new HandshakeOutcome(HandshakeStatus.ServerUnavailable, null, Auth2ClientErrorCodes.ServerUnavailable, "The server could not be reached or was too slow.");
        }
    }

    private static async Task<HandshakeOutcome> MapAsync(HttpResponseMessage response, Auth2ClientOptions o, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            ClientHandshakeResponse? body;
            try { body = await response.Content.ReadFromJsonAsync<ClientHandshakeResponse>(ct); }
            catch (JsonException) { body = null; }
            if (body is null) return new HandshakeOutcome(HandshakeStatus.Rejected, null, Auth2ClientErrorCodes.InvalidResponse, "The handshake answer of the server could not be understood. Is ServerUrl the SystemX.Auth2 Server?");

            // The server says which mode it has. The client never guesses.
            var hasKey = !string.IsNullOrWhiteSpace(o.ProjectKey);
            // A key sent to a single-project server is not an error: the server ignores it, and the client only warns.
            if (hasKey && body.ServerMode == ServerMode.SingleProject) return new HandshakeOutcome(HandshakeStatus.Ok, body, null, SingleServerMessage);
            if (!hasKey && body.ServerMode == ServerMode.MultiProject) return new HandshakeOutcome(HandshakeStatus.Rejected, null, Auth2ErrorCodes.ServerModeMismatch, MultiServerMessage);
            return new HandshakeOutcome(HandshakeStatus.Ok, body, null, null);
        }

        var code = await ReadCodeAsync(response, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new HandshakeOutcome(HandshakeStatus.NotFound, null, code ?? Auth2ErrorCodes.ProjectNotFound,
                "The server does not know this application. Check ClientId and ServerUrl.");
        if (status >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            return new HandshakeOutcome(HandshakeStatus.ServerUnavailable, null, code ?? Auth2ClientErrorCodes.ServerUnavailable, $"The server answered {status}.");

        if (code == Auth2ErrorCodes.ServerModeMismatch)
            return new HandshakeOutcome(HandshakeStatus.Rejected, null, code, string.IsNullOrWhiteSpace(o.ProjectKey) ? MultiServerMessage : SingleServerMessage);
        if (code == Auth2ErrorCodes.ProjectKeyOrNameMismatch)
            return new HandshakeOutcome(HandshakeStatus.Rejected, null, code, "ProjectKey or ApplicationName does not match the application of this client id on the server (the application must belong to that project and have that name).");
        return new HandshakeOutcome(HandshakeStatus.Rejected, null, code ?? "rejected", $"The server rejected the handshake ({status}{(code is null ? "" : ", " + code)}).");
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
