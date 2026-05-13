# Caching

PAM has a single read-through cache: a MediatR pipeline behavior that
caches `IQuery<T>` responses in Redis when the query type opts in with
`[Cache]`, and invalidates them when commands opt in with
`[InvalidateCache]`. There is no module-level caching, no DB-query cache,
and no HTTP response cache. The CQRS seam is the only one we cache at.

Live since branch `feat/redis-caching-behavior` (merged 2026-05-11,
PR #20).

## The shape

Four pieces, all under `Pam.Shared`:

| File | Role |
|---|---|
| `Pam.Shared.Contracts/Caching/ICacheService.cs` | The abstraction. Get / Set / Remove / RemoveByPattern / Exists, all keyed by string. |
| `Pam.Shared.Contracts/Caching/CacheAttribute.cs` | `[Cache(durationMinutes, keyPattern)]` — opts a query into caching. |
| `Pam.Shared.Contracts/Caching/InvalidateCacheAttribute.cs` | `[InvalidateCache(params string[] patterns)]` — opts a command into pattern-based purge. |
| `Pam.Shared.Contracts/Caching/CacheKeyGenerator.cs` | Static. `{Prop}` interpolation against the request, SHA-256-hash fallback when no pattern is given. |
| `Pam.Shared/Caching/RedisCacheService.cs` | The only `ICacheService` implementation. Singleton on top of the existing `IConnectionMultiplexer`. |
| `Pam.Shared/Behaviors/CachingBehavior.cs` | The MediatR `IPipelineBehavior` that ties them together. |

The behavior is registered inside `AddPamMediatR` (the existing extension
in `SharedServiceCollectionExtensions`). The Redis service is registered
by a new `AddPamCaching()` extension called from `Program.cs`
immediately after the connection multiplexer.

## Pipeline order

```
LoggingBehavior → ValidationBehavior → CachingBehavior → handler
```

`Logging` wraps the whole pipeline so cache hits and misses still show
up in request timings. `Validation` runs before `Caching` so an invalid
request never poisons the cache — the cache only sees well-formed,
validated requests. The handler runs last, only on a cache miss.

## Usage

A read-through query:

```csharp
[Cache(durationMinutes: 5, keyPattern: "identity:user:{UserId}")]
public sealed record GetUserQuery(Guid UserId) : IQuery<BackOfficeUserDto>;
```

A list query with multiple filters:

```csharp
[Cache(
    durationMinutes: 2,
    keyPattern: "identity:users:list:p={Page}:s={PageSize}:b={BrandId}:r={Role}:l={LockedOut}"
)]
public sealed record ListUsersQuery(...) : IQuery<ListUsersResult>;
```

A mutating command that purges related keys after it succeeds:

```csharp
[InvalidateCache("identity:user:*", "identity:users:list:*")]
public sealed record UpdateUserCommand(...) : ICommand;
```

`{Prop}` placeholders in `keyPattern` are interpolated against the
request type's properties (case-insensitive). When `keyPattern` is null
or empty, the key is `{TypeName}:{first 16 hex chars of SHA-256 of the
JSON-serialized request}`.

## Key namespacing

Every key written by `RedisCacheService` is prefixed with `pam:cache:`.
That namespace exists so the cache cannot collide with the rate-limiter
keyspace (`pam-api-rl:*`) or any future Redis use (locks, sessions).
Callers pass logical keys ("identity:user:abc-123"); the prefix is
applied internally on read, write, and pattern delete.

Use `redis-cli KEYS 'pam:cache:*'` (or `SCAN`) to inspect.

## Invalidation patterns are literal

`[InvalidateCache(...)]` takes literal strings. The behavior calls
`ICacheService.RemoveByPatternAsync(pattern)` for each entry; patterns
are passed to Redis verbatim (with the `pam:cache:` prefix applied).
`*` wildcards work; `{Prop}` placeholders **do not** — the attribute
doesn't interpolate against the command instance.

Practical consequence: a command can't say "invalidate just this user".
It has to either:

- Invalidate the whole namespace (`identity:user:*`) — what every Users
  command does today. Bounded and safe.
- Or be replaced with a finer-grained scheme later (see *Future work*).

## Failure mode

`RedisCacheService.Get/Set/Remove` wrap their Redis calls in `try/catch`
and log a warning on failure. A read returns `default`, which causes
the behavior to fall through to the handler and refresh the cache on
the next set. The query path never fails because of a Redis blip.

Pattern delete iterates `IConnectionMultiplexer.GetEndPoints()`, skips
replicas and disconnected endpoints, and uses `IServer.KeysAsync` (which
uses `SCAN` under the hood, not `KEYS`). One endpoint throwing doesn't
abort the others — every reachable primary gets a chance to purge.

## What's cached today

Module #1 to adopt the pattern is `Pam.Identity` / Users:

| Query | TTL | Pattern |
|---|---|---|
| `GetUserQuery` | 5 min | `identity:user:{UserId}` |
| `ListUsersQuery` | 2 min | `identity:users:list:p={Page}:s={PageSize}:b={BrandId}:r={Role}:l={LockedOut}` |

| Command | Invalidates |
|---|---|
| `CreateUserCommand` | `identity:user:*`, `identity:users:list:*` |
| `UpdateUserCommand` | same |
| `DeleteUserCommand` | same |
| `AssignRoleCommand` | same |
| `RemoveRoleCommand` | same |
| `UnlockUserCommand` | same |
| `ResetMfaCommand` | same |
| `SendConfirmationEmailCommand` | *not invalidated* — read-only against the user record (doesn't touch `EmailConfirmed`). |

`Pam.Operators`, `Pam.Players`, `Pam.Wallet`: nothing opted in yet.
Adopt when a query becomes a measurable hot path.

## Gotchas

- **Payload-shape drift.** The cache stores JSON; if a DTO field is
  renamed or removed, entries written by the old code deserialize with
  the old shape until TTL expires. Keep TTLs short on records that
  change often, and bump the `keyPattern` (e.g. add a `:v2` suffix)
  when the response shape changes meaningfully.
- **No cache stampede protection.** Many concurrent misses on the same
  key will each run the handler. Acceptable at current scale; revisit
  if a hot key shows up in metrics.
- **Pattern-delete cost.** `SCAN` is O(keys-in-DB). Cheap today but
  keep invalidation patterns specific — `identity:user:*` is fine,
  bare `*` would be wrong.
- **No `RemoveAsync` interpolation either.** `RemoveByPatternAsync`
  takes a literal pattern; it does not interpolate `{Prop}` against
  anything. Same constraint as `[InvalidateCache]`.
- **Redis is required, not optional.** `Program.cs` throws at boot if
  `ConnectionStrings:Redis` is missing; `RedisCacheService` has no
  in-memory fallback. Multi-replica safety relies on Redis being the
  shared substrate.
- **MediatR is pinned at 12.4.1** (last MIT-licensed release — see
  DECISIONS.md #5). Don't bump it.

## Future work

The two natural extensions, both deferred until a real need appears:

1. **Pattern interpolation in `[InvalidateCache]`.** Run each pattern
   through `CacheKeyGenerator.GenerateKey(request, pattern)` inside
   `CachingBehavior.InvalidateCacheIfNeededAsync`. That gives
   `[InvalidateCache("identity:user:{UserId}")]` per-user invalidation
   for free. Small change, plus a couple of behavior tests. Land it
   when broad-namespace purges become a measurable cost.
2. **Cache stampede / dog-pile protection.** A `SemaphoreSlim` keyed by
   cache key inside the behavior, or a Redis-side `SET NX` lock with a
   short TTL while the handler runs. Land it when a hot read shows up
   in the OTel pipeline.

## Tests

- **Unit**: `tests/Pam.Shared.UnitTests/Caching/` — `CacheKeyGenerator`
  (pattern interpolation, hash fallback) and `CachingBehavior`
  (cache hit / miss / invalidation, `NSubstitute` for `ICacheService`).
  12 tests.
- **Integration**: `tests/Pam.IntegrationTests/CachingTests.cs` —
  Get/Set/Remove round-trip, `ExistsAsync`, `RemoveByPatternAsync`
  scoping, and the `pam:cache:` prefix, against the existing
  `PamContainersFixture` Redis container.
