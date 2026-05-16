using System.ComponentModel;

using AwesomeAssertions;

using EasySynQ.UI.Documents.Reviewers;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.Reviewers;

/// <summary>
/// Unit tests for <see cref="ReviewerPickerViewModel"/> (ADR 0008
/// C6b stop 2). Pure VM behavior — no Control construction, no DI
/// graph, no WPF surface. Selection sync from
/// <see cref="System.Windows.Controls.ListBox.SelectedItems"/> is
/// exercised separately via the control code-behind; these tests
/// drive <see cref="ReviewerPickerViewModel.SelectedCandidates"/>
/// directly.
/// </summary>
public class ReviewerPickerViewModelTests
{
    private static ReviewerCandidate Alice =>
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "Alice Smith", "asmith");
    private static ReviewerCandidate Bob =>
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "Bob Johnson", "bjohnson");
    private static ReviewerCandidate Carol =>
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"), "Carol Davis", "cdavis");

    private static ReviewerPickerViewModel NewVm(params ReviewerCandidate[] candidates)
        => new(candidates);

    [Fact]
    public void Constructor_ExposesCandidatesInSuppliedOrder()
    {
        var vm = NewVm(Alice, Bob, Carol);

        vm.Candidates.Should().Equal(Alice, Bob, Carol);
    }

    [Fact]
    public void Constructor_NullCandidates_Throws()
    {
        Action act = () => new ReviewerPickerViewModel(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyCandidateList_IsAllowed()
    {
        var vm = NewVm();

        vm.Candidates.Should().BeEmpty();
        vm.FilteredCandidates.Should().BeEmpty();
    }

    [Fact]
    public void SelectedCandidates_StartsEmpty()
    {
        var vm = NewVm(Alice, Bob, Carol);

        vm.SelectedCandidates.Should().BeEmpty();
    }

    [Fact]
    public void FilterText_StartsEmpty()
    {
        var vm = NewVm(Alice, Bob, Carol);

        vm.FilterText.Should().BeEmpty();
    }

    [Fact]
    public void FilteredCandidates_EmptyFilter_ReturnsAllCandidatesInOrder()
    {
        var vm = NewVm(Alice, Bob, Carol);

        vm.FilteredCandidates.Should().Equal(Alice, Bob, Carol);
    }

    [Fact]
    public void FilteredCandidates_WhitespaceFilter_ReturnsAllCandidates()
    {
        var vm = NewVm(Alice, Bob, Carol);
        vm.FilterText = "   ";

        vm.FilteredCandidates.Should().Equal(Alice, Bob, Carol);
    }

    [Fact]
    public void FilteredCandidates_MatchesDisplayNameSubstring()
    {
        var vm = NewVm(Alice, Bob, Carol);
        vm.FilterText = "Smith";

        vm.FilteredCandidates.Should().ContainSingle().Which.Should().Be(Alice);
    }

    [Fact]
    public void FilteredCandidates_MatchesUsernameSubstring()
    {
        var vm = NewVm(Alice, Bob, Carol);
        vm.FilterText = "cdavis";

        vm.FilteredCandidates.Should().ContainSingle().Which.Should().Be(Carol);
    }

    [Fact]
    public void FilteredCandidates_FilterIsCaseInsensitive()
    {
        var vm = NewVm(Alice, Bob, Carol);
        vm.FilterText = "BOB";

        vm.FilteredCandidates.Should().ContainSingle().Which.Should().Be(Bob);
    }

    [Fact]
    public void FilteredCandidates_NoMatch_ReturnsEmpty()
    {
        var vm = NewVm(Alice, Bob, Carol);
        vm.FilterText = "zzz-no-match";

        vm.FilteredCandidates.Should().BeEmpty();
    }

    [Fact]
    public void FilteredCandidates_PreservesInputOrderAcrossFilter()
    {
        var dee = new ReviewerCandidate(
            Guid.Parse("00000000-0000-0000-0000-000000000004"), "Dee Ander", "dander");
        // Three matches on "d": Bob "Johnson" (no), Carol "Davis"
        // (matches), Dee "Ander"/"dander" (matches), and Alice (no).
        var vm = NewVm(Alice, Bob, Carol, dee);
        vm.FilterText = "d";

        vm.FilteredCandidates.Should().Equal(Carol, dee);
    }

    [Fact]
    public void FilterTextChange_RaisesPropertyChangedForFilteredCandidates()
    {
        var vm = NewVm(Alice, Bob, Carol);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.FilterText = "Alice";

        changed.Should().Contain(nameof(ReviewerPickerViewModel.FilterText));
        changed.Should().Contain(nameof(ReviewerPickerViewModel.FilteredCandidates));
    }

    [Fact]
    public void SelectedCandidates_IsExternallyMutable()
    {
        var vm = NewVm(Alice, Bob, Carol);

        vm.SelectedCandidates.Add(Bob);
        vm.SelectedCandidates.Add(Carol);

        vm.SelectedCandidates.Should().Equal(Bob, Carol);
    }

    [Fact]
    public void SelectedCandidates_SurvivesFilterNarrowing()
    {
        // The picker VM does NOT mutate SelectedCandidates in
        // response to filter changes — that's load-bearing for the
        // submit-for-review dialog's OK-can-execute gating.
        var vm = NewVm(Alice, Bob, Carol);
        vm.SelectedCandidates.Add(Alice);
        vm.SelectedCandidates.Add(Bob);

        vm.FilterText = "Carol";

        vm.FilteredCandidates.Should().ContainSingle().Which.Should().Be(Carol);
        vm.SelectedCandidates.Should().Equal(Alice, Bob);
    }

    [Fact]
    public void ReviewerCandidate_EqualityIsById()
    {
        var aliceA = new ReviewerCandidate(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "Alice Smith", "asmith");
        var aliceB = new ReviewerCandidate(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "Alice CHANGED", "asmith-new");

        aliceA.Should().Be(aliceB);
        aliceA.GetHashCode().Should().Be(aliceB.GetHashCode());
    }

    [Fact]
    public void ReviewerCandidate_InequalityByDifferentIds()
    {
        var alice = new ReviewerCandidate(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "Alice", "asmith");
        var bob = new ReviewerCandidate(
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            "Alice", "asmith");

        alice.Should().NotBe(bob);
    }
}
