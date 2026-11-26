#pragma warning disable CS1591 // Page helpers are not a public API; the UI reads their properties (see docs/ui-handover.md).
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemX.Auth2.Contracts.Constants;
using SystemX.Auth2.Contracts.Enums;
using SystemX.Auth2.Server.Application;
using SystemX.Auth2.Server.Domain.Settings;

namespace SystemX.Auth2.Server.Pages.Account;

/// <summary>What one identifier input of a form looks like, worked out from the login methods of the project.</summary>
/// <param name="LabelKey">Key of the label in the page texts (<c>Pages.resx</c>).</param>
/// <param name="Placeholder">An example value.</param>
/// <param name="Type">The input type (<c>text</c>, <c>email</c> or <c>tel</c>).</param>
/// <param name="Autocomplete">The autocomplete hint.</param>
/// <param name="InputMode">The soft keyboard (<c>text</c>, <c>email</c> or <c>tel</c>).</param>
/// <param name="Kinds">What the field accepts.</param>
public sealed record IdentifierField(string LabelKey, string Placeholder, string Type, string Autocomplete, string InputMode, IdentifierKinds Kinds);

/// <summary>The kinds of identifier a field accepts.</summary>
[Flags] public enum IdentifierKinds { None = 0, UserName = 1, Email = 2, Phone = 4 }

/// <summary>
/// The sign-in and sign-up rules of the project (the login methods, the user name, email, phone and password rules saved in the database) turned into
/// what a page needs: which inputs to draw, and the rules the browser checks while the person types (<c>data-a2-rule</c>, read by <c>wwwroot/js/a2.js</c>).
/// The Server still checks everything again when the form is sent; this only tells the person early.
/// </summary>
public sealed class AccountFormRules(ProjectSettings settings, IAuth2Localizer text)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The rules for the page that belongs to <paramref name="page"/>.</summary>
    public static AccountFormRules For(Auth2PageModel page) => new(page.Settings, page.Text);

    // ------------------------------------------------------------------ which identifier input

    /// <summary>The kinds of identifier that can sign in with a password (the sign-in page).</summary>
    public IdentifierKinds LoginKinds() => KindsOf(settings.Methods.AllowedLoginMethods, includeOtp: false, fallback: IdentifierKinds.UserName | IdentifierKinds.Email);

    /// <summary>The kinds of identifier that can be used to find an account (forgot password, join): the password methods and the one-time-code methods.</summary>
    public IdentifierKinds LookupKinds() => KindsOf(settings.Methods.AllowedLoginMethods, includeOtp: true, fallback: IdentifierKinds.UserName | IdentifierKinds.Email);

    /// <summary>The single input that accepts every kind in <paramref name="kinds"/>: its label, example, type and keyboard.</summary>
    public static IdentifierField FieldFor(IdentifierKinds kinds) => kinds switch
    {
        IdentifierKinds.UserName => new("login.identifier_username", "username", "text", "username", "text", kinds),
        IdentifierKinds.Email => new("login.identifier_email", "you@example.com", "email", "email", "email", kinds),
        IdentifierKinds.Phone => new("login.identifier_phone", "+14155552671", "tel", "tel", "tel", kinds),
        IdentifierKinds.UserName | IdentifierKinds.Email => new("login.username_or_email", "you@example.com", "text", "username", "email", kinds),
        IdentifierKinds.UserName | IdentifierKinds.Phone => new("login.identifier_username_phone", "username or +14155552671", "text", "username", "text", kinds),
        IdentifierKinds.Email | IdentifierKinds.Phone => new("login.identifier_email_phone", "you@example.com or +14155552671", "text", "username", "text", kinds),
        _ => new("login.identifier_all", "you@example.com", "text", "username", "text", IdentifierKinds.UserName | IdentifierKinds.Email | IdentifierKinds.Phone),
    };

    private static IdentifierKinds KindsOf(IEnumerable<LoginMethod> methods, bool includeOtp, IdentifierKinds fallback)
    {
        var kinds = IdentifierKinds.None;
        foreach (var m in methods)
        {
            kinds |= m switch
            {
                LoginMethod.UserNamePassword => IdentifierKinds.UserName,
                LoginMethod.EmailPassword => IdentifierKinds.Email,
                LoginMethod.PhonePassword => IdentifierKinds.Phone,
                LoginMethod.EmailOtp when includeOtp => IdentifierKinds.Email,
                LoginMethod.PhoneOtp when includeOtp => IdentifierKinds.Phone,
                _ => IdentifierKinds.None,
            };
        }
        return kinds == IdentifierKinds.None ? fallback : kinds;
    }

    // ------------------------------------------------------------------ rules for the browser

    /// <summary>
    /// A required identifier field (sign-in, forgot password, join): the value must fit <b>one</b> of the kinds the field accepts. A user name must follow the user name rules
    /// of the project (length and allowed characters: every account has one, an email or a phone number is the user name when none was chosen), an email must have an email format,
    /// a phone number must match the phone regex. The email domain lists are not checked here: they apply when an address is added, not when somebody signs in.
    /// </summary>
    public string Identifier(IdentifierField field, string label) => Serialize(new Rule
    {
        Required = Required(label),
        Max = 256,
        MaxMsg = text.Validation("too_long", label, 256),
        Any = new AnyOf
        {
            UserName = field.Kinds.HasFlag(IdentifierKinds.UserName)
                ? new UserNameRule(settings.UserName.MinLength, settings.UserName.MaxLength, settings.UserName.AllowedCharacters,
                    text.Validation("too_short", label, settings.UserName.MinLength), text.Validation("too_long", label, settings.UserName.MaxLength), text.Error(Auth2ErrorCodes.InvalidUserName))
                : null,
            Email = field.Kinds.HasFlag(IdentifierKinds.Email) ? text.Validation("invalid_email", label) : null,
            Phone = field.Kinds.HasFlag(IdentifierKinds.Phone) ? new PhoneRule(PhonePattern(), text.Error(Auth2ErrorCodes.InvalidPhoneNumber)) : null,
        },
    });

    /// <summary>
    /// The password input of a sign-in page: the same rules as when a password is chosen (length, digit, lower, upper, symbol, different characters), told as one message at a time
    /// without the list (a person signing in is not choosing a password).
    /// </summary>
    public string LoginPassword(string label) => NewPassword(label, showList: false);

    /// <summary>The code of an authenticator app: 6 to 8 digits (spaces are allowed, the Server ignores them).</summary>
    public string TotpCode(string label) => Serialize(new Rule
    {
        Required = Required(label),
        Pattern = @"^[0-9 ]{6,10}$",
        PatternMsg = text.Validation("invalid_format", label),
    });

    /// <summary>A required field that only needs a value (a current password, a code).</summary>
    public string RequiredOnly(string label) => Serialize(new Rule { Required = Required(label) });

    /// <summary>The user name rules of the project (length and allowed characters).</summary>
    public string UserName(string label, bool required) => Serialize(new Rule
    {
        Required = required ? Required(label) : null,
        Min = settings.UserName.MinLength,
        MinMsg = text.Validation("too_short", label, settings.UserName.MinLength),
        Max = settings.UserName.MaxLength,
        MaxMsg = text.Validation("too_long", label, settings.UserName.MaxLength),
        Pattern = settings.UserName.AllowedCharacters,
        PatternMsg = text.Error(Auth2ErrorCodes.InvalidUserName),
    });

    /// <summary>The email rules of the project (format and the allowed and blocked domains).</summary>
    public string Email(string label, bool required) => Serialize(new Rule
    {
        Required = required ? Required(label) : null,
        Max = 256,
        MaxMsg = text.Validation("too_long", label, 256),
        Email = text.Validation("invalid_email", label),
        Allow = settings.Email.AllowedDomains.Count > 0 ? settings.Email.AllowedDomains : null,
        Block = settings.Email.BlockedDomains.Count > 0 ? settings.Email.BlockedDomains : null,
        DomainMsg = text.Error(Auth2ErrorCodes.EmailDomainNotAllowed),
    });

    /// <summary>The phone rule of the project (its regular expression).</summary>
    public string Phone(string label, bool required) => Serialize(new Rule
    {
        Required = required ? Required(label) : null,
        Phone = PhonePattern(),
        PhoneMsg = text.Error(Auth2ErrorCodes.InvalidPhoneNumber),
    });

    /// <summary>The password rules of the project. The page shows them as a list that turns green while the person types.</summary>
    public string NewPassword(string label, bool showList = true)
    {
        var p = settings.Password;
        var checks = new List<Check>();
        checks.Add(new("length", text.Error(Auth2ErrorCodes.PasswordTooShort, p.MinLength)));
        if (p.RequireDigit) checks.Add(new("digit", text.Error(Auth2ErrorCodes.PasswordRequiresDigit)));
        if (p.RequireLower) checks.Add(new("lower", text.Error(Auth2ErrorCodes.PasswordRequiresLower)));
        if (p.RequireUpper) checks.Add(new("upper", text.Error(Auth2ErrorCodes.PasswordRequiresUpper)));
        if (p.RequireNonAlphanumeric) checks.Add(new("symbol", text.Error(Auth2ErrorCodes.PasswordRequiresNonAlphanumeric)));
        if (p.UniqueChars > 1) checks.Add(new("unique", text.Error(Auth2ErrorCodes.PasswordRequiresUniqueChars, p.UniqueChars)));
        return Serialize(new Rule
        {
            Required = Required(label),
            Min = p.MinLength,
            Max = p.MaxLength,
            MaxMsg = text.Error(Auth2ErrorCodes.PasswordTooLong, p.MaxLength),
            Digit = p.RequireDigit ? true : null,
            Lower = p.RequireLower ? true : null,
            Upper = p.RequireUpper ? true : null,
            Symbol = p.RequireNonAlphanumeric ? true : null,
            Unique = p.UniqueChars > 1 ? p.UniqueChars : null,
            Checks = checks,
            HideList = showList ? null : true,
        });
    }

    /// <summary>The "repeat the password" field: it must equal the field with the id <paramref name="otherId"/>.</summary>
    public string ConfirmPassword(string label, string otherId) => Serialize(new Rule
    {
        Required = Required(label),
        Match = otherId,
        MatchMsg = text.Validation("passwords_do_not_match"),
    });

    // ------------------------------------------------------------------ helpers

    private string Required(string label) => text.Validation("required", label);

    private string PhonePattern() => settings.Phone.PhoneNumberRegex;

    private static string Serialize(Rule rule) => JsonSerializer.Serialize(rule, Json);

    private sealed record Check(string Id, string Msg);

    private sealed class Rule
    {
        public string? Required { get; init; }
        public int? Min { get; init; }
        public string? MinMsg { get; init; }
        public int? Max { get; init; }
        public string? MaxMsg { get; init; }
        public string? Pattern { get; init; }
        public string? PatternMsg { get; init; }
        public string? Email { get; init; }
        public IReadOnlyList<string>? Allow { get; init; }
        public IReadOnlyList<string>? Block { get; init; }
        public string? DomainMsg { get; init; }
        public string? Phone { get; init; }
        public string? PhoneMsg { get; init; }
        public bool? Digit { get; init; }
        public bool? Lower { get; init; }
        public bool? Upper { get; init; }
        public bool? Symbol { get; init; }
        public int? Unique { get; init; }
        public IReadOnlyList<Check>? Checks { get; init; }
        public bool? HideList { get; init; }
        public string? Match { get; init; }
        public string? MatchMsg { get; init; }
        public AnyOf? Any { get; init; }
    }

    private sealed class AnyOf
    {
        public UserNameRule? UserName { get; init; }
        public string? Email { get; init; }
        public PhoneRule? Phone { get; init; }
    }

    private sealed record UserNameRule(int Min, int Max, string Pattern, string MinMsg, string MaxMsg, string PatternMsg);
    private sealed record PhoneRule(string Regex, string Msg);
}
