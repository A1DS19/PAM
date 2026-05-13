using System.Globalization;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pam.Shared.Behaviors;
using Pam.Shared.Contracts.Audit;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Contracts.Identity;
using Pam.Shared.Security;
using Pam.Shared.Time;
using Xunit;

namespace Pam.Shared.UnitTests.Behaviors;

public sealed class AuditBehaviorTests
{
    public sealed record DoThingCommand(Guid Id, [property: Sensitive] string Password)
        : ICommand<int>;

    public sealed record FireAndForgetCommand(string Note) : ICommand;

    public sealed record GetThingQuery(Guid Id) : IQuery<int>;

    public sealed record HighVolumeCommand(string Vendor) : ICommand<int>, IUnauditedCommand;


    [Fact]
    public async Task Command_success_writes_success_row_with_redacted_payload()
    {
        var (audit, sut) = CreateSut<DoThingCommand, int>(actor: new Actor(ActorType.Operator, "op-1"));
        AuditEntry? captured = null;
        await audit.RecordAsync(
            Arg.Do<AuditEntry>(e => captured = e),
            Arg.Any<CancellationToken>()
        );

        var response = await sut.Handle(
            new DoThingCommand(Guid.NewGuid(), "supersecret"),
            () => Task.FromResult(7),
            CancellationToken.None
        );

        response.Should().Be(7);
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(AuditStatus.Success);
        captured.ActorType.Should().Be(ActorType.Operator);
        captured.ActorId.Should().Be("op-1");
        captured.RequestType.Should().Be(typeof(DoThingCommand).FullName);
        captured.PayloadJson.Should().Contain("\"password\":\"***\"");
        captured.PayloadJson.Should().NotContain("supersecret");
        captured.ErrorType.Should().BeNull();
        captured.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Command_failure_writes_failure_row_then_rethrows()
    {
        var (audit, sut) = CreateSut<FireAndForgetCommand, Unit>();
        AuditEntry? captured = null;
        await audit.RecordAsync(
            Arg.Do<AuditEntry>(e => captured = e),
            Arg.Any<CancellationToken>()
        );
        var boom = new InvalidOperationException("nope");

        var act = async () =>
            await sut.Handle(
                new FireAndForgetCommand("hi"),
                () => throw boom,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("nope");
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(AuditStatus.Failure);
        captured.ErrorType.Should().Be(typeof(InvalidOperationException).FullName);
        captured.ErrorMessage.Should().Be("nope");
    }

    [Fact]
    public async Task Unaudited_command_bypasses_the_audit_service()
    {
        var (audit, sut) = CreateSut<HighVolumeCommand, int>();

        var response = await sut.Handle(
            new HighVolumeCommand("21g"),
            () => Task.FromResult(42),
            CancellationToken.None
        );

        response.Should().Be(42);
        await audit.DidNotReceive().RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unaudited_command_failure_still_bypasses_audit()
    {
        var (audit, sut) = CreateSut<HighVolumeCommand, int>();
        var boom = new InvalidOperationException("nope");

        var act = async () =>
            await sut.Handle(
                new HighVolumeCommand("21g"),
                () => throw boom,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        await audit.DidNotReceive().RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Query_bypasses_the_audit_service()
    {
        var (audit, sut) = CreateSut<GetThingQuery, int>();

        var response = await sut.Handle(
            new GetThingQuery(Guid.NewGuid()),
            () => Task.FromResult(99),
            CancellationToken.None
        );

        response.Should().Be(99);
        await audit.DidNotReceive().RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Audit_service_throwing_does_not_propagate()
    {
        var (audit, sut) = CreateSut<FireAndForgetCommand, Unit>();
        audit
            .RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("audit DB down"));

        var response = await sut.Handle(
            new FireAndForgetCommand("hi"),
            () => Task.FromResult(Unit.Value),
            CancellationToken.None
        );

        response.Should().Be(Unit.Value);
    }

    private static (IAuditService audit, AuditBehavior<TRequest, TResponse> sut) CreateSut<
        TRequest,
        TResponse
    >(Actor? actor = null)
        where TRequest : notnull
    {
        var audit = Substitute.For<IAuditService>();
        var userContext = Substitute.For<IUserContext>();
        userContext.Current.Returns(actor ?? new Actor(ActorType.System, "system"));
        var clock = Substitute.For<IClock>();
        clock
            .UtcNow.Returns(DateTimeOffset.Parse("2026-05-11T08:00:00Z", CultureInfo.InvariantCulture));
        var sut = new AuditBehavior<TRequest, TResponse>(
            audit,
            userContext,
            clock,
            NullLogger<AuditBehavior<TRequest, TResponse>>.Instance
        );
        return (audit, sut);
    }
}
