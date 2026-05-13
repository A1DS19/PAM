# Endpoint conventions

Every HTTP endpoint follows the same shape so Scalar renders a rich
card with summary, description, schemas, status codes, and auth.

Reference implementation:
`api/src/Modules/Ingest/Pam.Ingest/Vendors/TwentyOneG/TwentyOneGEndpoint.cs`.
Machine-enforced version: `CLAUDE.md` at the project root.

## The annotation chain

```csharp
app.MapPost("/v1/<module>/<resource>", handler)
    .WithTags("<Module>")                            // one tag = module
    .WithName("<UniqueOperationId>")
    .WithSummary("<verb-led one-liner, no period>")
    .WithDescription(
        """
        <one paragraph in plain English>

        **Auth:** <permission> OR "anonymous, rate-limited by ...".
        **Idempotency:** <one line if applicable>
        **Side effects:** <state changes beyond the obvious>
        **Status codes:**
        - `200 OK` — ...
        - `400 Bad Request` — ...
        """
    )
    .Accepts<TRequest>("application/json")           // POST/PUT/PATCH
    .Produces<TResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status400BadRequest)        // when applicable
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden)         // when auth-protected
    .ProducesProblem(StatusCodes.Status404NotFound)          // when applicable
    .ProducesProblem(StatusCodes.Status409Conflict)          // when applicable
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity) // when applicable
    .ProducesProblem(StatusCodes.Status429TooManyRequests)   // when rate-limited
    .RequireAuthorization($"Permissions.{PermissionCodes.X}");
    // OR for public:  .AllowAnonymous().RequireRateLimiting("auth-sensitive");
```

## Summary

One sentence, ~80 chars, sentence case, no trailing period, verb-led.

| ✓ Good | ✗ Bad |
|---|---|
| `Create a back-office user` | `User creation endpoint.` |
| `List players with filtering` | `GET /players` |
| `Reset a player's MFA enrollment` | `MFA admin reset` |
| `Confirm a back-office user's email address` | `Email confirmation` |

Appears next to every endpoint in Scalar's left-hand nav.

## Description

Triple-quoted raw string. Markdown is rendered. **Bold** labels for the
structured sections; skip any section that doesn't apply. Most endpoint
files already have a leading `//` comment block — promote that content
into the description.

## Status codes

Declare every code this endpoint can actually return. Sprinkling
`ProducesProblem` on every endpoint pollutes Scalar with false outcomes.

| Status | When |
|---|---|
| `200 OK` | Read, or write with a body |
| `201 Created` | Resource creation; emit `Location` header |
| `204 No Content` | Successful write with no body |
| `400 Bad Request` | Malformed input that bypasses the validator |
| `401 Unauthorized` | Anything not `.AllowAnonymous()` |
| `403 Forbidden` | Anything with `.RequireAuthorization("Permissions.X")` |
| `404 Not Found` | Read, or write that looks up first |
| `409 Conflict` | Uniqueness collisions (slug, email taken) |
| `422 Unprocessable Entity` | Domain rule violations (`BusinessRuleViolationException`) |
| `423 Locked` | Lockout outcomes (login during lockout) |
| `429 Too Many Requests` | Anything with `.RequireRateLimiting(...)` |

`.ProducesValidationProblem()` is the FluentValidation surface — call
it on every endpoint with a request body.

## DTOs must be public

OpenAPI / Scalar only renders types it can reach.

- Request bodies: top-level public records, in the endpoint file or a
  sibling `*Contracts.cs`. Never private nested types.
- Response bodies: same. Never anonymous types — `.Produces<T>()` would
  lie.

`TwentyOneGContracts.cs` is the reference.

## Tags

One tag per endpoint — the module name (`Identity`, `Operators`,
`Ingest`). Scalar groups by tag.

Multi-tagging is deliberately not used — it duplicates the endpoint in
the nav. For hierarchical nav use the `x-tagGroups` OpenAPI extension
via a document transformer.

## Operation IDs

`.WithName(...)` is the OpenAPI operation id and the method name in
generated client SDKs (NSwag). Treat as public contract.

- Camel- or Pascal-case, globally unique.
- Match the C# command/query type name when there is one
  (`CreateUser`, `GetBrand`, `ListUsers`).
- Verb-noun shape for action endpoints (`UnlockUser`, `ResetMfa`).

## Rate limiting

| Policy | Window | Limit | Use for |
|---|---|---|---|
| `auth-sensitive` | 1 min | 5 / partition | Login, password reset, MFA verify, anything that creates auth state |
| `api-default` | 30 s sliding | 100 / partition | Every other write that doesn't have a more specific limit |

Read endpoints don't need rate limiting by default — they're cached and
benign under retry.

## Auth

- One named policy per `PermissionCode`, declared in `Program.cs`.
- `platform.admin` is an implicit grant on every policy (Owner role).
  Don't manually OR it into individual endpoints.
- Public endpoints (login, register, forgot-password, email-confirm,
  vendor callbacks) use `.AllowAnonymous() + .RequireRateLimiting(...)`.

## Examples

| File | What it shows |
|---|---|
| `Pam.Ingest/Vendors/TwentyOneG/TwentyOneGEndpoint.cs` | Public vendor callback; anonymous + rate-limited; full description |
| `Pam.Identity/Authentication/Login/LoginEndpoint.cs` | Anonymous, multiple 2xx (`200` MFA challenge, `204` success) |
| `Pam.Identity/Users/Features/CreateUser/CreateUserEndpoint.cs` | Admin write with `409` + `422` coverage |
| `Pam.Operators/Brands/Features/GetBrand/GetBrandEndpoint.cs` | Authenticated read; minimal status set |

## Anti-patterns

| Don't | Why |
|---|---|
| Return anonymous objects | `.Produces<T>()` lies; Scalar wrong schema |
| Skip `.WithSummary` / `.WithDescription` | Bare path in Scalar |
| Declare a `ProducesProblem` you can't produce | False signal to consumers |
| Use `.RequireAuthorization()` without a policy | Falls back to "any authenticated user" — too broad |
| Skip rate-limiting on a write endpoint | DoS vector |
| Mix `.Produces<T>` and `.Produces(StatusCodes.X)` for the same code | Pick the typed overload if there's a body |
| Put `.WithDescription` after `Produces` | Hurts chain readability |

## Adding an endpoint

1. Four files under `Pam.<Module>/<Aggregate>/Features/<UseCase>/`:
   `<UseCase>{Command,Validator,Handler,Endpoint}.cs`.
2. Public request/response records (in the command file, or sibling
   `*Contracts.cs` for vendor adapters).
3. Full annotation chain per the template.
4. Validator test in `tests/Pam.<Module>.UnitTests/`.
5. `dotnet build`; `make -C api test`.
6. Verify Scalar at `http://localhost:5000/scalar/v1`.

Topical refs (`AUTH.md`, `INGEST.md`, `CACHING.md`) win for their
domain. The annotation pattern is universal.
