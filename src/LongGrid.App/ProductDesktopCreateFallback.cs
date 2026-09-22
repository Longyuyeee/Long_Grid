using System.Runtime.InteropServices;

namespace LongGrid.App;

internal static class ProductDesktopCreateFallback
{
    // Cancel is the default. No control-center owner is activated or displayed.
    private const uint Options = 0x00000001 | 0x00000030 | 0x00000100 | 0x00002000 | 0x00010000;

    internal static string? Confirm(string name, Func<string, int>? show = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string message = $"桌面创建预览暂时不可用。\n\n是否使用默认名称“{name}”在桌面创建盒子？\n创建后可在桌面重命名。原始文件不会移动或删除。\n\n选择“取消”不会创建盒子。";
        int result = show is null
            ? MessageBoxW(nint.Zero, message, "新建 Long方格盒子", Options)
            : show(message);
        return result == 1 ? name : null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
