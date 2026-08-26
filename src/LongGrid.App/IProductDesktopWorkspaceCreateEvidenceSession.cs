namespace LongGrid.App;

internal interface IProductDesktopWorkspaceCreateEvidenceSession
{
    bool TryTakePreviewResponse(out string? response);

    void RecordStage(string stage);

    void ObservePreview(DesktopWorkspaceCreatePreviewWindow previewWindow);

    void ObserveFallbackPreview(bool hasEvidenceVisualTree);

    void ObserveSafePreview(bool hasEvidenceVisualTree);
}
