using LongGrid.Core.Configuration;

namespace LongGrid.Core.Tests.Configuration;

public sealed class ProductWorkspaceContainerNavigationPolicyTests
{
    private static readonly int[] One = [1];
    private static readonly int[] Two = [2];
    private static readonly int[] OneAndThree = [1, 3];
    private static readonly int[] DuplicateTwo = [2, 2];
    private static readonly int[] OneTwoThree = [1, 2, 3];
    private static readonly int[] ThreeTwoOne = [3, 2, 1];

    [Fact]
    public void ExactWorkspaceAndCandidateMatchResolvesCandidateIndex()
    {
        Assert.Equal(1, ProductWorkspaceContainerNavigationPolicy.ResolveCandidateIndex(
            requestedOrdinal: 2,
            workspaceOrdinals: OneTwoThree,
            candidateOrdinals: ThreeTwoOne));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidOrdinalFailsClosed(int requestedOrdinal)
    {
        Assert.Equal(-1, ProductWorkspaceContainerNavigationPolicy.ResolveCandidateIndex(
            requestedOrdinal,
            workspaceOrdinals: One,
            candidateOrdinals: One));
    }

    [Fact]
    public void MissingWorkspaceMatchFailsClosed()
    {
        Assert.Equal(-1, ProductWorkspaceContainerNavigationPolicy.ResolveCandidateIndex(
            requestedOrdinal: 2,
            workspaceOrdinals: OneAndThree,
            candidateOrdinals: Two));
    }

    [Fact]
    public void DuplicateWorkspaceMatchFailsClosed()
    {
        Assert.Equal(-1, ProductWorkspaceContainerNavigationPolicy.ResolveCandidateIndex(
            requestedOrdinal: 2,
            workspaceOrdinals: DuplicateTwo,
            candidateOrdinals: Two));
    }

    [Fact]
    public void MissingCandidateMatchFailsClosed()
    {
        Assert.Equal(-1, ProductWorkspaceContainerNavigationPolicy.ResolveCandidateIndex(
            requestedOrdinal: 2,
            workspaceOrdinals: Two,
            candidateOrdinals: OneAndThree));
    }

    [Fact]
    public void DuplicateCandidateMatchFailsClosed()
    {
        Assert.Equal(-1, ProductWorkspaceContainerNavigationPolicy.ResolveCandidateIndex(
            requestedOrdinal: 2,
            workspaceOrdinals: Two,
            candidateOrdinals: DuplicateTwo));
    }

    [Fact]
    public void NullCollectionsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProductWorkspaceContainerNavigationPolicy.ResolveCandidateIndex(
                requestedOrdinal: 1,
                workspaceOrdinals: null!,
                candidateOrdinals: Array.Empty<int>()));
        Assert.Throws<ArgumentNullException>(() =>
            ProductWorkspaceContainerNavigationPolicy.ResolveCandidateIndex(
                requestedOrdinal: 1,
                workspaceOrdinals: Array.Empty<int>(),
                candidateOrdinals: null!));
    }
}
