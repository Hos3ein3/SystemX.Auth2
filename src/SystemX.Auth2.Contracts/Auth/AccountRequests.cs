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

/// <summary>Change the own user name. <c>CurrentPassword</c> is needed when the person has a password.</summary>
public sealed record ChangeUserNameRequest(string NewUserName, string? CurrentPassword = null);

/// <summary>Start an email change. A confirmation link goes to <c>NewEmail</c>; nothing changes until it is used. <c>CurrentPassword</c> is needed when the person has a password.</summary>
public sealed record ChangeEmailRequest(string NewEmail, string? CurrentPassword = null, string? ReturnUrl = null);

/// <summary>Finish an email change with the token of the link that was sent to the new address (the link carries <c>email</c> and <c>token</c>).</summary>
public sealed record ConfirmEmailChangeRequest(string NewEmail, string Token);

/// <summary>Start a phone change. An SMS code goes to <c>NewPhoneNumber</c>; nothing changes until it is used. <c>CurrentPassword</c> is needed when the person has a password.</summary>
public sealed record ChangePhoneNumberRequest(string NewPhoneNumber, string? CurrentPassword = null);

/// <summary>Finish a phone change with the code that was sent to the new number.</summary>
public sealed record ConfirmPhoneChangeRequest(string NewPhoneNumber, string Code);
