namespace SystemX.Auth2.Contracts.Constants;

/// <summary>Why a one-time code was made. A code only works for its own purpose.</summary>
public static class Auth2OtpPurposes
{
    /// <summary>Login with a one-time code.</summary>
    public const string Login = "login";
    /// <summary>Confirm a phone number.</summary>
    public const string ConfirmPhone = "confirm_phone";
    /// <summary>Proof of ownership when joining a second application.</summary>
    public const string Join = "join";
    /// <summary>Password reset by phone.</summary>
    public const string PasswordReset = "password_reset";
    /// <summary>A code sent to a <b>new</b> phone number to prove it belongs to the person. The real purpose adds a short hash of the number, so the code works for that number only.</summary>
    public const string ChangePhone = "change_phone";
}
