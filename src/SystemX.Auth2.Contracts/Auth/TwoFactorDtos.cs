namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Data for the authenticator app. Show <c>SharedKey</c> as text and <c>AuthenticatorUri</c> as a QR code.</summary>
public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);

/// <summary>Turn on two-factor with a code from the authenticator app.</summary>
public sealed record TwoFactorEnableRequest(string Code);

/// <summary>The recovery codes. They are shown once.</summary>
public sealed record TwoFactorEnableResponse(IReadOnlyList<string> RecoveryCodes);

/// <summary>Turn off two-factor. The password is needed.</summary>
public sealed record TwoFactorDisableRequest(string Password);

/// <summary>Make new recovery codes. A code from the authenticator app is needed.</summary>
public sealed record RecoveryCodesRequest(string Code);

/// <summary>New recovery codes. They are shown once.</summary>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> Codes);
