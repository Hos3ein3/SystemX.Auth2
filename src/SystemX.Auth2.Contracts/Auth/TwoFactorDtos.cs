namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Data for the authenticator app. Show <c>SharedKey</c> as text and <c>AuthenticatorUri</c> as a QR code.</summary>
public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);

/// <summary>
/// A code from the authenticator app, checked for the signed-in user before a sensitive action. A code works once.
/// <paramref name="Optional"/>: the action only asks people who have two-factor on. A person without it passes (nothing to check); with <c>false</c> (the default) such a person is refused
/// with <c>totp_setup_required</c>. An empty code from a person who has two-factor is <c>totp_required</c> and is not counted as a failed attempt.
/// </summary>
public sealed record TwoFactorVerifyRequest(string Code, bool Optional = false);

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
