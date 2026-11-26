# Accounts, organizations and administration

Staff sign in to the dashboard with an account. Each account belongs to one or more
**organizations** (tenants); every request is scoped to the caller's current organization, and the
API answers `404` for any id outside it, so another organization's ids look like ids that don't
exist. Reporters never have accounts: they use the separate reporter app and the intake API
(requirements §3, NFR-SEC-7).

There are two kinds of role:

- **Organization roles** (below) decide who can manage the organization: its members and
  invitations.
- **Testigo staff roles** (Analyst, Reviewer, Intake advocate; requirements §3 and NFR-SEC-8)
  decide who can read and work cases. They are separate: being an organization Admin doesn't let
  anyone read a report. Reading cases also needs two-factor sign-in when
  `Testigo:Staff:RequireTwoFactor` is on (the default), and every read is recorded in the audit log.

## People and roles

| Role | Can |
|---|---|
| Viewer | Sign in and see the organization and its members. Cases need a staff role on top. |
| Member | The same; the usual role for analysts and reviewers. |
| Admin | Also invite, remove and change the role of members, and grant staff roles. |
| Owner | Also manage who is an owner. An organization always keeps at least one owner. |

**Accounts** are email and password.
- Passwords are hashed with PBKDF2-SHA256 at 600,000 iterations.
- Sign-in and sign-up are rate limited per client address.
- An unknown email takes as long to reject as a wrong password.
- Wrong passwords are counted per account **and address**: after `Auth:WrongPasswordsPerAccount`
  (10) in 15 minutes, that address waits, while the account's owner can still sign in from theirs.
  So knowing someone's email isn't enough to lock them out. Guessing spread over many addresses
  locks the account itself at ten times that.
- Staff join by invitation: Docker Compose sets `Auth:AllowSignup=false`. If sign-up is on, one
  address can create `Auth:SignUpsPerAddressPerDay` (10) new organizations a day; joining by
  invitation doesn't count. Emails are confirmed either way ([below](#email-verification)).
- For both counts an IPv6 address stands for its whole /64 network, since whoever has one address
  in it has all of them.

**Sessions** are random tokens in an `HttpOnly`, `SameSite=Lax` cookie (`Secure` over HTTPS).
- Only their SHA-256 is stored.
- They slide for `Auth:SessionHours` (8 by default, NFR-SEC-8).
- Changing a password signs out every other browser. Removing a member ends their sessions in
  that organization.

**Two-factor sign-in** is optional, per person: see [below](#two-factor-sign-in).

**Invitations** are one-time links, valid for `Auth:InvitationDays` (7 by default).
- A link works only for the email it was sent to.
- Admins can't invite anyone with more access than they have themselves.

**Local admin.** Docker Compose and the Development environment create a built-in account at
startup: sign in with username `admin` and password `admin` (the username goes in the email field).
It owns the default organization and is an operator. It's created only if missing, so a changed
password survives restarts. Set `LOCAL_ADMIN_USERNAME=` (empty) on any server others can reach.

### Email verification

A sign-up has to prove it owns its email before the account exists, so nobody can sign up with
someone else's address, and an address nobody confirmed never holds an account that blocks its
owner from signing up. Invitations confirm too: an admin can copy an invitation link and pass it
on, so having the link doesn't prove who owns the address it names.

1. The sign-up form runs every check (email, password strength, the address isn't taken, and
   either the invitation is still open and names this address, or sign-up is allowed and the
   per-address limit isn't reached), then emails a link instead of creating the account. The
   response is `{verification_required, email, resend_id, expires_in_hours}` and sets no cookie;
   the page says **Check your email**.
2. Opening the link (`/verify-email?token=…`, on any device) asks for the password chosen at
   sign-up. Nothing happens when the page loads, so a mail scanner that opens links to check them
   doesn't use the link up. With the right password the checks run again (an invitation revoked
   or expired since is refused), the account is created in its new organization or the one it was
   invited to, and it's signed in there.
3. **Send a new link** (once a minute) replaces that sign-up's link. The old one stops working
   only once the new email has gone; if it can't be sent, the old link still works.

Each sign-up keeps its own link and password (stored as hashes in `PendingSignUps`, never the
token itself). The password asked for at the link is what ties it to the person who signed up:
someone who signs up with another person's address gets an email sent to that person, and if they
open it, their own password doesn't finish the stranger's sign-up. A wrong password leaves the link
working for up to five tries.

Sign-ups for one address from different networks don't hold each other back, so a stranger can't
keep the owner from signing up. From one network, an address gets at most one email a minute, and
signing up again replaces that network's earlier sign-up. An address can have sign-ups from up to
ten networks waiting at once. Links work for 24 hours; confirming one ends every sign-up waiting for
that address.

The server's first account (the setup token) skips it, as do accounts created before it was turned
on. `Auth:EmailVerification` is `auto` by default: on when the server can send email
(`EMAIL_SMTP_*`), off otherwise. `required` turns it on without email, writing each link to the
API's log for the operator to pass on; `off` turns it off.

**Self-hosting alone?** `Auth:Mode=disabled` skips sign-in entirely and makes everyone the owner of
the default organization. Use it only on a machine nobody else can reach.

## Two-factor sign-in

Anyone can turn on two-factor sign-in under **Settings → Account**. After that, signing in takes
the password and then a 6-digit code from an authenticator app (Google Authenticator, 1Password,
Authy, Microsoft Authenticator and others).

**Turning it on.**
1. Enter your password.
2. Scan the QR code, or type the key shown under it.
3. Enter the app's current code to confirm.
4. Save the 10 recovery codes it shows. They're shown once.

Turning it on signs out every other browser. Turning it off, or making new recovery codes, needs
the password and a current code (or a recovery code). Changing the password ends any sign-in
that's waiting for its code.

**Signing in.** A right password on such an account opens no session. It returns a short-lived
challenge instead, and the code step completes it:
- the challenge lasts 5 minutes and allows 5 wrong codes, then the password is needed again;
- after 10 wrong codes across sign-ins within 15 minutes, the account waits 15 minutes before
  signing in again, so someone who has the password can't keep starting new sign-ins to guess;
- each attempt is counted, and each code used up, in one database statement, so requests sent at
  the same moment can't get past either limit or use a code twice;
- codes are accepted one 30-second step either side, for clock drift;
- each code works once, so a code someone saw over your shoulder is already spent (a code that
  was just used gets "wait for the next code", and doesn't count as a wrong one);
- a recovery code (`xxxxx-xxxxx`, any case, dashes optional) works in place of a code, once.

**Lost the phone and the recovery codes?** An operator turns it off from Platform admin →
Overview → **Locked out of two-factor sign-in**, by email (or the local admin's username). That
signs the person out everywhere, and the password alone signs in again. The reset is in the audit
log as `user.two_factor_reset`. An account that doesn't have it on is left alone (`409`).

**Notices.** With outgoing mail set up, people are emailed when two-factor sign-in is turned on,
turned off, reset by an operator, or when a recovery code is used.

**How it's stored.**
- The app's secret is encrypted with the secrets master key (AES-GCM).
- Recovery codes and challenges are stored only as SHA-256 hashes.
- Codes follow RFC 6238: HMAC-SHA1, 6 digits, 30 seconds.

`Auth:Mode=disabled` never asks for codes.

## Deleting your account

**Settings → Account → Delete your account** deletes the signed-in person's account. Before
asking for anything, it shows what happens to each organization they belong to:

| Their organization | What happens |
|---|---|
| Shared, and someone else is also an owner, or they aren't an owner | They leave it. Their work there stays with the organization, which owns it. |
| Shared, and they're its only owner | **Blocks the deletion.** An organization always keeps an owner: make another member an owner, or remove the others, first. |
| Nobody else belongs to it | **Closed with the account**, since nobody could reach it again (below). |

It asks for the password and for "delete my account" to be typed. Then:
1. **Each closed organization is suspended**, with the reason "Closed: its only member deleted
   their account", and its open invitations are revoked. Its cases and audit log are kept,
   untouched, for a platform operator to deal with: a person leaving never erases reports.
2. **The account goes.** Its memberships, sessions, pending sign-ins and open invitations for its
   email. Every browser is signed out. The email can sign up again as a new account.

A platform operator's account (an email in `PLATFORM_ADMIN_EMAIL` / `Auth:PlatformAdmins`, or the
local admin) can't be deleted this way: remove it from the operators first.

## Platform administration

Operators (the emails in `PLATFORM_ADMIN_EMAIL` / `Auth:PlatformAdmins`, and the local admin) get
a **Platform admin** item in the sidebar (`/admin`). It covers every organization on the server.

A sign-up can't claim an operator's email, confirmed or not: it is accepted
only as the server's first account. To make someone else an operator, have them create their
account (or accept an invitation) first, then add their email to `PLATFORM_ADMIN_EMAIL`.

| Tab | What you can do |
|---|---|
| Overview | Organization and member counts, the latest operator actions, and [which version is running](#which-version-is-running). **Locked out of two-factor sign-in**: turn it off for someone who lost their phone ([two-factor sign-in](#two-factor-sign-in)). |
| Organizations | Search by name, id or member email. Open one to rename it, add people with any role, change roles, remove people, revoke invitations, and suspend or reactivate it. **New organization** creates one with its first owner. |

**Adding people.** Someone who already has an account joins straight away. Anyone else gets an
invitation, so only they ever choose their password; operators never set or see passwords. When
outgoing mail is set up (`Email:Smtp`), the invitation is emailed. Otherwise the portal shows the
one-time link for you to send. A new invitation for the same person and organization revokes any
older open one.

**Suspending** an organization is reversible and deletes nothing. While it's suspended:
- its members are signed out of it, and sign-in skips it (someone with no other organization is
  told it's suspended);
- its invitations can't be accepted;
- its cases stay as they are, and reports already received are kept.

An organization that an operator belongs to can't be suspended, so nobody can lock themselves out.
Every organization keeps at least one owner.

### Which version is running

The **Version** card on the Overview tab shows:
- the server's commit;
- when that build was made, and when the server started;
- the commit the website was built from.

If the website and the server were built from different commits, a warning says so. This usually
means a deploy didn't finish.

A build can be stamped with its commit and build time:
- the API image through `APP_VERSION` and `APP_BUILT_AT`, which become `App:Version` and
  `App:BuiltAt`;
- the web image through `NEXT_PUBLIC_APP_VERSION`.

A server built from a git checkout without them shows the commit the .NET SDK recorded. A build
with neither says it isn't stamped. The same details are in `GET /api/admin/overview` as `version`.

**Audit.** Every operator action is appended to the hash-chained audit log under the `platform`
scope: who did it, to which organization, and when.

## Security notes

- **Cookies and CSRF.** The dashboard's cookie is `SameSite=Lax`, so other sites can't send it on
  cross-site `POST`s. The API only lets the configured dashboard origins (`Cors:AllowedOrigins`)
  send credentials. Serve both over HTTPS in production.
- **Behind a reverse proxy.** When a proxy terminates TLS (Caddy in `docker-compose.prod.yml`),
  the API sees plain HTTP from the proxy's address. Set `ForwardedHeaders:Enabled` so it reads the
  proxy's `X-Forwarded-Proto` and `X-Forwarded-For`. Then the session cookie is `Secure`, the links
  it builds use https, and the sign-in rate limit counts each visitor rather than the proxy. Only
  proxies in `ForwardedHeaders:TrustedNetworks` (default: the private ranges Docker uses) are
  believed, so the API itself must not be reachable from outside. `Auth:CookieSecure=always` marks
  the cookie `Secure` whatever the request looks like. The production overlay sets both.
- **One model provider for the server.** The model settings (`Llm:*`) are the server's, shared by
  every organization, and only redacted report text is ever sent to a model
  ([llm-settings.md](llm-settings.md), [data-protection.md](data-protection.md)).

## Configuration

| Setting | Env (compose) | Default | |
|---|---|---|---|
| `Auth:Mode` | | `accounts` | `disabled`: no sign-in, single user. |
| `Auth:AllowSignup` | | `true` (`false` in compose) | `false`: invitation only. The first account can still be created, with the setup token. |
| `Auth:EmailVerification` | `AUTH_EMAIL_VERIFICATION` | `auto` | Sign-ups, invited or not, confirm their email before the account exists ([email verification](#email-verification)): `auto` when email is set up, `required` always (links go to the API's log without email), `off` never. |
| `Auth:WrongPasswordsPerAccount` | | `10` | Wrong passwords one account may get in 15 minutes before sign-in to it pauses for the rest of them, wherever they come from. `0`: no limit. Counted per API process. |
| `Auth:SetupToken` | `SETUP_TOKEN` | generated | What creating the first account on a server with none takes. Empty: generated at each start and written to the API's log. |
| `Auth:SetupTokenRequired` | | `true` | `false`: anyone may create the first account. Only for a machine nobody else can reach. |
| `Auth:PlatformAdmins` | `PLATFORM_ADMIN_EMAIL` | | Operators: the Platform admin portal. |
| `Email:Smtp:Host` / `Port` / `Tls` / `Username` / `Password` / `From` | `EMAIL_SMTP_HOST`, `EMAIL_SMTP_PORT`, `EMAIL_SMTP_TLS`, `EMAIL_SMTP_USERNAME`, `EMAIL_SMTP_PASSWORD`, `EMAIL_FROM` | (off) / 587 / starttls | The server's own mail: invitations and security notices. |
| `Auth:LocalAdmin:Username` / `Password` | `LOCAL_ADMIN_USERNAME` / `LOCAL_ADMIN_PASSWORD` | `admin` / `admin` in compose and Development; none otherwise | Built-in local sign-in. |
| `Auth:SessionHours` / `Auth:InvitationDays` | | 8 / 7 | A session's sliding lifetime; how long an invitation works. |
| `Auth:SignInAttemptsPerMinute` | | 10 | Requests per client address per minute to the endpoints that check a password or code: sign-in and its code step, sign-up and its confirmation link and resend, password change, invitation links, and two-factor setup, enable, disable and new recovery codes. |
| `Auth:CookieSecure` | | `auto` | `always`: the session cookie is always `Secure` (`auto`: when the request was HTTPS). |
| `ForwardedHeaders:Enabled` | | `false` | Trust a TLS-terminating proxy's `X-Forwarded-Proto` / `X-Forwarded-For`. `true` in `docker-compose.prod.yml`. |
| `ForwardedHeaders:TrustedNetworks` | | 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16 | CIDR ranges the proxy connects from. |

## API

The staff API is described at `/openapi/v1.json`. The case endpoints (`/api/testigo/…`) and the
intake endpoints (`/api/intake/…`, for the reporter app only) are covered in the requirements
(§5) and in the code; these are the account ones.

| Method | Path | Role | |
|---|---|---|---|
| `POST` | `/api/auth/signup` | anyone | `{email, password, name?, organization?, invitation?}`. With [email verification](#email-verification) it returns `{verification_required, email, resend_id, expires_in_hours}` and no cookie. |
| `POST` | `/api/auth/signup/verify` | anyone | `{token, password}`: the emailed link's token and the password chosen at sign-up. Creates the account and sets the session cookie. A wrong password is `422` and the link still works; a link that doesn't is `400`. |
| `POST` | `/api/auth/signup/resend` | anyone | `{id}` (the `resend_id`): a new link, at most once a minute. |
| `POST` | `/api/auth/login` / `logout` | anyone | Sets or clears the session cookie. |
| `POST` | `/api/auth/login/two-factor` | anyone | `{challenge, code}`: the code step, after `login` returned `{two_factor_required, challenge}`. |
| `GET` | `/api/auth/me` | anyone | The caller, their organizations, and the server's auth mode. |
| `POST` | `/api/auth/switch` | session | `{tenant_id}` |
| `POST` | `/api/auth/password` | session | `{current_password, new_password}` |
| `GET` | `/api/auth/two-factor` | session | `{enabled, enabled_at, recovery_codes_left}` |
| `POST` | `/api/auth/two-factor/setup` / `enable` | session | `setup {password}` returns `{secret, otpauth_uri, qr_svg}`; `enable {code}` returns `{recovery_codes}`, once. |
| `POST` | `/api/auth/two-factor/disable` / `recovery-codes` | session | `{password, code}`; `recovery-codes` returns new `{recovery_codes}`. |
| `GET` / `POST` | `/api/auth/invitations/{token}` / `accept` | anyone / session | |
| `GET` | `/api/account/deletion` | session | What deleting the account would do: `{allowed, is_operator, leaves, closes, blocks}`. |
| `POST` | `/api/account/delete` | session | `{password}`. Deletes the account and clears the session cookie. |
| `GET` / `PATCH` | `/api/organization` | Viewer / Admin | |
| `GET` / `PUT` / `DELETE` | `/api/organization/members[/{id}[/role]]` | Viewer / Admin | Anyone may remove themselves. |
| `GET` / `POST` / `DELETE` | `/api/organization/invitations` | Admin | `POST` returns the token once. |
| `GET` | `/api/admin/overview` | operator | Counts, mail status, recent operator activity, and `version`. |
| `POST` | `/api/admin/users/two-factor/reset` | operator | `{email}`: turns off someone's two-factor sign-in and signs them out. |
| `GET` / `POST` | `/api/admin/organizations` | operator | `?q=&status=`; `POST {name, owner_email}`. |
| `GET` / `PATCH` | `/api/admin/organizations/{id}` | operator | Members and invitations; `PATCH {name}`. |
| `POST` | `/api/admin/organizations/{id}/suspend` / `reactivate` | operator | `suspend {reason?}` |
| `POST` / `PUT` / `DELETE` | `/api/admin/organizations/{id}/members[/{userId}[/role]]` | operator | `POST {email, role}` adds or invites. |
| `DELETE` | `/api/admin/organizations/{id}/invitations/{invitationId}` | operator | |
| `GET` | `/openapi/v1.json` | anyone | |

## Tests

- **Unit:** `PlatformTests` (password hashing and rehashing, token hashes), `TotpTests` (RFC 6238
  vectors, the drift window, replay, base32, the app link), `LocalAdminGuardTests`,
  `PlatformEmailTests`.
- **Integration:** `PlatformAdminTests` (operators only; suspension cuts off sessions and sign-in
  until reactivation; organizations keep an owner), `EmailVerificationApiTests`,
  `AccountDeletionTests`, `TwoFactorTests`, `SignInHardeningTests`, `SetupTokenTests`,
  `LocalAdminTests`, `ProxyHeadersTests`, and `TestigoFlowTests` for the case and intake flow.
