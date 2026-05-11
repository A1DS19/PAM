using System.Diagnostics;
using FluentAssertions;
using NSubstitute;
using Pam.Shared.Behaviors;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Contracts.Identity;
using Pam.Shared.Http;
using Pam.Shared.Observability;
using Pam.Shared.Security;
using Xunit;

namespace Pam.Shared.UnitTests.Behaviors;

public sealed class OpenTelemetryBehaviorTests
{
    public sealed record CreateBrandCommand(string Name) : ICommand<Guid>;

    public sealed record FireCommand(string Note) : ICommand;

    public sealed record GetBrandQuery(Guid Id) : IQuery<int>;

    [Fact]
    public async Task Successful_command_emits_one_ok_span_with_tags()
    {
        var spans = new List<Activity>();
        using var listener = SubscribeToMediatRSource(spans);

        var sut = CreateSut<CreateBrandCommand, Guid>(new Actor(ActorType.Operator, "op-1"));
        var response = await sut.Handle(
            new CreateBrandCommand("BetAnything EU"),
            () => Task.FromResult(Guid.NewGuid()),
            CancellationToken.None
        );

        response.Should().NotBe(Guid.Empty);
        spans.Should().HaveCount(1);
        var span = spans[0];
        span.OperationName.Should().Be("mediatr CreateBrandCommand");
        span.Status.Should().Be(ActivityStatusCode.Ok);
        span.GetTagItem("mediatr.request_type").Should().Be(typeof(CreateBrandCommand).FullName);
        span.GetTagItem("mediatr.request_kind").Should().Be("command");
        span.GetTagItem("actor.type").Should().Be("Operator");
        span.GetTagItem("actor.id").Should().Be("op-1");
    }

    [Fact]
    public async Task Non_generic_command_is_still_tagged_as_command()
    {
        var spans = new List<Activity>();
        using var listener = SubscribeToMediatRSource(spans);

        var sut = CreateSut<FireCommand, MediatR.Unit>();
        await sut.Handle(
            new FireCommand("hi"),
            () => Task.FromResult(MediatR.Unit.Value),
            CancellationToken.None
        );

        spans[0].GetTagItem("mediatr.request_kind").Should().Be("command");
    }

    [Fact]
    public async Task Query_is_tagged_as_query()
    {
        var spans = new List<Activity>();
        using var listener = SubscribeToMediatRSource(spans);

        var sut = CreateSut<GetBrandQuery, int>();
        await sut.Handle(
            new GetBrandQuery(Guid.NewGuid()),
            () => Task.FromResult(1),
            CancellationToken.None
        );

        spans[0].GetTagItem("mediatr.request_kind").Should().Be("query");
    }

    [Fact]
    public async Task Handler_exception_is_recorded_and_rethrown()
    {
        var spans = new List<Activity>();
        using var listener = SubscribeToMediatRSource(spans);

        var sut = CreateSut<CreateBrandCommand, Guid>();
        var boom = new InvalidOperationException("kaboom");

        var act = async () =>
            await sut.Handle(
                new CreateBrandCommand("x"),
                () => throw boom,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("kaboom");
        spans.Should().HaveCount(1);
        var span = spans[0];
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be("kaboom");
    }

    [Fact]
    public async Task Correlation_id_baggage_is_propagated_to_the_span()
    {
        var spans = new List<Activity>();
        using var listener = SubscribeToMediatRSource(spans);

        using var parent = new Activity("parent");
        parent.Start();
        parent.AddBaggage(CorrelationIdMiddleware.BaggageKey, "abc123");

        var sut = CreateSut<GetBrandQuery, int>();
        await sut.Handle(
            new GetBrandQuery(Guid.NewGuid()),
            () => Task.FromResult(1),
            CancellationToken.None
        );

        spans[0].GetTagItem("correlation.id").Should().Be("abc123");
    }

    private static OpenTelemetryBehavior<TRequest, TResponse> CreateSut<TRequest, TResponse>(
        Actor? actor = null
    )
        where TRequest : notnull
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.Current.Returns(actor ?? new Actor(ActorType.System, "system"));
        return new OpenTelemetryBehavior<TRequest, TResponse>(userContext);
    }

    private static ActivityListener SubscribeToMediatRSource(List<Activity> sink)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                string.Equals(source.Name, PamActivitySources.MediatRName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = sink.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
