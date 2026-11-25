using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using SystemX.Auth2.Client.Services;
using SystemX.Auth2.Contracts.Auth;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;

namespace SystemX.Auth2.Client.IntegrationTests;

/// <summary>The account methods of <see cref="IAuth2ApiClientService"/> against the real Server, and the language of the calls.</summary>
public class ClientAccountApiTests : ClientTestBase
{
    private const string Password = "Pass-12345!x";

    /// <summary>Remembers the <c>Accept-Language</c> of every request that goes to the Server.</summary>
    private sealed class LanguageSpy(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public List<string> Languages { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Languages.Add(string.Join(",", request.Headers.AcceptLanguage.Select(l => l.Value)));
            return base.SendAsync(request, ct);
        }
    }

    private async Task<ServiceProvider> StartAsync(Action<Client.Options.Auth2ClientOptions>? options = null, HttpMessageHandler? handler = null)
    {
        await UpdateSettingsAsync(s =>
        {
            s.App.AllowSelfUiLogin = true;
            s.Methods.AllowedRegisterMethods.Add(LoginMethod.EmailPassword);
            s.Methods.AllowedRegisterMethods.Add(LoginMethod.PhonePassword);
        });
        return BuildClient("blog", options, b => b.EnableResilience(), handler);
    }

    private async Task<(IAuth2ApiClientService Api, string Token)> SignedInAsync(ServiceProvider client)
    {
        var api = client.GetRequiredService<IAuth2ApiClientService>();
        (await api.RegisterAsync(new RegisterRequest("sara", "sara@example.com", null, Password))).IsSuccess.ShouldBeTrue();
        var login = await api.LoginAsync(new LoginRequest("sara", Password));
        login.IsSuccess.ShouldBeTrue(login.Error?.Code);
        return (api, login.Value!.AccessToken!);
    }

    // ---- confirmations

    [Fact]
    public async Task EmailConfirmation_CanBeSentAgain_AndConfirmedWithTheToken()
    {
        await using var client = await StartAsync();
        var api = client.GetRequiredService<IAuth2ApiClientService>();
        var registered = await api.RegisterAsync(new RegisterRequest("sara", "sara@example.com", null, Password));

        (await api.ResendEmailConfirmationAsync(new ResendEmailConfirmationRequest("sara@example.com"))).IsSuccess.ShouldBeTrue();
        var mail = Server.Messages.LastEmailTo("sara@example.com")!;
        var token = mail.Data!["token"];

        (await api.ConfirmEmailAsync(new ConfirmEmailRequest(registered.Value!.UserId, "wrong-token"))).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidToken);
        (await api.ConfirmEmailAsync(new ConfirmEmailRequest(registered.Value.UserId, token))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task PhoneConfirmation_AskForTheCode_ThenConfirm()
    {
        await using var client = await StartAsync();
        var api = client.GetRequiredService<IAuth2ApiClientService>();
        (await api.RegisterAsync(new RegisterRequest("sara", "sara@example.com", "+15551234567", Password))).IsSuccess.ShouldBeTrue();

        (await api.RequestOtpAsync(new OtpRequest("sara", Auth2OtpPurposes.ConfirmPhone))).IsSuccess.ShouldBeTrue();
        var code = Server.Messages.LastTextTo("+15551234567")!.Data!["code"];

        (await api.ConfirmPhoneAsync(new ConfirmPhoneRequest("sara", code == "000000" ? "111111" : "000000"))).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidOtp);
        (await api.ConfirmPhoneAsync(new ConfirmPhoneRequest("sara", code))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task CompleteJoin_WithAWrongCode_IsAResult_NotAnException()
    {
        await using var client = await StartAsync();
        var result = await client.GetRequiredService<IAuth2ApiClientService>().CompleteJoinAsync(new JoinCompleteRequest("nobody@example.com", "000000", "New-Pass-12345!"));
        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldNotBeNullOrEmpty();
    }

    // ---- two-factor

    [Fact]
    public async Task TwoFactor_Setup_Enable_LoginWithTheCode_NewRecoveryCodes_Disable()
    {
        await using var client = await StartAsync();
        var (api, token) = await SignedInAsync(client);

        var setup = await api.SetupTwoFactorAsync(token);
        setup.IsSuccess.ShouldBeTrue(setup.Error?.Code);
        setup.Value!.AuthenticatorUri.ShouldStartWith("otpauth://totp/");

        (await api.EnableTwoFactorAsync(token, new TwoFactorEnableRequest("000000"))).IsSuccess.ShouldBeFalse();
        var enabled = await api.EnableTwoFactorAsync(token, new TwoFactorEnableRequest(Totp.Now(setup.Value.SharedKey)));
        enabled.IsSuccess.ShouldBeTrue(enabled.Error?.Code);
        enabled.Value!.RecoveryCodes.ShouldNotBeEmpty();

        var first = await api.LoginAsync(new LoginRequest("sara", Password));
        first.Value!.TwoFactorRequired.ShouldBeTrue();
        var second = await api.TwoFactorLoginAsync(new TwoFactorRequest(first.Value.TwoFactorToken!, Totp.Now(setup.Value.SharedKey)));
        second.IsSuccess.ShouldBeTrue(second.Error?.Code);

        var fresh = await api.RegenerateRecoveryCodesAsync(second.Value!.AccessToken!, new RecoveryCodesRequest(Totp.Now(setup.Value.SharedKey)));
        fresh.IsSuccess.ShouldBeTrue(fresh.Error?.Code);
        fresh.Value!.Codes.ShouldNotBeEmpty();
        fresh.Value.Codes.ShouldNotContain(enabled.Value.RecoveryCodes[0]);

        (await api.DisableTwoFactorAsync(second.Value.AccessToken!, new TwoFactorDisableRequest("Wrong-Password-1"))).IsSuccess.ShouldBeFalse();
        (await api.DisableTwoFactorAsync(second.Value.AccessToken!, new TwoFactorDisableRequest(Password))).IsSuccess.ShouldBeTrue();
        (await api.LoginAsync(new LoginRequest("sara", Password))).Value!.TwoFactorRequired.ShouldBeFalse();
    }

    // ---- change the own details

    [Fact]
    public async Task ChangeUserName_NeedsThePassword_AndChangesTheName()
    {
        await using var client = await StartAsync();
        var (api, token) = await SignedInAsync(client);

        (await api.ChangeUserNameAsync(token, new ChangeUserNameRequest("sara2", "Wrong-Password-1"))).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        (await api.ChangeUserNameAsync(token, new ChangeUserNameRequest("sara2", Password))).IsSuccess.ShouldBeTrue();
        (await api.GetMeAsync((await api.LoginAsync(new LoginRequest("sara2", Password))).Value!.AccessToken!)).Value!.UserName.ShouldBe("sara2");
    }

    [Fact]
    public async Task ChangeEmail_SendsTheLinkToTheNewAddress_AndConfirmsWithItsToken()
    {
        await using var client = await StartAsync();
        var (api, token) = await SignedInAsync(client);

        (await api.RequestEmailChangeAsync(token, new ChangeEmailRequest("sara.new@example.com", Password))).IsSuccess.ShouldBeTrue();
        var link = new Uri(Server.Messages.LastEmailTo("sara.new@example.com")!.Data!["link"]);
        var query = QueryHelpers.ParseQuery(link.Query);

        (await api.ConfirmEmailChangeAsync(token, new ConfirmEmailChangeRequest("sara.new@example.com", "wrong-token"))).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidToken);
        (await api.ConfirmEmailChangeAsync(token, new ConfirmEmailChangeRequest(query["email"]!, query["token"]!))).IsSuccess.ShouldBeTrue();
        (await api.GetMeAsync((await api.LoginAsync(new LoginRequest("sara", Password))).Value!.AccessToken!)).Value!.Email.ShouldBe("sara.new@example.com");
    }

    [Fact]
    public async Task ChangePhone_SendsTheCodeToTheNewNumber_AndConfirmsWithIt()
    {
        await using var client = await StartAsync();
        var (api, token) = await SignedInAsync(client);

        (await api.RequestPhoneChangeAsync(token, new ChangePhoneNumberRequest("+15551234567", Password))).IsSuccess.ShouldBeTrue();
        var code = Server.Messages.LastTextTo("+15551234567")!.Data!["code"];

        (await api.ConfirmPhoneChangeAsync(token, new ConfirmPhoneChangeRequest("+15551234567", code == "000000" ? "111111" : "000000"))).Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidOtp);
        (await api.ConfirmPhoneChangeAsync(token, new ConfirmPhoneChangeRequest("+15551234567", code))).IsSuccess.ShouldBeTrue();
        (await api.GetMeAsync((await api.LoginAsync(new LoginRequest("sara", Password))).Value!.AccessToken!)).Value!.PhoneNumber.ShouldBe("+15551234567");
    }

    // ---- language

    [Fact]
    public async Task EveryCall_SendsTheLanguageOfTheCurrentRequest_AndWithCultureChoosesAnother()
    {
        var spy = new LanguageSpy(Server.Server.CreateHandler());
        await using var client = await StartAsync(handler: spy);
        var api = client.GetRequiredService<IAuth2ApiClientService>();
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("fa");
            spy.Languages.Clear();
            await api.LoginAsync(new LoginRequest("nobody", "nope-nope-1A!"));          // the password grant
            await api.GetPermissionsAsync();                                              // a client-token call
            spy.Languages.Where(l => l.Length > 0).ShouldNotBeEmpty();
            spy.Languages.Where(l => l.Length > 0).ShouldAllBe(l => l == "fa");

            spy.Languages.Clear();
            await api.WithCulture("nl-NL").LoginAsync(new LoginRequest("nobody", "nope-nope-1A!"));
            spy.Languages.ShouldContain("nl-NL");
            (await api.WithCulture("fa").WithCulture(null).LoginAsync(new LoginRequest("nobody", "nope-nope-1A!"))).IsSuccess.ShouldBeFalse();   // null goes back to the request language
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public async Task SendRequestCulture_Off_SendsNothing_ButWithCultureStillDoes()
    {
        var spy = new LanguageSpy(Server.Server.CreateHandler());
        await using var client = await StartAsync(o => o.SendRequestCulture = false, spy);
        var api = client.GetRequiredService<IAuth2ApiClientService>();
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("fa");
            spy.Languages.Clear();
            await api.LoginAsync(new LoginRequest("nobody", "nope-nope-1A!"));
            spy.Languages.ShouldAllBe(l => l.Length == 0);
            await api.WithCulture("it").LoginAsync(new LoginRequest("nobody", "nope-nope-1A!"));
            spy.Languages.ShouldContain("it");
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public async Task TheErrorMessage_FollowsTheLanguage_ButTheCodeDoesNot()
    {
        await using var client = await StartAsync();
        var (api, token) = await SignedInAsync(client);
        var request = new ChangeUserNameRequest("sara2", "Wrong-Password-1");

        var english = await api.WithCulture("en").ChangeUserNameAsync(token, request);
        var persian = await api.WithCulture("fa").ChangeUserNameAsync(token, request);
        english.Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        persian.Error!.Code.ShouldBe(Auth2ErrorCodes.InvalidCredentials);
        persian.Error.Message.ShouldNotBe(english.Error.Message);
        persian.Error.Message!.ShouldContain("ن");   // Persian letters

        // The sign-in (token endpoint) answers in the language of the call too.
        var login = new LoginRequest("sara", "Wrong-Password-1");
        var loginEnglish = await api.WithCulture("en").LoginAsync(login);
        var loginPersian = await api.WithCulture("fa").LoginAsync(login);
        loginPersian.Error!.Code.ShouldBe(loginEnglish.Error!.Code);
        loginPersian.Error.Message.ShouldNotBe(loginEnglish.Error.Message);
    }

    [Fact]
    public async Task TheEmailAndSms_OfAChange_AreWrittenInTheLanguageOfTheCall()
    {
        await using var client = await StartAsync();
        var (api, token) = await SignedInAsync(client);

        await api.WithCulture("fa").RequestEmailChangeAsync(token, new ChangeEmailRequest("sara.fa@example.com", Password));
        await api.WithCulture("en").RequestEmailChangeAsync(token, new ChangeEmailRequest("sara.en@example.com", Password));
        Server.Messages.LastEmailTo("sara.fa@example.com")!.Subject.ShouldBe("نشانی ایمیل جدید خود را تأیید کنید");
        Server.Messages.LastEmailTo("sara.en@example.com")!.Subject.ShouldBe("Confirm your new email address");
    }

    private static class Totp
    {
        public static string Now(string base32Key)
        {
            var key = Decode(base32Key.Replace(" ", "").ToUpperInvariant());
            var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
            if (BitConverter.IsLittleEndian) Array.Reverse(counter);
            var hash = HMACSHA1.HashData(key, counter);
            var offset = hash[^1] & 0x0F;
            var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            return (binary % 1_000_000).ToString("D6");
        }

        private static byte[] Decode(string input)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var bytes = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var c in input.TrimEnd('='))
            {
                buffer = (buffer << 5) | alphabet.IndexOf(c);
                bits += 5;
                if (bits >= 8) { bits -= 8; bytes.Add((byte)((buffer >> bits) & 0xFF)); }
            }
            return [.. bytes];
        }
    }
}
