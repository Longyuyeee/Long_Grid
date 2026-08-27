using LongGrid.Core.Taskbar;
using LongGrid.Infrastructure.Taskbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LongGrid.App;

public sealed partial class MainWindow
{
    private bool _taskbarCompatibilityProbeInProgress;

    private async Task RefreshTaskbarCompatibilityAsync()
    {
        if (_taskbarCompatibilityProbeInProgress)
        {
            return;
        }

        _taskbarCompatibilityProbeInProgress = true;
        TaskbarCompatibilityRefreshButton.IsEnabled = false;
        TaskbarCompatibilityInfoBar.Severity = InfoBarSeverity.Informational;
        TaskbarCompatibilityInfoBar.Title = "正在检测任务栏兼容性";
        TaskbarCompatibilityInfoBar.Message =
            "正在安全地执行只读检查；不会修改系统任务栏。";
        TaskbarCompatibilityDetail.Text = "等待独立检测进程返回只读结果。";
        AutomationProperties.SetItemStatus(
            TaskbarCompatibilityInfoBar,
            "TaskbarCompatibility=Probing;Mutation=Disabled");

        try
        {
            TaskbarCompatibilityClientResult result =
                await TaskbarCompatibilityClient.ProbeAsync(
                    TimeSpan.FromSeconds(3));
            ApplyTaskbarCompatibilityResult(result);
        }
        catch (OperationCanceledException)
        {
            ApplyTaskbarCompatibilityFailure(
                "检测已取消",
                "任务栏未发生任何变化；可以稍后重新检测。",
                "ProbeCanceled");
        }
        finally
        {
            _taskbarCompatibilityProbeInProgress = false;
            TaskbarCompatibilityRefreshButton.IsEnabled = true;
        }
    }

    private void ApplyTaskbarCompatibilityResult(
        TaskbarCompatibilityClientResult result)
    {
        if (!result.IsCompleted || result.Report is null)
        {
            ApplyTaskbarCompatibilityFailure(
                result.Status == TaskbarCompatibilityClientStatus.TimedOut
                    ? "任务栏检测超时"
                    : "无法确认任务栏兼容状态",
                "兼容性检测没有返回可信结果。任务栏样式保持关闭，系统没有被修改。",
                result.DiagnosticCode);
            return;
        }

        TaskbarCompatibilityReport report = result.Report;
        string buildDetail =
            $"Windows {report.Actual.OperatingSystemVersion}（Build {report.Actual.WindowsBuild}）";
        string windowDetail =
            $"检测到 {report.Actual.TaskbarWindows.Count} 个任务栏窗口";
        switch (report.RuntimeAdmission)
        {
            case TaskbarRuntimeAdmission.DeniedConflictDetected:
                TaskbarCompatibilityInfoBar.Severity = InfoBarSeverity.Warning;
                TaskbarCompatibilityInfoBar.Title = "检测到其他任务栏工具";
                TaskbarCompatibilityInfoBar.Message =
                    $"为避免冲突，Long方格不会启用任务栏样式。检测到：{string.Join("、", report.Actual.ConflictingProcesses)}。";
                break;
            case TaskbarRuntimeAdmission.DeniedNoCertifiedBuild:
                TaskbarCompatibilityInfoBar.Severity = InfoBarSeverity.Warning;
                TaskbarCompatibilityInfoBar.Title = "当前 Windows 版本尚未认证";
                TaskbarCompatibilityInfoBar.Message =
                    "只读检测已通过，但还缺少该版本的应用、回滚、Explorer 重启和卸载实机证据，因此任务栏样式保持关闭。";
                break;
            case TaskbarRuntimeAdmission.DeniedProbeFailure:
                TaskbarCompatibilityInfoBar.Severity = InfoBarSeverity.Error;
                TaskbarCompatibilityInfoBar.Title = "任务栏环境未通过只读检查";
                TaskbarCompatibilityInfoBar.Message =
                    "检测结果与安全预期不一致。任务栏样式保持关闭，系统没有被修改。";
                break;
            case TaskbarRuntimeAdmission.Allowed:
                TaskbarCompatibilityInfoBar.Severity = InfoBarSeverity.Success;
                TaskbarCompatibilityInfoBar.Title = "任务栏环境通过兼容性检查";
                TaskbarCompatibilityInfoBar.Message =
                    "当前阶段仍只展示检测结果；样式预设会在可恢复写入阶段单独交付。";
                break;
        }

        ApplyTaskbarPresetAvailability(report);

        TaskbarCompatibilityDetail.Text =
            $"{buildDetail}；{windowDetail}；只读耗时 {report.Actual.ProbeMilliseconds:F0} ms；系统修改：无。";
        AutomationProperties.SetItemStatus(
            TaskbarCompatibilityInfoBar,
            $"TaskbarCompatibility={report.RuntimeAdmission};Mutation=Disabled;Build={report.Actual.WindowsBuild}");
        RaiseLiveRegionChanged(TaskbarCompatibilityInfoBar);
    }

    private void ApplyTaskbarPresetAvailability(TaskbarCompatibilityReport report)
    {
        TaskbarPresetAvailability availability =
            TaskbarPresetAvailabilityPolicy.Evaluate(
                report,
                TaskbarNativeAdapterAvailability.Unavailable,
                recoveryPending: false);
        TaskbarClearPresetButton.IsEnabled = availability.ClearEnabled;
        TaskbarSystemDefaultButton.IsEnabled =
            availability.RestoreSystemDefaultEnabled;
        TaskbarPresetAvailabilityText.Text = availability.Status switch
        {
            TaskbarPresetAvailabilityStatus.ConflictDetected =>
                "检测到其他任务栏工具，两个预设均保持关闭。",
            TaskbarPresetAvailabilityStatus.BuildNotCertified =>
                "当前 Windows Build 尚未完成实机认证，预设不可应用。",
            TaskbarPresetAvailabilityStatus.AdapterUnavailable =>
                "当前版本没有经过认证的原生适配器，预设不可应用。",
            TaskbarPresetAvailabilityStatus.Ready =>
                "环境与适配器均已通过准入，可以应用通透预设。",
            _ => "任务栏只读检查未通过，预设不可应用。",
        };
        AutomationProperties.SetItemStatus(
            TaskbarPresetGrid,
            $"PresetAvailability={availability.Status};ClearEnabled={availability.ClearEnabled};RestoreEnabled={availability.RestoreSystemDefaultEnabled};Mutation=Disabled");
    }

    private void ApplyTaskbarCompatibilityFailure(
        string title,
        string message,
        string diagnosticCode)
    {
        TaskbarCompatibilityInfoBar.Severity = InfoBarSeverity.Error;
        TaskbarCompatibilityInfoBar.Title = title;
        TaskbarCompatibilityInfoBar.Message = message;
        TaskbarCompatibilityDetail.Text =
            $"诊断代码：{diagnosticCode}；系统修改：无。";
        TaskbarClearPresetButton.IsEnabled = false;
        TaskbarSystemDefaultButton.IsEnabled = false;
        TaskbarPresetAvailabilityText.Text =
            "无法确认任务栏环境，两个预设均保持关闭。";
        AutomationProperties.SetItemStatus(
            TaskbarPresetGrid,
            $"PresetAvailability=ProbeUnavailable;ClearEnabled=False;RestoreEnabled=False;Diagnostic={diagnosticCode};Mutation=Disabled");
        AutomationProperties.SetItemStatus(
            TaskbarCompatibilityInfoBar,
            $"TaskbarCompatibility=Unavailable;Mutation=Disabled;Diagnostic={diagnosticCode}");
        RaiseLiveRegionChanged(TaskbarCompatibilityInfoBar);
    }

    private void TaskbarCompatibilityRefreshButton_Click(
        object sender,
        RoutedEventArgs e) => _ = RefreshTaskbarCompatibilityAsync();
}
