using System.Diagnostics;

namespace Pam.Shared.Observability;

// Custom ActivitySources for in-process spans not covered by the
// stock instrumentation libraries (ASP.NET / EF / HttpClient /
// MassTransit). Picked up automatically by the AddSource("Pam.*")
// wildcard in Program.cs — no extra registration needed.
public static class PamActivitySources
{
    public const string MediatRName = "Pam.Mediatr";

    public static readonly ActivitySource MediatR = new(MediatRName, "1.0.0");
}
