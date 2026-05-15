using AwesomeAssertions;

using EasySynQ.Domain.Common;
using EasySynQ.Services.Events;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Unit.Services.Events;

/// <summary>
/// Unit tests for <see cref="DomainEventDispatcher"/> (ADR 0008 C3).
/// Exercises queue semantics, handler invocation, multi-handler fan-out,
/// no-handler no-op, exception propagation, and clear.
/// </summary>
public class DomainEventDispatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record SampleEventA(string Tag) : IDomainEvent;
    private sealed record SampleEventB(int Number) : IDomainEvent;

    private sealed class RecordingHandlerA : IDomainEventHandler<SampleEventA>
    {
        public List<SampleEventA> Received { get; } = [];

        public Task HandleAsync(SampleEventA domainEvent, CancellationToken cancellationToken)
        {
            Received.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandlerB : IDomainEventHandler<SampleEventB>
    {
        public List<SampleEventB> Received { get; } = [];

        public Task HandleAsync(SampleEventB domainEvent, CancellationToken cancellationToken)
        {
            Received.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandlerA : IDomainEventHandler<SampleEventA>
    {
        public Task HandleAsync(SampleEventA domainEvent, CancellationToken cancellationToken)
            => throw new InvalidOperationException("handler failure");
    }

    [Fact]
    public void Enqueue_RejectsNull()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(sp);

        Action act = () => dispatcher.Enqueue(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HasPending_ReflectsQueueState()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(sp);

        dispatcher.HasPending.Should().BeFalse();

        dispatcher.Enqueue(new SampleEventA("x"));
        dispatcher.HasPending.Should().BeTrue();

        dispatcher.Clear();
        dispatcher.HasPending.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchPending_WithNoHandlers_Drains_NoSideEffectsAsync()
    {
        // Phase 2 default state — no handlers registered for the event
        // type. The dispatcher must drain silently.
        var sp = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(sp);
        dispatcher.Enqueue(new SampleEventA("alpha"));
        dispatcher.Enqueue(new SampleEventA("beta"));

        await dispatcher.DispatchPendingAsync(Ct);

        dispatcher.HasPending.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchPending_InvokesRegisteredHandlerInEnqueueOrderAsync()
    {
        var handler = new RecordingHandlerA();
        var sp = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<SampleEventA>>(handler)
            .BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(sp);

        dispatcher.Enqueue(new SampleEventA("first"));
        dispatcher.Enqueue(new SampleEventA("second"));
        dispatcher.Enqueue(new SampleEventA("third"));

        await dispatcher.DispatchPendingAsync(Ct);

        handler.Received.Select(e => e.Tag).Should().Equal("first", "second", "third");
        dispatcher.HasPending.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchPending_RoutesEventsToCorrectHandlerByTypeAsync()
    {
        var handlerA = new RecordingHandlerA();
        var handlerB = new RecordingHandlerB();
        var sp = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<SampleEventA>>(handlerA)
            .AddSingleton<IDomainEventHandler<SampleEventB>>(handlerB)
            .BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(sp);

        dispatcher.Enqueue(new SampleEventA("a-1"));
        dispatcher.Enqueue(new SampleEventB(42));
        dispatcher.Enqueue(new SampleEventA("a-2"));

        await dispatcher.DispatchPendingAsync(Ct);

        handlerA.Received.Select(e => e.Tag).Should().Equal("a-1", "a-2");
        handlerB.Received.Select(e => e.Number).Should().Equal(42);
    }

    [Fact]
    public async Task DispatchPending_FanOutsAcrossMultipleHandlersForSameEventTypeAsync()
    {
        var handlerOne = new RecordingHandlerA();
        var handlerTwo = new RecordingHandlerA();
        var sp = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<SampleEventA>>(handlerOne)
            .AddSingleton<IDomainEventHandler<SampleEventA>>(handlerTwo)
            .BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(sp);

        dispatcher.Enqueue(new SampleEventA("only"));

        await dispatcher.DispatchPendingAsync(Ct);

        handlerOne.Received.Select(e => e.Tag).Should().Equal("only");
        handlerTwo.Received.Select(e => e.Tag).Should().Equal("only");
    }

    [Fact]
    public async Task DispatchPending_AfterDrain_IsNoOpAsync()
    {
        var handler = new RecordingHandlerA();
        var sp = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<SampleEventA>>(handler)
            .BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(sp);

        dispatcher.Enqueue(new SampleEventA("once"));
        await dispatcher.DispatchPendingAsync(Ct);
        await dispatcher.DispatchPendingAsync(Ct);

        // Handler invoked exactly once — no re-fire.
        handler.Received.Should().ContainSingle().Which.Tag.Should().Be("once");
    }

    [Fact]
    public async Task DispatchPending_HandlerException_PropagatesAndStopsDispatchAsync()
    {
        var sp = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<SampleEventA>>(new ThrowingHandlerA())
            .BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(sp);

        dispatcher.Enqueue(new SampleEventA("fails"));
        dispatcher.Enqueue(new SampleEventA("never-reached"));

        Func<Task> act = async () => await dispatcher.DispatchPendingAsync(Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*handler failure*");

        // The remaining queued event is left for a later drain (or
        // discard) — the failed dispatch surfaces the exception so
        // the surrounding SaveChanges rolls back; the unprocessed
        // event would normally vanish with the rolled-back scope.
        dispatcher.HasPending.Should().BeTrue();
    }

    [Fact]
    public void Clear_EmptiesQueueWithoutDispatch()
    {
        var handler = new RecordingHandlerA();
        var sp = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<SampleEventA>>(handler)
            .BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(sp);

        dispatcher.Enqueue(new SampleEventA("a"));
        dispatcher.Enqueue(new SampleEventA("b"));

        dispatcher.Clear();

        dispatcher.HasPending.Should().BeFalse();
        handler.Received.Should().BeEmpty();
    }
}
