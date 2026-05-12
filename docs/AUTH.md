# Authentication and Authorization

End-to-end reference for `Pam.Identity` — the back-office auth module.
This doc covers what's actually shipped today (Phase 3, three PRs) and
the contracts other modules consume.

**Scope.** Back-office Users & Agents only. The Players module returns
in Phase 4 and will use the *same* OpenIddict server under a different
audience (`pam_player_api`). Until then, every `/v1/*` endpoint that
isn't `.AllowAnonymous()` requires a back-office token.

---

## The stack

| Layer | What it does | Where it lives |
|---|---|---|
| **OpenIddict** | OAuth 2.0 / OIDC token issuance + validation. AuthorizationCode + PKCE + Refresh. | `IdentityModule.cs` (`AddOpenIddict().AddCore/AddServer/AddValidation`) |
| **ASP.NET Core Identity** | User storage, password hashing, lockout, TOTP. | `BackOfficeUser : IdentityUser<Guid>`, `IdentityDbContext` |
| **Cookie auth** | The IDP session. Set on `POST /v1/identity/login`, read by `/connect/authorize`. | `ConfigureApplicationCookie` in `IdentityModule.cs` |
| **Bearer tokens** | API auth. Sent as `Authorization: Bearer <access_token>` on `/v1/*`. | `OpenIddictValidation...` scheme in `Program.cs` |
| **Per-permission policies** | Fine-grained authz. One policy per code in `PermissionCodes`. | `AddAuthorization` in `Program.cs` |
| **HttpUserContext** | Translates the OIDC `sub` to an `Actor` for audit columns. | `Pam.Identity/Authentication/HttpUserContext.cs` |

Single host, single database. Identity tables, OpenIddict tables, and
the custom permissions table all cohabit `IdentityDbContext` in schema
`identity`. No separate IDP process, no separate DB.

See [DECISIONS.md #16](DECISIONS.md) for why this beats an external IDP.

---

## DB layout (schema `identity`)

```
users                       BackOfficeUser : IdentityUser<Guid> + BrandId + audit
roles                       IdentityRole<Guid>
user_roles                  user ↔ role (M:N)
user_claims, role_claims    per-user / per-role claims (unused today)
user_logins                 external SSO links (unused today, see Federation)
user_tokens                 TOTP secret + "remember this machine" cookie pin

permissions                 custom — string codes from PermissionCodes
role_permissions            role ↔ permission (M:N)

openiddict_applications     OAuth clients (pam-bo today)
openiddict_scopes           pam_api + standard OIDC scopes
openiddict_authorizations   one row per (user, client, scopes) grant
openiddict_tokens           every access / refresh / authcode ever issued
```

The Quartz cleanup job (`UseQuartz()` in `AddCore`) prunes expired and
revoked tokens hourly, so `openiddict_tokens` tracks active sessions,
not all-time history.

---

## Roles + permissions

Two layers of authz, both project as claims at token-issuance time.

**Roles** (coarse, seeded at startup):

| Role | Default permissions |
|---|---|
| `Owner` | All of them, including `platform.admin` |
| `Manager` | `operators.brands.*`, `identity.users.*`, `identity.roles.write` |
| `Operator` | `operators.brands.read`, `identity.users.read` |
| `Accountant` | `operators.brands.read` |

**Permissions** (fine-grained, declared in
`Pam.Identity.Contracts.Permissions.PermissionCodes`):

```
operators.brands.read
operators.brands.write
identity.users.read
identity.users.write
identity.roles.write
platform.admin              ← meta: grants every other policy via assertion
```

Endpoints gate on permissions, not roles:

```csharp
app.MapPost("/v1/identity/users", ...)
    .RequireAuthorization($"Permissions.{PermissionCodes.Identity.UsersWrite}");
```

`platform.admin` is an Owner-only escape hatch — the policy
authorization handler accepts the request if the user has the specific
permission **OR** `platform.admin`. See [DECISIONS.md #20](DECISIONS.md).

---

## Flows

### Bootstrap — first run

Env vars seed the very first Owner so somebody can log in:

```
PAM_BOOTSTRAP_OWNER_EMAIL=owner@you.local
PAM_BOOTSTRAP_OWNER_PASSWORD=<initial>
```

`IdentitySeeder.SeedBootstrapOwnerAsync` runs at API startup:

1. If any user with the `Owner` role already exists → skip.
2. Else, if either env var is missing → log a warning and skip (the
   API still boots, but no one can log in until you provision an Owner).
3. Else, create the user with `EmailConfirmed = true` and assign the
   `Owner` role.

After the first Owner exists, the env vars are ignored on subsequent
restarts. Every other back-office user is created via
`POST /v1/identity/users`. There is **no self-service back-office
registration** — that's a privilege-boundary breach. See ROADMAP.

### Login — no MFA

```
                                            ┌─────────┐
SPA browser                                 │  API    │
─────────────                               └─────────┘
1. user types email + password
2. POST /v1/identity/login  ───────────────▶ SignInManager.PasswordSignInAsync
                                              · validate credentials
                                              · check lockout
                                              · set the auth cookie
                                              ◀─── 204 + Set-Cookie
3. navigate to                                .AspNetCore.Identity.Application
   GET /connect/authorize?
       client_id=pam-bo&
       response_type=code&
       scope=openid+profile+email+
             roles+pam_api+offline_access&
       redirect_uri=spa://callback&
       code_challenge=<S256 of verifier>&
       code_challenge_method=S256
                              ───────────────▶ AuthorizationController.Authorize
                                                · cookie validates → user known
                                                · BuildIdentityAsync (claims)
                                                ◀─── 302 spa://callback?code=…
4. POST /connect/token         ───────────────▶ AuthorizationController.Exchange
   grant_type=authorization_code                · refresh claims from live user
   code=<the code>                              · SetDestinations
   code_verifier=<original verifier>            ◀─── { access_token, refresh_token,
                                                       id_token, expires_in: 3600 }
5. API calls                  ──Bearer──────▶  /v1/identity/users
                                              · OpenIddict validation:
                                                  - signature
                                                  - expiry
                                                  - authorization entry (DB)
                                                  - token entry (DB)
                                                · per-permission policy check
                                                ◀─── 200 OK
```

The cookie is set in step 2 and persists for the entire IDP session.
The access token is in SPA memory and lives ~1 hour; the refresh token
lives ~14 days. See [Token lifecycle](#token-lifecycle).

### Login — with MFA (TOTP)

User had previously enrolled an authenticator. Login bifurcates after
the password check:

```
1. POST /v1/identity/login                  PasswordSignInAsync detects
                                            TwoFactorEnabled=true → returns
                                            SignInResult.TwoFactorRequired
                                            ◀─── 200 { mfaRequired: true }
                                                + partial 2FA cookie
2. user opens authenticator, types code
3. POST /v1/identity/login/mfa
   { code: "123456", rememberMachine: false }
                              ───────────────▶ TwoFactorAuthenticatorSignInAsync
                                                · reads the partial cookie
                                                · verifies TOTP
                                                · sets the full auth cookie
                                                ◀─── 204
4. continue at step 3 of the no-MFA flow (GET /connect/authorize)
```

`rememberMachine: true` writes a long-lived "two-factor-remembered"
cookie that bypasses MFA on this browser for the next login. Tracked in
`user_tokens` (provider `[AspNetUserStore]`, name `RememberClient`).

### Login — with a recovery code

User lost their authenticator. Same partial-cookie flow:

```
1. POST /v1/identity/login                 ◀─── 200 { mfaRequired: true }
2. user pulls saved recovery codes
3. POST /v1/identity/login/recovery-code
   { code: "abcde-fghij" }                  TwoFactorRecoveryCodeSignInAsync
                                            · verifies one-time code
                                            · marks the code redeemed
                                            ◀─── 204
4. continue at step 3 of the no-MFA flow
```

Each recovery code is **one-time use** — Identity stores them hashed and
marks them as consumed on use.

### Token refresh (silent renewal)

The SPA refreshes its access token in the background before the current
one expires:

```
POST /connect/token
  grant_type=refresh_token
  refresh_token=<refresh>
  client_id=pam-bo
                              ───────────────▶ AuthorizationController.Exchange
                                                · refresh-token grant detected
                                                · CanSignInAsync(user) → still OK?
                                                · re-mint claims from live user
                                                  (roles, permissions, brand may
                                                   have changed)
                                                · old refresh-token row marked
                                                  redeemed, new pair issued
                                                ◀─── { access_token, refresh_token }
```

**Refresh-token rotation is on by default.** Each refresh invalidates
the previous refresh token, so replaying a captured refresh token after
the user has refreshed once gets the attacker rejected.

### Logout

Three things you might want to mean by "logout":

| Want | Endpoint(s) | What it does |
|---|---|---|
| End the cookie session | `GET\|POST /connect/logout` | `SignInManager.SignOutAsync()` clears the auth cookie + the OIDC `id_token_hint` flow redirects to `post_logout_redirect_uri`. Existing access tokens still work for their TTL. |
| Kill the refresh token | `POST /connect/revocation` (token=refresh) | Marks the refresh row revoked. Combined with `EnableTokenEntryValidation`, the access token issued from it fails at the next API call. |
| Kill everything for this user | Admin action: `DELETE /v1/identity/users/{id}` or `POST /v1/identity/users/{id}/mfa/reset` | Rotates the security stamp + revokes outstanding authorizations. Instant when entry validation is on. |

SPA logout should call **both** `/connect/logout` and
`/connect/revocation` (with the refresh token) for proper hygiene.

### Self-service: change password

```
POST /v1/identity/me/change-password
  { currentPassword, newPassword }     [authenticated, rate-limited 5/min/sub]
                              ───────────────▶ ChangePasswordHandler
                                                · UserManager.ChangePasswordAsync
                                                · security stamp auto-rotated
                                                ◀─── 204
```

`CurrentPassword` is required even when authenticated — a captured
session cookie shouldn't be able to silently change the password.

### Self-service: forgot / reset password

```
1. POST /v1/identity/forgot-password    [anonymous, rate-limited]
   { email }
                              ───────────────▶ ForgotPasswordHandler
                                                · find user by email
                                                · IF user exists AND email
                                                  is confirmed:
                                                    - generate reset token
                                                    - email link
                                                · ALWAYS 204
                                                ◀─── 204

2. user clicks link in email
   GET https://spa/reset-password?email=…&token=…
3. SPA shows new-password form
4. POST /v1/identity/reset-password      [anonymous, rate-limited]
   { email, token, newPassword }
                              ───────────────▶ ResetPasswordHandler
                                                · UserManager.ResetPasswordAsync
                                                · security stamp rotated
                                                ◀─── 204 or 422
```

**Anti-enumeration design** — `/forgot-password` always returns 204,
even for unknown emails or unconfirmed accounts. SMTP failures are
caught + logged so the response shape stays constant under outage; the
email address is **not** logged (that's the PII this endpoint protects).
See [ForgotPasswordHandler](../src/Modules/Identity/Pam.Identity/Authentication/ForgotPassword/ForgotPasswordHandler.cs).

### Email confirmation

A new admin-created user starts with `EmailConfirmed = false`. The
confirmation flow:

```
admin: POST /v1/identity/users        ───▶  CreateUserHandler
                                              · UserManager.CreateAsync
                                              · AddToRolesAsync
                                              · AUTO-FIRE SendConfirmationEmailCommand
                                                  (logged-only on SMTP failure;
                                                   admin can retry below)
                                              ◀─── 201
                                              ────── email sent ──────▶
                                                                       (user's inbox)
user clicks link from email:
GET https://spa/confirm-email?email=…&token=…
SPA: POST /v1/identity/confirm-email   ◀──   { email, token }
                                              · UserManager.ConfirmEmailAsync
                                              ◀─── 204

admin retry (if email lost / SMTP was down):
POST /v1/identity/users/{id}/send-confirmation-email   [identity.users.write]
                                              · idempotent — no-op if already confirmed
                                              ◀─── 204
```

**Gotcha for production.** `options.SignIn.RequireConfirmedEmail =
false` is currently set in `IdentityModule.cs`. That means a created-
but-unconfirmed user CAN log in. To make confirmation mandatory before
login, flip it to `true`. The bootstrap Owner is fine either way
(seeded with `EmailConfirmed = true`).

### MFA — enroll

```
POST /v1/identity/me/mfa/enroll                [authenticated]
                              ───────────────▶ MfaEnrollHandler
                                                · GetAuthenticatorKeyAsync (or
                                                  ResetAuthenticatorKeyAsync
                                                  if first time)
                                                · build otpauth:// URI
                                                ◀─── { sharedKey, authenticatorUri }
SPA renders the URI as a QR code or shows the
sharedKey for manual entry. User scans into
Authenticator app, gets a 6-digit code.

POST /v1/identity/me/mfa/verify                [authenticated, rate-limited]
  { code: "123456" }
                              ───────────────▶ MfaVerifyHandler
                                                · VerifyTwoFactorTokenAsync
                                                · SetTwoFactorEnabledAsync(true)
                                                ◀─── 204
```

**The authenticator key persists** until `SetTwoFactorEnabledAsync(true)`
succeeds — re-calling `/enroll` returns the same key, not a fresh one.
That's intentional: the user might close the QR modal before scanning
and need to come back. Only after the user explicitly disables MFA and
re-enrolls does a new key get generated.

### MFA — recovery codes

```
POST /v1/identity/me/mfa/recovery-codes        [authenticated, rate-limited]
   (requires TwoFactorEnabled=true)
                              ───────────────▶ GenerateRecoveryCodesHandler
                                                · GenerateNewTwoFactorRecoveryCodesAsync(user, 10)
                                                ◀─── { codes: [10 codes] }
```

Plaintext returned **once.** Codes are stored hashed in `user_tokens`.
Re-issuing invalidates the previous batch.

### MFA — disable (self)

```
POST /v1/identity/me/mfa/disable               [authenticated, rate-limited]
  { currentPassword }
                              ───────────────▶ MfaDisableHandler
                                                · CheckPasswordAsync (challenge)
                                                · SetTwoFactorEnabledAsync(false)
                                                · ResetAuthenticatorKeyAsync
                                                  (so re-enable starts fresh)
                                                ◀─── 204
```

`CurrentPassword` again — same reason as change-password. A captured
session can't weaken auth posture.

### MFA — admin reset

For "user lost their phone AND their recovery codes." Hard escape hatch:

```
POST /v1/identity/users/{id}/mfa/reset         [identity.users.write]
                              ───────────────▶ ResetMfaHandler
                                                · SetTwoFactorEnabledAsync(false)
                                                · ResetAuthenticatorKeyAsync
                                                · UpdateSecurityStampAsync
                                                  → outstanding tokens die
                                                ◀─── 204
```

After reset, the user logs in with password only (TwoFactor is off) and
can re-enroll via `/me/mfa/enroll`.

---

## Admin operations

All under `/v1/identity/users`, all gated on `identity.users.write` or
`identity.users.read`.

| Method | Path | Permission | Purpose |
|---|---|---|---|
| `POST` | `/v1/identity/users` | `identity.users.write` | Create user. Auto-fires confirmation email. |
| `GET` | `/v1/identity/users?page=&pageSize=&brandId=&role=&lockedOut=` | `identity.users.read` | Paged list with filters. |
| `GET` | `/v1/identity/users/{id}` | `identity.users.read` | Detail. |
| `PATCH` | `/v1/identity/users/{id}` | `identity.users.write` | Email / BrandId / LockoutEnabled. `null` = no change. |
| `DELETE` | `/v1/identity/users/{id}` | `identity.users.write` | **Soft-delete** (LockoutEnd = MaxValue + security-stamp rotation). |
| `POST` | `/v1/identity/users/{id}/roles` | `identity.roles.write` | Assign role. Idempotent. |
| `DELETE` | `/v1/identity/users/{id}/roles/{role}` | `identity.roles.write` | Remove role. Idempotent. |
| `POST` | `/v1/identity/users/{id}/unlock` | `identity.users.write` | Clear failed-attempt lockout. Refuses on soft-deleted users. |
| `POST` | `/v1/identity/users/{id}/mfa/reset` | `identity.users.write` | Admin MFA escape hatch. |
| `POST` | `/v1/identity/users/{id}/send-confirmation-email` | `identity.users.write` | Re-send confirmation email. Idempotent. |

**Why soft-delete via lockout?** Regulatory retention forbids hard
delete (the user's actions must stay auditable). `LockoutEnd =
DateTimeOffset.MaxValue` rides Identity's existing lockout
infrastructure: `SignInManager.PasswordSignInAsync` already refuses to
sign locked-out users in.

**Why role changes rotate the security stamp?** So tokens issued before
the change re-validate within the security-stamp interval and pick up
the new claim set. Combined with `EnableAuthorizationEntryValidation`,
the effect is ~immediate.

---

## Token lifecycle

| Token | Default TTL | Where it lives | Refresh on use? |
|---|---|---|---|
| Authorization code | 5 minutes | `openiddict_tokens` (single-use) | n/a |
| Access token | 1 hour | SPA memory | No — get a new one via refresh |
| Refresh token | 14 days | SPA memory + `openiddict_tokens` | **Yes** — rotation on every refresh |
| Auth cookie (IDP session) | 8 hours sliding | Browser, `HttpOnly` | Yes — sliding window |
| 2FA "remember machine" | 14 days | Browser + `user_tokens` | No |

### Entry validation — what it buys

`AddValidation()` in `IdentityModule.cs` is configured with:

```csharp
options.EnableAuthorizationEntryValidation();
options.EnableTokenEntryValidation();
```

Effect: every API call cross-checks the access token's row in
`openiddict_tokens` and its parent authorization row in
`openiddict_authorizations`. If either has status `revoked` or
`inactive`, the call 401s. **Revocation is therefore effectively
instant** — kill the row, every subsequent call dies.

**Cost**: 2 extra SQL Server lookups per authenticated request. Both are
PK reads on indexed columns; sub-ms each; SQL Server caches them in
shared buffers. For the back-office surface (tens to low-hundreds of
operators) this is fine. For the future `Pam.GameWallet` host
(sub-200ms p99, thousands of req/s) it would not be — that host should
run its own validation stack **without** these flags and accept short
revocation lag. See the comments in
[IdentityModule.cs](../src/Modules/Identity/Pam.Identity/IdentityModule.cs).

### How tokens die

| Trigger | Mechanism | Latency |
|---|---|---|
| Token TTL expires | OpenIddict validation rejects | At expiry |
| `/connect/revocation` called | Token row → revoked | Next API call (entry validation) |
| User soft-deleted | Security stamp rotates | Next security-stamp check (~30min default for cookie; immediate for tokens via entry validation) |
| Role / permission changed | Security stamp rotates | Same as above |
| Password reset | Security stamp rotates | Same |

---

## Cookie vs token

After a successful login the SPA has two parallel auth mechanisms:

| | Identity cookie | Bearer token |
|---|---|---|
| **Set by** | Server, on `POST /v1/identity/login` (`Set-Cookie`) | Returned in `POST /connect/token` response body |
| **Stored where** | Browser, `HttpOnly` (SPA JS can't read) | SPA memory |
| **Sent to** | `/connect/*` endpoints (automatic, browser attaches) | `/v1/*` endpoints (SPA attaches `Authorization: Bearer`) |
| **Purpose** | Identifies user TO THE IDP | Authorizes API calls |
| **Lifetime** | 8h sliding (or browser-close, depending on Remember Me) | 1h access / 14d refresh |
| **Survives browser restart?** | Only if `rememberMe=true` on login | Tokens are in memory — gone on tab close |

So the SPA's actual "I'm still logged in" state is held by the bearer
tokens. The cookie matters when the SPA needs to refresh tokens
silently via `/connect/authorize` (no UI prompt because cookie still
proves identity).

### "Remember Me" vs "Remember Machine"

Two different flags on two different endpoints:

| Flag | Endpoint | Effect |
|---|---|---|
| `rememberMe: true` | `/v1/identity/login` | Auth cookie becomes persistent (survives browser restart). Does **not** affect tokens or MFA. |
| `rememberMachine: true` | `/v1/identity/login/mfa` | Writes the 2FA-remembered cookie. **This browser** bypasses MFA on the next login. Independent of `rememberMe`. |

---

## Configuration

All keys live under `appsettings.{env}.json` (or env vars with `__`
nesting: `Identity__BackOfficeSpa__LoginUrl=…`).

### `Identity:BackOfficeSpa` (`BackOfficeSpaOptions`)

| Key | Default | Purpose |
|---|---|---|
| `ClientId` | `pam-bo` | OpenIddict client id seeded in `openiddict_applications`. |
| `DisplayName` | `PAM Back-Office SPA` | Cosmetic. |
| `LoginUrl` | `http://localhost:3000/login` | Where the cookie middleware redirects on `/connect/authorize` without a session. Adds `?returnUrl=…`. |
| `ResetPasswordUrl` | `http://localhost:3000/reset-password` | Where `/forgot-password` emails point. |
| `ConfirmEmailUrl` | `http://localhost:3000/confirm-email` | Where confirmation emails point. |
| `RedirectUris` | `[http://localhost:3000/auth/callback]` | OIDC redirect_uri allowlist. |
| `PostLogoutRedirectUris` | `[http://localhost:3000/]` | OIDC post_logout_redirect_uri allowlist. |

### `Notifications:Smtp` (`SmtpOptions`, in `Pam.Notifications`)

| Key | Default | Production setting |
|---|---|---|
| `Host` | `localhost` | Your SMTP relay (SES, SendGrid, …) |
| `Port` | `1025` (Mailpit) | `587` for SES STARTTLS |
| `UseStartTls` | `false` | `true` |
| `Username` | (none) | provider credential |
| `Password` | (none) | provider credential — **env var only** |
| `FromAddress` | `no-reply@pam.local` | A real domain you control |
| `FromName` | `PAM` | Brand-aware in the future |

### Bootstrap env vars

| Var | When |
|---|---|
| `PAM_BOOTSTRAP_OWNER_EMAIL` | First run only, while no Owner exists |
| `PAM_BOOTSTRAP_OWNER_PASSWORD` | First run only, while no Owner exists |

### Hardcoded policies (`IdentityModule.cs`)

| Setting | Value | How to change |
|---|---|---|
| Password min length | 12 | Edit `options.Password.RequiredLength` |
| Password complexity | digit + lower + upper + nonalphanumeric, 4 unique chars | Edit `options.Password.*` |
| Lockout after | 5 failed attempts | `options.Lockout.MaxFailedAccessAttempts` |
| Lockout duration | 15 min | `options.Lockout.DefaultLockoutTimeSpan` |
| Cookie sliding expiration | 8h | `options.ExpireTimeSpan` |
| `RequireConfirmedEmail` | **false** | Flip to `true` in prod (and after seeding all existing users to `EmailConfirmed=true`). |
| `RequireConfirmedAccount` | false | Stricter than above; rarely needed if `RequireConfirmedEmail=true`. |

### Per-environment knobs

`AddIdentityModule(builder.Configuration, builder.Environment)` checks
`environment.IsDevelopment()` and disables OpenIddict's HTTPS
requirement only in dev:

```csharp
if (environment.IsDevelopment())
{
    aspNetCore.DisableTransportSecurityRequirement();
}
```

In prod the TLS-terminating reverse proxy / ingress sets
`X-Forwarded-Proto: https`; `app.UseForwardedHeaders()` makes
`Request.IsHttps` report true; OpenIddict accepts.

### Dev signing/encryption certs

`AddDevelopmentEncryptionCertificate()` + `AddDevelopmentSigningCertificate()`
generate self-signed certs into the user's profile store on first run
and reuse them across restarts. **Production must replace these** with
real certs from the host (separate from the HTTPS cert; OpenIddict
recommends 2048-bit RSA, rotated annually). Pinned as TBD in ROADMAP.

---

## What's NOT implemented (intentional)

| Capability | Why deferred | Trigger to revisit |
|---|---|---|
| Self-service back-office registration | Wrong threat model — privilege boundary breach. Bootstrap Owner is the on-ramp. | Never. |
| Dynamic client registration (`/connect/register`) | We own every client; `pam-bo` today, future module-to-module clients seeded. | Never — third party app needing to register. |
| WebAuthn / passkeys | TOTP covers MFA sufficiently for the back-office threat model. | A regulator or customer asks. Code path: OpenIddict + Fido2 package. |
| External OIDC federation (Azure AD, Google Workspace) | No customer asking; back-office is small. | Customer or compliance team requires single-sign-on. `user_logins` table is already there. |
| Active sessions list / "logout everywhere" UI | Not a regulatory requirement today. Token revocation works via admin endpoints + `/connect/revocation`. | Operator UX feature request. |
| Concurrent session limits | Same reason. | Same trigger. |
| Player auth (`POST /v1/auth/register` etc.) | Players module deferred to Phase 4. Will use the same OpenIddict server under a different audience (`pam_player_api`). | The Players module itself. |

---

## Implementation map

### Where each piece lives

```
src/Modules/Identity/Pam.Identity/
  IdentityModule.cs                ← AddIdentityModule wire-up, OpenIddict setup
  BackOfficeSpaOptions.cs          ← strongly-typed SPA config

  Data/
    IdentityDbContext.cs           ← all identity.* tables
    IdentityDbContextDesignTimeFactory.cs
    Configurations/                ← EF Core fluent config
    Migrations/                    ← EF Core migrations

  Users/
    Models/BackOfficeUser.cs       ← IdentityUser<Guid> + BrandId + audit
    Exceptions/UserErrors.cs       ← stable error codes
    Features/
      CreateUser/                  ← POST /v1/identity/users
      ListUsers/                   ← GET /v1/identity/users
      GetUser/                     ← GET /v1/identity/users/{id}
      UpdateUser/                  ← PATCH /v1/identity/users/{id}
      DeleteUser/                  ← DELETE /v1/identity/users/{id} (soft)
      AssignRole/                  ← POST /v1/identity/users/{id}/roles
      RemoveRole/                  ← DELETE /v1/identity/users/{id}/roles/{role}
      UnlockUser/                  ← POST /v1/identity/users/{id}/unlock
      ResetMfa/                    ← POST /v1/identity/users/{id}/mfa/reset
      SendConfirmationEmail/       ← POST /v1/identity/users/{id}/send-confirmation-email

  Authentication/
    HttpUserContext.cs             ← reads sub claim → Actor for audit columns
    Login/                         ← POST /v1/identity/login
    LoginMfa/                      ← POST /v1/identity/login/mfa
    LoginRecoveryCode/             ← POST /v1/identity/login/recovery-code
    ChangePassword/                ← POST /v1/identity/me/change-password
    ForgotPassword/                ← POST /v1/identity/forgot-password
    ResetPassword/                 ← POST /v1/identity/reset-password
    ConfirmEmail/                  ← POST /v1/identity/confirm-email
    Mfa/                           ← /me/mfa/enroll, /verify, /disable, /recovery-codes

  OpenIddict/
    ClaimDestinations.cs           ← maps claims → access-token vs id-token
    Controllers/
      AuthorizationController.cs   ← /connect/authorize + /connect/token + /connect/logout
      UserinfoController.cs        ← /connect/userinfo

  Permissions/
    Models/Permission.cs           ← entity
    Models/RolePermission.cs       ← entity
    PermissionResolver.cs          ← role[] → permission code[]

  Seeding/
    IdentitySeeder.cs              ← permissions, roles, role-permissions,
                                    scopes, applications, bootstrap Owner

src/Modules/Identity/Pam.Identity.Contracts/
  Permissions/PermissionCodes.cs   ← well-known permission strings
  Permissions/PamClaimTypes.cs     ← custom claim type names
  Roles/RoleNames.cs               ← well-known role names
  Users/Dtos/BackOfficeUserDto.cs  ← public response shape

src/Modules/Notifications/Pam.Notifications.Contracts/
  Email/IEmailSender.cs            ← cross-module email surface
  Email/EmailMessage.cs            ← DTO

src/Modules/Notifications/Pam.Notifications/
  Email/SmtpEmailSender.cs         ← MailKit impl (internal)
  Email/SmtpOptions.cs             ← Notifications:Smtp config
  NotificationsModule.cs           ← AddNotificationsModule
  Consumers/                       ← future integration-event subscribers
```

### Patterns to copy when adding a new auth-adjacent endpoint

1. New feature under `Pam.Identity/<area>/Features/<UseCase>/`.
2. Four files: `<UseCase>Command.cs`, `<UseCase>Validator.cs`,
   `<UseCase>Handler.cs`, `<UseCase>Endpoint.cs`.
3. Endpoint either `.RequireAuthorization($"Permissions.{code}")` for
   admin actions, or `.RequireAuthorization()` for "any authenticated
   back-office user", or `.AllowAnonymous()` for public flows.
4. Anonymous flows (`/login`, `/forgot-password`, `/reset-password`,
   `/confirm-email`, `/login/mfa`, `/login/recovery-code`) MUST
   `.RequireRateLimiting("auth-sensitive")`.
5. Authenticated self-service that exposes secrets or weakens auth
   posture (change-password, mfa/disable, mfa/verify, mfa/recovery-codes)
   SHOULD `.RequireRateLimiting("auth-sensitive")` too.
6. Stable error codes go in `Users/Exceptions/UserErrors.cs` (or a
   sibling file in your area).
7. For email sending: inject `IEmailSender` from
   `Pam.Notifications.Contracts`. For sensitive content (tokens),
   call it directly. For "describe what happened" cross-module flows,
   publish an integration event and let a `Pam.Notifications/Consumers/`
   subscriber render the email.

---

## Smoke testing the full flow

A bash script that exercises register → login → token → API call lives
in the team folklore but isn't checked in yet. The bones:

```bash
# 1. Make sure the bootstrap Owner exists (env vars set in launchSettings.json
#    or shell before `make dev-api`).

# 2. Cookie-based login
curl -i -c jar -X POST http://localhost:5000/v1/identity/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"owner@test.local","password":"OwnerPassword123!"}'
# → 204 + Set-Cookie

# 3. userinfo with the cookie
curl -i -b jar http://localhost:5000/connect/userinfo
# → 200 + { sub, email, name, role: [Owner] }

# 4. Bearer-token via OIDC code flow needs PKCE — see the "What you can
#    do with the cookie" section in this doc's git history, or use
#    Scalar with OAuth configured.
```

See [LOCAL_DEV.md](LOCAL_DEV.md) for the rest of the dev loop.
