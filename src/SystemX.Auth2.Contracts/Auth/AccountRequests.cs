namespace SystemX.Auth2.Contracts.Auth;

/// <summary>Ask for a one-time code. <c>Purpose</c> is <c>login</c> or <c>confirm_phone</c> (see <c>Auth2OtpPurposes</c>). The answer is always the same.</summary>
public sealed record OtpRequest(string Login, string Purpose = "login");

/// <summary>Confirm an email address with the token from the email.</summary>
public sealed record ConfirmEmailRequest(Guid UserId, string Token);

/// <summary>Confirm a phone number with the code from the SMS.</summary>
public sealed record ConfirmPhoneRequest(string Login, string Code);

/// <summary>Send the email confirmation again. The answer is always the same.</summary>
public sealed record ResendEmailConfirmationRequest(string Login, string? ReturnUrl = null);

/// <summary>Finish joining an application: the code that proves ownership, and the new password for this application.</summary>
public sealed record JoinCompleteRequest(string Login, string Code, string NewPassword);
