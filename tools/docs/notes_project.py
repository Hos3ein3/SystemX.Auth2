# Hand-written explanations of the project settings. Facts (type, default, who may edit) come from the code; only the words are here.
# key: path in ProjectSettings. what / when / why / ex (a value that is written into a JSON example; None = no example).
N = {}
def n(path, what, when, why, ex=None): N[path] = dict(what=what, when=when, why=why, ex=ex)

n('SchemaVersion', 'The version of the JSON layout of the settings. The Server refuses a value it does not know.', 'Never by hand. A later version of the Server may raise it and convert old JSON.', 'It lets the Server change the shape of the settings without breaking projects that already store the old shape.')

# Methods
n('Methods.AllowedLoginMethods', 'The ways a person may sign in to this project (`LoginMethod`). A sign-in with a method that is not in the list is refused with `login_method_not_allowed`, before the password is checked. `External`, `Ldap` and `Passkey` must be here before those providers work.', 'When you open or close a way to sign in: add `External` for social login and SAML, `Ldap` for a directory, `Passkey` for passkeys, `EmailOtp` / `PhoneOtp` for codes.', 'One list decides every entry point (token endpoint, hosted pages, passkey and external routes), so a method you do not offer cannot be used even if its provider exists. The list must not be empty.', ['EmailPassword', 'External', 'Passkey'])
n('Methods.AllowedRegisterMethods', 'Which identifiers a new person may register with. An email needs `EmailPassword` or `EmailOtp`, a phone number needs `PhonePassword` or `PhoneOtp`, a user name needs `UserNamePassword`. If any password method is listed the password is required.', 'When your product registers people by email only, or by phone only.', 'It keeps the identifier rules of the product in one place: a request with a phone number is refused with `login_method_not_allowed` when phone sign-up is not offered.', ['EmailPassword'])

# UserName
n('UserName.MinLength', 'The shortest user name (counted in characters). Checked when a person is created or renamed.', 'Raise it to avoid one- and two-letter names.', 'Very short names are easy to guess and to squat. The validator needs at least 1.', 4)
n('UserName.MaxLength', 'The longest user name. At most 256 is accepted.', 'Lower it if the name is shown in narrow places.', 'Bounds the data and the display.', 32)
n('UserName.AllowedCharacters', 'A regular expression the whole user name must match. It runs with a time limit (`Phone.RegexTimeoutMs`), so a hostile pattern cannot hang the server; a timeout counts as "does not match".', 'When names must follow a pattern, for example letters and digits only.', 'Stops look-alike or unsafe characters. Email-style names (`@`, `.`) are allowed by default because people often use their email as the name.', '^[a-z0-9._-]+$')
n('UserName.RequireUnique', 'Intended as the switch for unique user names. **Today no code reads it**: user names are always unique across the server (an index and the Identity validator enforce it).', 'Leave it on.', 'Documented so that nobody expects turning it off to allow duplicates. It is stored and shown in the admin settings page only.', True)

# Password
n('Password.MinLength', 'The shortest password. The validator refuses a value below 6.', 'Raise it (12 to 14) for sensitive products.', 'Length is the strongest single defence against guessing.', 12)
n('Password.MaxLength', 'The longest password. At most 1024.', 'Rarely.', 'Very long passwords cost CPU in the hash. The limit is also a guard against abuse.', 128)
n('Password.RequireDigit', 'The password must contain a digit.', 'Switch off if you prefer long passphrases over composition rules.', 'Composition rules add little over length, but some policies demand them.', True)
n('Password.RequireLower', 'The password must contain a lowercase letter.', 'As above.', 'Some policies demand it.', True)
n('Password.RequireUpper', 'The password must contain an uppercase letter.', 'As above.', 'Some policies demand it.', True)
n('Password.RequireNonAlphanumeric', 'The password must contain a character that is not a letter or digit.', 'Turn on when a policy demands a symbol.', 'Off by default: it makes passwords harder to type without making them much stronger.', True)
n('Password.UniqueChars', 'The password must have at least this many different characters (0 turns the rule off; it may not be more than `MinLength`).', 'Raise it to reject `aaaaaaaaaaaa`-style passwords.', 'A cheap way to block repeated characters.', 6)
n('Password.BannedPasswords', 'A list of passwords that are always refused (compared without regard to case).', 'Add the product name, the company name, and passwords from your own breach list.', 'People pick these first, and attackers try them first.', ['PaperTrade2026!', 'Welcome123!'])

# Email
n('Email.RequireConfirmedEmail', 'A person whose email is not confirmed cannot finish signing in, even with the right password. The check comes **after** the password. It also forces the confirmation email to be sent at registration (`Registration.SendConfirmationEmail` is then ignored).', 'Turn on in production, once a real email provider is configured ([email-and-sms.md](email-and-sms.md)).', 'It proves the person owns the address, which protects password reset and account recovery. It is off by default so that a fresh install works with the logging email sender.', True)
n('Email.AllowedDomains', 'When the list is not empty, only these email domains may be used.', 'A company product that only accepts staff email.', 'Keeps strangers out without invitations.', ['corp.com'])
n('Email.BlockedDomains', 'These email domains are never accepted.', 'Block disposable-mail domains.', 'Reduces fake sign-ups.', ['mailinator.com'])

# Phone
n('Phone.PhoneNumberRegex', 'A regular expression a phone number must match. The default accepts the international E.164 form (`+` and 7 to 15 digits).', 'When you accept only some countries or a different format.', 'A predictable format lets you send SMS and compare numbers.', '^\\+31[1-9]\\d{8}$')
n('Phone.RequireConfirmedPhone', 'A person whose phone number is not confirmed cannot finish signing in. The confirmation code is then always sent at registration.', 'When phone is the main identifier.', 'Proves the number belongs to the person.', True)
n('Phone.RegexTimeoutMs', 'The time limit of **every** regular expression the Server runs for names and phone numbers (1 to 1000 ms).', 'Lower it if you must be stricter; do not raise it.', 'Protects the Server from a regular expression that runs for a very long time (ReDoS).', 100)

# Registration
n('Registration.RegistrationEnabled', 'Switches registration of new people on or off. Off answers `registration_disabled` and hides the register link.', 'Turn off for a closed product where admins create the accounts.', 'The simplest way to close sign-up without removing the feature.', False)
n('Registration.JoinMode', 'What happens when a person who already has an account (in another project) wants to join this project: `AutoJoin` (with proof of ownership), `InviteOnly` or `Closed`. See [enums-reference.md](enums-reference.md#joinmode).', 'Set `Closed` or `InviteOnly` for private projects.', 'It decides who may get a membership row, which is what lets a person sign in to the project.', 'InviteOnly')
n('Registration.LoginRequiredAfterRegister', 'When `true` the person goes to the sign-in page after registering. When `false` the hosted page signs them in at once, but only if nothing has to be confirmed first.', 'Set `false` for a smooth sign-up when no confirmation is required.', 'One step fewer for the user; `true` is the safer default because the first sign-in then runs every check.', False)
n('Registration.SendConfirmationEmail', 'Send the confirmation email right after registration. Always sent when `Email.RequireConfirmedEmail` is on. An application can override it; the user can always ask for it again.', 'Turn off when your application confirms emails in another way.', 'Avoids mail you do not want to send, without losing the resend route.', False)
n('Registration.AssignedRoles', 'Role names every new member gets (register, join, first external sign-in). The roles must exist in the project; a missing one is skipped and audited. `GlobalAdmin`, `Developer` and `ProjectAdmin` are refused.', 'Give every new user a starting role such as `Customer`.', 'Saves an admin step after each registration. It is global-admin only because it decides what a stranger gets.', ['Customer'])
n('Registration.AssignedGroups', 'Group names every new member joins. The groups must exist.', 'Put every new user in a group such as `customers` that carries roles and permissions.', 'A group is the easiest way to change the starting rights later: change the group, not the setting.', ['customers'])
n('Registration.AssignedPermissions', 'Permission keys every new member gets directly. The permissions must exist.', 'Rarely; prefer a role or a group.', 'Direct grants are harder to review than roles.', ['orders.read'])

# Lockout
n('Lockout.LockoutEnabled', 'Locks an account for a while after too many failed attempts. Wrong passwords, wrong one-time codes, wrong authenticator codes and a wrong current password all count.', 'Keep it on.', 'It slows down password guessing for one account (the rate limiter slows it per address).', True)
n('Lockout.MaxFailedAttempts', 'How many failed attempts lock the account (at least 1). The counter is reset when the lock starts.', 'Lower it for sensitive projects.', 'A small number stops guessing sooner but makes it easier to lock someone else out on purpose.', 5)
n('Lockout.LockoutMinutes', 'How long the lock lasts (at least 1).', 'Raise it to slow attackers; lower it to reduce support work.', 'The user is told `locked_out` and can try again when it ends.', 30)
n('Lockout.LockoutForNewUsers', 'Whether accounts that are created from now on can be locked. A new account is lockable only when `LockoutEnabled` and this are both on.', 'Almost never.', 'A switch for rare cases such as service accounts that must never be locked.', True)

# TwoFactor
n('TwoFactor.Mode', 'Whether the authenticator-app second step is off (`Off`), the user\'s choice (`Optional`), or demanded (`Required`). With `Required`, a person who has not set it up cannot sign in and gets `two_factor_setup_required`. See [enums-reference.md](enums-reference.md#twofactormode).', 'Set `Required` for admin projects after your users have enrolled.', 'A second factor stops most password theft. Check that people can enrol **before** you make it required.', 'Optional')
n('TwoFactor.AllowTotp', 'Allows authenticator apps (TOTP). Required for `Mode` other than `Off` to have any effect, and for `Mode = Required`.', 'Turn off only if you do not want any second step.', 'It is the only second factor the Server offers today.', True)
n('TwoFactor.AllowRecoveryCodes', 'Lets people create and use one-time recovery codes for the case that the phone is lost.', 'Turn off if you want only an admin to recover accounts.', 'Without recovery codes a lost phone locks the person out.', True)
n('TwoFactor.RecoveryCodeCount', 'How many recovery codes are made (1 to 50).', 'Rarely.', 'Ten is enough for several lost-phone cases.', 8)
n('TwoFactor.OtpLength', 'The number of digits of **every** one-time code: login, phone confirmation, join and password reset (4 to 10).', 'Raise it for stronger codes; lower it if people type from SMS.', 'Longer codes are harder to guess in the few attempts that are allowed.', 6)
n('TwoFactor.OtpLifetimeSeconds', 'How long a one-time code is valid (10 to 3600).', 'Raise it when SMS is slow.', 'A short life limits the time an attacker has.', 300)
n('TwoFactor.OtpMaxAttempts', 'How many wrong tries close a code (at least 1).', 'Lower it for stricter projects.', 'A code is a short secret; few attempts make guessing hopeless.', 3)
n('TwoFactor.OtpResendSeconds', 'The wait before a new code may be requested (0 or more).', 'Raise it to limit SMS cost.', 'Stops people and bots from flooding one phone or inbox.', 90)
n('TwoFactor.AllowPasskeys', 'Intended as the switch for passkeys as a second factor. **Today no code reads it.** Passkeys are a primary sign-in method and are controlled by `Passkeys.AllowPasskeys` and `Methods.AllowedLoginMethods`.', 'Use `Passkeys.AllowPasskeys` instead.', 'Documented to avoid confusion: it is stored and shown in the admin settings page only.', False)

# Sessions
n('Sessions.AccessTokenMinutes', 'The lifetime of an access token (1 to 1440). An application can override it.', 'Lower it for sensitive APIs; raise it to make fewer refreshes.', 'A short life limits the damage of a stolen token. APIs that check the signature locally accept a revoked token until it expires.', 10)
n('Sessions.RefreshTokenDays', 'The lifetime of a refresh token (1 to 365). Every use makes a new one; using an old one twice revokes the whole chain.', 'Raise it for apps people open rarely.', 'It is how long a person stays signed in without typing a password. Keep `Auth2:Keys:RetiredKeyDays` longer than this ([key-ring.md](key-ring.md)).', 30)
n('Sessions.IdentityTokenMinutes', 'The lifetime of an identity token (1 to 1440).', 'Rarely.', 'Identity tokens are read once at sign-in, so a short life is enough.', 5)
n('Sessions.CookieLifetimeMinutes', 'How long the Server\'s own sign-in cookie lasts, and (read at the handshake) the default lifetime of the cookie of your applications. `null` means a session cookie (until the browser closes) on the Server and 8 hours in the Client package. An application can override it.', 'Set it when "stay signed in" should survive closing the browser.', 'A persistent cookie is convenient but stays on shared computers; choose the time on purpose.', 480)
n('Sessions.CookieSliding', 'Intended as the switch for a sliding cookie expiry. **Today no code reads it.** The Client package has its own `SlidingExpiration` option.', 'Use the client option.', 'Documented so that nobody expects a server effect.', True)
n('Sessions.SingleActiveSession', 'When a person signs in, their other sessions in this project are revoked (every application of the project). With per-project credentials the security stamp is also changed, so the older cookies stop working.', 'Banking-style apps where one device at a time is the rule.', 'Limits the damage of a shared or stolen session.', True)
n('Sessions.RequireConsent', 'Master switch for the consent page. Off: no consent page whatever `ConsentMode` says. On: an `Implicit` mode is treated as `Explicit`.', 'Turn on when third-party applications use your project.', 'People should see what an application asks for before they allow it.', True)
n('Sessions.ConsentMode', 'How consent works when it is on: `Implicit` (never asks), `Explicit` (asks once, remembers), `Systematic` (asks every time). An application can override it. See [enums-reference.md](enums-reference.md#consentmode).', 'Together with `RequireConsent`.', 'It maps to the consent type of the OpenIddict application.', 'Explicit')

# Redirects
n('Redirects.RedirectAfterLogin', 'Where the hosted sign-in sends the person when the request has no valid return URL. The value must be a local path (one `/`) or an absolute `http(s)` URL; `//host` is rejected.', 'Set it to the home page of your product.', 'A fallback so that sign-in never ends on a blank page. A valid return URL always wins, and must be registered on the application.', '/home')
n('Redirects.RedirectAfterRegister', 'Where the person goes after registering with the hosted page when they are signed in at once (`LoginRequiredAfterRegister = false`).', 'Send new users to an onboarding page.', 'Same reason as above.', '/welcome')
n('Redirects.RedirectAfterLogout', 'Where the hosted logout page goes when no valid post-logout address is given.', 'Set it to your public home page.', 'Same reason as above.', '/')

# Clients
n('Clients.AllowedClientTypes', 'Intended as the list of kinds of applications that may be created in the project. **Today no code reads it.**', 'Leave the default.', 'Stored and shown in the admin settings page only.', ['Api', 'Mvc'])
n('Clients.PermissionSyncAllowed', 'Lets applications push permissions (and, for roles that name unknown permissions, create them). Off answers `sync_not_allowed`.', 'Turn off when admins own the permission list.', 'Gives you control over who may create permissions.', True)
n('Clients.ScopeSyncAllowed', 'Lets applications push custom scopes and allow them for themselves.', 'Turn off when scopes are managed by admins.', 'A scope decides what a token may call, so you may want to approve them.', True)
n('Clients.GroupSyncAllowed', 'Lets applications push groups. Off by default. Push only adds missing groups.', 'Turn on when your code, not admins, defines the groups.', 'Admins normally own groups because they decide who is in them.', True)
n('Clients.RoleSyncAllowed', 'Lets applications push roles.', 'Turn off when admins own the roles.', 'Roles carry rights; some teams want to approve each one.', True)
n('Clients.AllowRemovePermissionsFromSync', 'Lets a push remove the permissions of a role that are not in the pushed list. Without it a push only adds.', 'Turn on when the code is the single source of truth.', 'The safe default never removes access by surprise.', True)
n('Clients.AllowProjectAdminRotateSecret', 'Lets a project admin (not only a global admin) rotate the secret of an application of their own project.', 'Turn on for teams that run their own applications.', 'A new secret stops the old one at once, so it is global-admin only by default.', True)
n('Clients.DefaultUiMode', 'The project\'s intended sign-in mode (`Hosted`, `Self`, `None`). An application can override it. **The Server does not enforce it**: the Client package chooses with `UseHostedUi()` / `UseSelfUi()`. The enforced switch is the application\'s `AllowSelfUiLogin`. See [enums-reference.md](enums-reference.md#uimode).', 'As documentation of the plan for the project.', 'Shows admins how applications are meant to sign in.', 'Hosted')

# Localization
n('Localization.DefaultCulture', 'The language used when nothing better is known (must be in `SupportedCultures`).', 'Set to the main language of your users.', 'The last step of the language choice ([localization.md](localization.md)).', 'fa')
n('Localization.SupportedCultures', 'The languages the project offers. A request is matched against this list.', 'Add a language once its texts exist.', 'A language without texts would show English keys.', ['en', 'fa'])
n('Localization.BrandName', 'The name used in emails and consent pages. When empty the project name is used.', 'Set it to the customer-facing product name.', 'Emails should say "PaperTrade", not the internal project key.', 'PaperTrade')
n('Localization.SupportEmail', 'Intended as the support address shown to users. **Today no code reads it.**', 'Store it for your own use.', 'Stored and shown in the admin settings page only.', 'help@papertrade.test')

# External providers (handled separately)
n('ExternalProviders', 'The list of external sign-in providers (Google, Microsoft, GitHub, Apple, Facebook, OpenID Connect, SAML, LDAP). Changed **only** through the provider admin routes, which check each field and encrypt secrets; the generic settings route keeps the list as it is.', 'Use the admin API or the Providers page.', 'Providers have secrets and extra rules, so they have their own routes. Fields: [settings-external-providers.md](settings-external-providers.md).', None)

# Messaging
n('Messaging.Email.Provider', 'The email provider of the project: `logging`, `smtp`, `google` or your own name. Empty means "use the server default". See [email-and-sms.md](email-and-sms.md).', 'To send real email.', 'Each project (and application) may use its own mail account.', 'smtp')
n('Messaging.Email.Host', 'The SMTP server name (not used by `google`).', 'With the `smtp` provider.', 'Where the mail is handed in.', 'smtp.example.com')
n('Messaging.Email.Port', 'The SMTP port (1 to 65535).', 'Match the security mode: 587 for STARTTLS, 465 for TLS.', 'A wrong port is the usual reason for a timeout.', 587)
n('Messaging.Email.Security', 'How the connection is secured: `StartTls`, `SslOnConnect` or `None`. See [enums-reference.md](enums-reference.md#smtpsecurity).', 'Use `None` only for a trusted local relay.', '`None` sends the password in clear text.', 'StartTls')
n('Messaging.Email.UserName', 'The SMTP login (for `google` the Gmail address).', 'When the server asks for a login.', 'Authenticates the sender.', 'mail@example.com')
n('Messaging.Email.PasswordProtected', 'The SMTP password, **encrypted**. Never returned; set with the messaging admin route (`password`).', 'Rotate when the password changes.', 'The password must not sit in clear text in the database or in an answer.', None)
n('Messaging.Email.FromAddress', 'The address in the From header (for `google` it defaults to the user name).', 'Always for `smtp`.', 'Receivers check it; use an address your domain may send from.', 'noreply@example.com')
n('Messaging.Email.FromName', 'The display name in the From header; empty uses the brand name or project name.', 'To control how the sender looks.', 'Recognisable senders get fewer "spam" complaints.', 'PaperTrade')
n('Messaging.Email.TimeoutSeconds', 'The time limit of one send (1 to 120).', 'Raise for slow relays.', 'A stuck mail server must not hold the request forever.', 20)
n('Messaging.Sms.Provider', 'The SMS provider name (`logging` or your own). Empty means "use the server default".', 'When you add a real SMS provider.', 'Same idea as for email.', 'logging')
n('Messaging.Sms.Sender', 'The sender id or number shown to the user, for providers that support it.', 'With a real provider.', 'Branded sender ids build trust.', 'PaperTrade')

# Passkeys
n('Passkeys.AllowPasskeys', 'Turns passkeys on for the project. Also needs `Auth2:Passkeys:ServerDomain` on the server and `Passkey` in `Methods.AllowedLoginMethods`.', 'To offer passkey sign-in ([passkeys.md](external-identity/passkeys.md)).', 'Off by default: passkeys are bound to a domain, so turn them on on purpose.', True)
n('Passkeys.PasskeyUserVerification', 'Whether the device must verify the person (fingerprint, face, PIN): `required`, `preferred` or `discouraged`. With `required` a passkey without verification is refused at registration and sign-in.', 'Keep `required`.', 'Verification makes a passkey count as several factors.', 'required')
n('Passkeys.PasskeyResidentKey', 'Whether the passkey is stored on the device so it can sign in without a user name: `required`, `preferred` or `discouraged`.', '`required` for username-less sign-in.', 'Resident keys enable the "just touch the sensor" experience.', 'preferred')
n('Passkeys.MaxPasskeysPerUser', 'The most passkeys one person may have in the project.', 'Rarely.', 'Bounds the data a person can store.', 5)
n('Passkeys.PasskeyNameMaxLength', 'The longest name a person may give a passkey.', 'Rarely.', 'Bounds the data.', 40)
n('Passkeys.RequirePasskeyBackupMethod', 'A person whose only other way to sign in is one external login cannot remove that login while they have one passkey; they must add a second passkey or another method first.', 'Keep on.', 'A lost device must not lock the person out for good.', True)

n('CredentialScope', 'Overrides where passwords and two-factor live for this project (`PerProject` or `Shared`), when the server allows overrides (`Auth2:AllowCredentialScopeOverride`). `null` uses the server value. See [credential-scope.md](credential-scope.md).', 'Almost never; choose it once for the whole server.', 'Changing it later moves where credentials are read, so existing passwords stop working.', None)
