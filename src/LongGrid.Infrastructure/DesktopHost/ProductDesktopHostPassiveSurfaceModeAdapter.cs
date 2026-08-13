using LongGrid.Core.DesktopHost;

namespace LongGrid.Infrastructure.DesktopHost;

internal sealed class ProductDesktopHostPassiveSurfaceModeAdapter(
    IReadOnlyList<IProductDesktopHostReadOnlySurface> surfaces,
    long registryGeneration)
    : IProductDesktopInteractionSurfaceModeAdapter
{
    private readonly IReadOnlyList<IProductDesktopHostReadOnlySurface> surfaces =
        surfaces is { Count: > 0 }
            ? surfaces
            : throw new ArgumentException(
                "At least one owned product surface is required.",
                nameof(surfaces));
    private readonly long registryGeneration = registryGeneration > 0
        ? registryGeneration
        : throw new ArgumentOutOfRangeException(nameof(registryGeneration));

    public ProductDesktopInteractionSurfaceCapture Capture()
    {
        bool passive = surfaces.All(surface =>
            surface.PassiveWindowContractAttested);
        bool hidden = surfaces.All(surface =>
            surface.HiddenWindowContractAttested);
        if (passive == hidden)
        {
            return ProductDesktopInteractionSurfaceCapture.Failed;
        }

        return new(
            true,
            new(
                passive
                    ? ProductDesktopInteractionSurfaceMode.Passive
                    : ProductDesktopInteractionSurfaceMode.Hidden,
                registryGeneration,
                Visible: passive,
                HitTestTransparent: true,
                IsKeyboardFocusable: false,
                SelectionPatternAvailable: false,
                ToolWindow: true,
                NoActivate: true,
                Topmost: false,
                HasOwner: false,
                OwnsForeground: false));
    }

    public bool ApplyExplicit(ProductDesktopInteractionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return false;
    }

    public bool ApplyPassive(long expectedWindowRegistryGeneration) =>
        Apply(
            expectedWindowRegistryGeneration,
            surface => surface.ApplyPassive(),
            evidence => evidence.IsPassiveContract,
            hideOperation: false);

    public bool Restore(ProductDesktopInteractionSurfaceEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence.WindowRegistryGeneration == registryGeneration
            && evidence.Mode switch
            {
                ProductDesktopInteractionSurfaceMode.Passive =>
                    ApplyPassive(registryGeneration),
                ProductDesktopInteractionSurfaceMode.Hidden =>
                    Hide(registryGeneration),
                _ => false,
            };
    }

    public bool Hide(long expectedWindowRegistryGeneration) =>
        Apply(
            expectedWindowRegistryGeneration,
            surface => surface.ApplyHidden(),
            evidence => evidence.IsHiddenContract,
            hideOperation: true);

    private bool Apply(
        long expectedWindowRegistryGeneration,
        Func<IProductDesktopHostReadOnlySurface, bool> operation,
        Func<ProductDesktopInteractionSurfaceEvidence, bool> verify,
        bool hideOperation)
    {
        if (expectedWindowRegistryGeneration != registryGeneration)
        {
            return false;
        }

        bool succeeded = true;
        foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
        {
            succeeded &= SafeApply(() => operation(surface));
        }

        ProductDesktopInteractionSurfaceCapture capture = Capture();
        bool verified = succeeded
            && capture.Succeeded
            && capture.Evidence is not null
            && verify(capture.Evidence);
        if (!verified && !hideOperation)
        {
            foreach (IProductDesktopHostReadOnlySurface surface in surfaces)
            {
                _ = SafeApply(surface.ApplyHidden);
            }
        }

        return verified;
    }

    private static bool SafeApply(Func<bool> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return false;
        }
    }
}
