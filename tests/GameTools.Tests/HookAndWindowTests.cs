using GameTools.Infrastructure.Hooks;
using GameTools.Win32.Helpers;
using Xunit;

namespace GameTools.Tests;

public class HookAndWindowTests
{
    [Fact]
    public void LowLevelHookManager_StartAndStop_ShouldNotThrow()
    {
        using var hookManager = new LowLevelHookManager();
        hookManager.StartKeyboardHook();
        hookManager.StartMouseHook();

        hookManager.StopKeyboardHook();
        hookManager.StopMouseHook();
    }

    [Fact]
    public void WindowHelper_FindTopLevelWindows_ShouldReturnWindows()
    {
        var windows = WindowHelper.FindTopLevelWindows();
        Assert.NotNull(windows);
        Assert.NotEmpty(windows);

        foreach (var w in windows)
        {
            Assert.NotEqual(IntPtr.Zero, w.Handle);
            Assert.NotNull(w.Title);
        }
    }
}
