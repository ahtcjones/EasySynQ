using AwesomeAssertions;

using EasySynQ.UI.Navigation;
using EasySynQ.UI.Placeholders;
using EasySynQ.UI.Pulse;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Navigation;

public class MainShellViewModelTests
{
    private readonly Mock<ILogger<MainShellViewModel>> _logger = new();

    private static PulseDrawerViewModel NewDrawer() =>
        new(Mock.Of<IPulseSource>(), NullLogger<PulseDrawerViewModel>.Instance);

    private MainShellViewModel BuildSut()
    {
        _logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        // The drawer VM is unused by the nav-focused tests in this
        // file — a default-mock IPulseSource behind a fresh drawer
        // satisfies the constructor without changing observable
        // behavior on any test.
        return new MainShellViewModel(_logger.Object, NewDrawer());
    }

    [Fact]
    public async Task NavigateToAsync_NullTarget_DoesNothingAsync()
    {
        var sut = BuildSut();
        // Seed a dirty content so we'd notice if the guard ran inadvertently.
        var confirmCalls = 0;
        sut.SetCurrentContentForTesting(new FakeDirtyContent
        {
            HasUnsavedChanges = true,
            ConfirmDiscardHandler = _ => { confirmCalls++; return Task.FromResult(true); },
        });

        await sut.NavigateToCommand.ExecuteAsync(null);

        sut.SelectedItem.Should().BeNull();
        confirmCalls.Should().Be(0, "a null target short-circuits before the dirty-state check");
    }

    [Fact]
    public async Task NavigateToAsync_SameAsCurrent_DoesNothingAsync()
    {
        var sut = BuildSut();
        var target = NavigationCatalog.AllItems[0];
        sut.SelectedItem = target;

        var confirmCalls = 0;
        sut.SetCurrentContentForTesting(new FakeDirtyContent
        {
            HasUnsavedChanges = true,
            ConfirmDiscardHandler = _ => { confirmCalls++; return Task.FromResult(true); },
        });

        await sut.NavigateToCommand.ExecuteAsync(target);

        sut.SelectedItem.Should().BeSameAs(target);
        confirmCalls.Should().Be(0,
            "navigating to the currently-selected item must bypass the dirty-state check");
    }

    [Fact]
    public async Task NavigateToAsync_NoCurrentContent_SetsSelectedItemImmediatelyAsync()
    {
        var sut = BuildSut();
        sut.CurrentContent.Should().BeNull("default state — no content has been assigned");
        var target = NavigationCatalog.AllItems[0];

        await sut.NavigateToCommand.ExecuteAsync(target);

        sut.SelectedItem.Should().BeSameAs(target);
    }

    [Fact]
    public async Task NavigateToAsync_CurrentContentNotDirtyStateAware_SetsSelectedItemAsync()
    {
        var sut = BuildSut();
        sut.SetCurrentContentForTesting(new object());  // not IDirtyStateAware
        var target = NavigationCatalog.AllItems[0];

        await sut.NavigateToCommand.ExecuteAsync(target);

        sut.SelectedItem.Should().BeSameAs(target);
    }

    [Fact]
    public async Task NavigateToAsync_CurrentContentDirtyButNoUnsavedChanges_SetsSelectedItemAsync()
    {
        var sut = BuildSut();
        var confirmCalls = 0;
        sut.SetCurrentContentForTesting(new FakeDirtyContent
        {
            HasUnsavedChanges = false,
            ConfirmDiscardHandler = _ => { confirmCalls++; return Task.FromResult(true); },
        });
        var target = NavigationCatalog.AllItems[0];

        await sut.NavigateToCommand.ExecuteAsync(target);

        sut.SelectedItem.Should().BeSameAs(target);
        confirmCalls.Should().Be(0,
            "ConfirmDiscardAsync is gated on HasUnsavedChanges; the prompt must not surface for clean content");
    }

    [Fact]
    public async Task NavigateToAsync_CurrentContentDirtyWithChangesAndConfirmAllows_SetsSelectedItemAsync()
    {
        var sut = BuildSut();
        var dirty = new FakeDirtyContent
        {
            HasUnsavedChanges = true,
            ConfirmDiscardHandler = _ => Task.FromResult(true),
        };
        sut.SetCurrentContentForTesting(dirty);
        var target = NavigationCatalog.AllItems[1];

        var cancelledCalls = 0;
        sut.NavigationCancelled += (_, _) => cancelledCalls++;

        await sut.NavigateToCommand.ExecuteAsync(target);

        sut.SelectedItem.Should().BeSameAs(target);
        cancelledCalls.Should().Be(0);
    }

    [Fact]
    public async Task NavigateToAsync_CurrentContentDirtyWithChangesAndConfirmCancels_DoesNotChangeSelectedItemAndRaisesNavigationCancelledAsync()
    {
        var sut = BuildSut();
        var origin = NavigationCatalog.AllItems[0];
        sut.SelectedItem = origin;

        sut.SetCurrentContentForTesting(new FakeDirtyContent
        {
            HasUnsavedChanges = true,
            ConfirmDiscardHandler = _ => Task.FromResult(false),
        });
        var target = NavigationCatalog.AllItems[1];

        NavigationItem? cancelledTarget = null;
        var cancelledCalls = 0;
        sut.NavigationCancelled += (_, e) => { cancelledTarget = e.AttemptedTarget; cancelledCalls++; };

        await sut.NavigateToCommand.ExecuteAsync(target);

        sut.SelectedItem.Should().BeSameAs(origin,
            "rejected nav must not move the selection off the previous item");
        cancelledCalls.Should().Be(1);
        cancelledTarget.Should().BeSameAs(target);
    }

    [Fact]
    public void NavigationCatalog_AllItems_HasOneEntryPerSpecPhaseModule()
    {
        // Locks the SPEC §9 mapping at test-time. Editing the catalog
        // without updating this list (or vice versa) is the failure
        // signal — anyone making a structural change must touch both
        // and re-read SPEC §9 in the process.
        var expected = new (string Id, NavigationSection Section, int Phase, bool Available)[]
        {
            ("pulse.dashboard",              NavigationSection.Pulse,      1, true),
            ("governance.documents",         NavigationSection.Governance, 2, false),
            ("governance.risk",              NavigationSection.Governance, 3, false),
            ("governance.competency",        NavigationSection.Governance, 4, false),
            ("governance.audits",            NavigationSection.Governance, 9, false),
            ("governance.management-review", NavigationSection.Governance, 9, false),
            ("operations.suppliers",         NavigationSection.Operations, 3, false),
            ("operations.assets",            NavigationSection.Operations, 5, false),
            ("operations.material",          NavigationSection.Operations, 6, false),
            ("operations.production",        NavigationSection.Operations, 7, false),
            ("quality.ncr",                  NavigationSection.Quality,    8, false),
            ("quality.capa",                 NavigationSection.Quality,    8, false),
            ("insights.analytics",           NavigationSection.Insights,  10, false),
        };

        var actual = NavigationCatalog.AllItems
            .Select(i => (i.Id, i.Section, Phase: i.TargetPhase, Available: i.IsAvailable))
            .ToList();

        actual.Should().BeEquivalentTo(expected, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void NavigationCatalog_PulseDashboard_IsAvailable()
    {
        var pulse = NavigationCatalog.AllItems.Single(i => i.Id == "pulse.dashboard");
        pulse.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void NavigationCatalog_AllNonPulseItems_AreNotAvailable()
    {
        var nonPulse = NavigationCatalog.AllItems.Where(i => i.Id != "pulse.dashboard");
        nonPulse.Should().AllSatisfy(i => i.IsAvailable.Should().BeFalse());
    }

    [Fact]
    public async Task NavigateToAsync_SetsCurrentContentFromFactoryAsync()
    {
        var sut = BuildSut();

        // Pulse Dashboard → PulseDashboardViewModel.
        await sut.NavigateToCommand.ExecuteAsync(
            NavigationCatalog.AllItems.Single(i => i.Id == "pulse.dashboard"));
        sut.CurrentContent.Should().BeOfType<PulseDashboardViewModel>();

        // Any other entry → ComingSoonViewModel carrying that item's data.
        var documents = NavigationCatalog.AllItems.Single(i => i.Id == "governance.documents");
        await sut.NavigateToCommand.ExecuteAsync(documents);
        var coming = sut.CurrentContent.Should().BeOfType<ComingSoonViewModel>().Subject;
        coming.DisplayName.Should().Be(documents.DisplayName);
    }

    [Fact]
    public async Task NavigateToAsync_DirtyContentRejectsConfirm_DoesNotSwapCurrentContentAsync()
    {
        var sut = BuildSut();
        var dirty = new FakeDirtyContent
        {
            HasUnsavedChanges = true,
            ConfirmDiscardHandler = _ => Task.FromResult(false),
        };
        sut.SetCurrentContentForTesting(dirty);

        var target = NavigationCatalog.AllItems.Single(i => i.Id == "governance.documents");
        await sut.NavigateToCommand.ExecuteAsync(target);

        sut.CurrentContent.Should().BeSameAs(dirty,
            "rejected nav must leave CurrentContent on the prior dirty VM, not the would-be placeholder");
    }

    /// <summary>
    /// Test double for <see cref="IDirtyStateAware"/>. Public mutable
    /// state so each test sets the scenario it needs; the
    /// <see cref="ConfirmDiscardHandler"/> delegate lets tests both
    /// supply the boolean answer and observe how many times the prompt
    /// was reached.
    /// </summary>
    private sealed class FakeDirtyContent : IDirtyStateAware
    {
        public bool HasUnsavedChanges { get; set; }
        public Func<CancellationToken, Task<bool>>? ConfirmDiscardHandler { get; set; }

        public Task<bool> ConfirmDiscardAsync(CancellationToken cancellationToken)
            => ConfirmDiscardHandler is null
                ? Task.FromResult(true)
                : ConfirmDiscardHandler(cancellationToken);
    }
}
