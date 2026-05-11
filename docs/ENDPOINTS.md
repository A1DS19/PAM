# Endpoint conventions

Every HTTP endpoint in PAM follows the same shape so Scalar — the
OpenAPI UI — renders a rich card with summary, description, request /
response schemas, every possible status code, and the auth requirement.
The reference implementation is the 21G Ingest endpoint at
`src/Modules/Ingest/Pam.Ingest/Vendors/TwentyOneG/TwentyOneGEndpoint.cs`.

This document is the human-readable spec for that convention. The
machine-enforced version is the corresponding section of
[`CLAUDE.md`](../CLAUDE.md) at the project root, which Claude Code reads
on every session.

## The full annotation chain

```csharp
app.MapPost("/v1/<module>/<resource>", handler)
    .WithTags("<Module>")                            // one tag = module name
    .WithName("<UniqueOperationId>")
    .WithSummary("<verb-led one-liner, no period>")
    .WithDescription(
        """
        <one paragraph plain-English overview>

        **Auth:** <permission code> OR "anonymous, rate-limited by ...".

        **Idempotency:** <one line if applicable>

        **Side effects:** <if state changes beyond the obvious>

        **Status codes:**
        - `200 OK` — ...
        - `400 Bad Request` — ...
        - ...
        """
    )
    .Accepts<TRequest>("application/json")          // POST/PUT/PATCH
    .Produces<TResponse>(StatusCodes.Status200OK)   // or Status201Created
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status400BadRequest)        // when applicable
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden)         // when auth-protected
    .ProducesProblem(StatusCodes.Status404NotFound)          // when applicable
    .ProducesProblem(StatusCodes.Status409Conflict)          // when applicable
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity) // when applicable
    .ProducesProblem(StatusCodes.Status429TooManyRequests)   // when rate-limited
    .RequireAuthorization($"Permissions.{PermissionCodes.X}");
    // OR for public endpoints:
    // .AllowAnonymous().RequireRateLimiting("auth-sensitive");
```

Order matters for readability, not correctness:

1. **Tags + Name** — identifies the endpoint in Scalar's navigation.
2. **Summary + Description** — what Scalar shows.
3. **Accepts** — request body schema (POST/PUT/PATCH only).
4. **Produces / ProducesProblem** — response shapes + status codes.
5. **Auth / rate-limit** — the runtime contract.

## Summary

One short sentence. Under ~80 chars. Sentence case, no trailing period.
Verb-led.

| ✓ Good | ✗ Bad |
|---|---|
| `Create a back-office user` | `User creation endpoint.` |
| `List players with filtering` | `GET /players` |
| `Reset a player's MFA enrollment` | `MFA admin reset` |
| `Confirm a back-office user's email address` | `Email confirmation` |

The summary appears next to every endpoint in Scalar's left-hand
navigation. It's what someone scanning the API surface sees first.

## Description

Triple-quoted raw string (`"""..."""`). Markdown is rendered by Scalar.
Use **bold** labels for the structured sections. Skip a section if it
doesn't apply (e.g. no "Idempotency" line for a one-shot mutation that
isn't idempotent).

### Template

```text
"""
<One paragraph in plain English. What the endpoint does, not how. Don't
restate the schema — Scalar shows that already. Mention business semantics
the schema can't convey: "Idempotent via the `Reference` field" or
"Auto-fires a confirmation email" or "Soft-deletes — never hard-deletes".>

**Auth:** <permission> OR "anonymous, rate-limited by ...".

**Idempotency:** <if the endpoint has retry semantics — explicit one
line about behavior on duplicate request>

**Side effects:** <state changes beyond the obvious response —
emails sent, security stamps rotated, tokens revoked, integration
events published>

**Status codes:**
- `200 OK` — <when this returns and what's in the body>
- `204 No Content` — <when this returns>
- `400 Bad Request` — <typical cause>
- `401 Unauthorized` / `403 Forbidden` — auth failed / forbidden.
- `404 Not Found` — <what's missing>
- `409 Conflict` — <typical conflict>
- `422 Unprocessable Entity` — <typical 422 cause>
- `429 Too Many Requests` — rate-limited.
"""
```

### Deriving content from the existing comment block

Most endpoint files already have a leading `//` comment that documents
the route's behavior. Promote that content into the description.
Comments stay in the source for code-review context; descriptions surface
in Scalar.

## Request and response DTOs MUST be public

OpenAPI / Scalar can only render schemas for types it can reach. That
means:

- **Request bodies**: top-level public records, either in the endpoint
  file or a sibling `*Contracts.cs` file. **Never private nested
  types** inside handlers, adapters, or controllers.
- **Response bodies**: same. **Never return anonymous types** from
  endpoints — the `.Produces<T>()` declaration would lie.

The 21G vendor endpoint pattern is the reference: `TwentyOneGContracts.cs`
holds the public `TwentyOneGRequest` + `TwentyOneGResponse` records;
the adapter consumes the request type and returns the response type;
the endpoint annotates `.Accepts<TwentyOneGRequest>` + `.Produces<TwentyOneGResponse>`.

## Which status codes to declare

Declare every status code that this specific endpoint can actually
return. Don't sprinkle every `ProducesProblem` overload on every
endpoint — Scalar will show the false ones as if they're real outcomes.

| Status | Declare when |
|---|---|
| `200 OK` | Read endpoints, or write endpoints that return a body |
| `201 Created` | Write endpoints that create a resource; emit `Location` header |
| `204 No Content` | Successful writes that intentionally have no body |
| `400 Bad Request` | The framework or handler returns 400 for malformed input |
| `401 Unauthorized` | Any endpoint that isn't `.AllowAnonymous()` |
| `403 Forbidden` | Any endpoint with `.RequireAuthorization("Permissions.X")` |
| `404 Not Found` | Read endpoints + writes that look up a record first |
| `409 Conflict` | Uniqueness collisions (slug taken, email taken) |
| `422 Unprocessable Entity` | Domain rule violations (`BusinessRuleViolationException`) |
| `423 Locked` | Specifically: lockout-style outcomes (Login during lockout) |
| `429 Too Many Requests` | Anything with `.RequireRateLimiting(...)` |

`.ProducesValidationProblem()` is the FluentValidation surface — call it
on every endpoint that takes a request body (it produces 400 with
`errors: {...}` per-field detail).

## Tags

One tag per endpoint — the module name. `"Identity"`, `"Operators"`,
`"Ingest"`. Scalar groups by tag.

```csharp
.WithTags("Ingest")
```

We deliberately do NOT use multi-tagging (e.g. `"Ingest"` +
`"Vendor:21g"`). Multi-tagging makes Scalar render the same endpoint
twice in the left nav — once under each tag — which looks like
duplication. If we later want hierarchical nav (split `"Identity"` into
`"Authentication"` and `"Users"` sub-groups; group vendor endpoints
under `"Ingest"` with `"Vendor:21g"` leaves), the right tool is the
`x-tagGroups` OpenAPI extension via an `AddOpenApi` document
transformer — single-tag endpoints + a transformer that defines the
hierarchy. Not wired today.

## Operation IDs

`.WithName(...)` is the OpenAPI operation id. Conventions:

- Camel- or Pascal-case identifier.
- Globally unique across the API.
- Same as the C# command/query type name when there's one
  (e.g. `CreateUser`, `GetBrand`, `ListUsers`).
- For action verbs without a separate command type, use a verb-noun
  shape (`UnlockUser`, `ResetMfa`).

Client SDK generators (e.g. NSwag) use these as the method name —
treat them as a public contract.

## Rate limiting

Two policies are wired (see `Program.cs`):

| Policy | Window | Limit | Use for |
|---|---|---|---|
| `auth-sensitive` | 1 minute | 5 per partition | Login, password reset, MFA verify, anything that creates auth state |
| `api-default` | 30 seconds (sliding) | 100 per partition | Every other write endpoint that doesn't have a more specific limit |

Read endpoints don't need rate limiting by default — they're cached
(per `CACHING.md`) and benign under retry.

## Auth

Use the granular policy form: `RequireAuthorization($"Permissions.{PermissionCodes.X.Y}")`.

- **One named policy per `PermissionCode`**, declared in `Program.cs`.
- `platform.admin` is an implicit grant on every policy (Owner role
  carries it). Don't manually OR it into individual endpoints.
- Public endpoints (login, register, forgot-password, email-confirm,
  vendor callbacks) use `.AllowAnonymous()` + a rate-limit policy.

## Examples

See these for the canonical shape:

- `Pam.Ingest/Vendors/TwentyOneG/TwentyOneGEndpoint.cs` — public
  vendor callback; anonymous + rate-limited; full description block.
- `Pam.Identity/Authentication/Login/LoginEndpoint.cs` — anonymous,
  multiple 2xx outcomes (`200` for MFA challenge, `204` for success).
- `Pam.Identity/Users/Features/CreateUser/CreateUserEndpoint.cs` —
  admin write with full status code coverage including `409` and `422`.
- `Pam.Operators/Brands/Features/GetBrand/GetBrandEndpoint.cs` —
  authenticated read; minimal status code set (no `409`, no `422`,
  no rate-limit).

## Anti-patterns

| Don't | Why |
|---|---|
| Return anonymous objects from endpoints | `.Produces<T>()` lies; Scalar shows wrong schema |
| Skip `.WithSummary` / `.WithDescription` | Endpoint shows in Scalar as a bare path |
| Declare a `ProducesProblem` you can't actually produce | False signal to API consumers |
| Use `.RequireAuthorization()` without a policy | Falls back to "any authenticated user" — too broad for write endpoints |
| Forget rate-limiting on a write endpoint | One missing limit, one DoS vector |
| Mix `.Produces<T>` and `.Produces(StatusCodes.X)` for the same code | Pick one — the typed overload if there's a body |
| Put the description after `Produces` | Hurts readability of the chain |

## Adding a new endpoint

Mechanical checklist:

1. Four files under `Pam.<Module>/<Aggregate>/Features/<UseCase>/`:
   `<UseCase>Command.cs`, `<UseCase>Validator.cs`,
   `<UseCase>Handler.cs`, `<UseCase>Endpoint.cs`.
2. If the request or response has a non-trivial shape, define them as
   public records (in the command file is fine for simple cases; a
   sibling `*Contracts.cs` file for vendor adapters).
3. Wire the full annotation chain per the template above.
4. Validator test in `tests/Pam.<Module>.UnitTests/`.
5. Run `dotnet build` to verify; run `make test` to confirm tests pass.
6. Check Scalar — `http://localhost:5000/scalar/v1` after `make dev-api`.

If anything in this doc contradicts a more specific topical reference
(`AUTH.md`, `INGEST.md`, `CACHING.md`), the topical doc wins for its
domain — but the annotation pattern itself is universal.
