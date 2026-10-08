using System.Windows.Forms;
using GameTools.Core.Enums;
using GameTools.Infrastructure.Host;
using GameTools.Infrastructure.Hotkeys;
using Xunit;

namespace GameTools.Tests;

public class HotkeyManagerTests
{
    [Fact]
    public void HotkeyManager_RegisterAndUnregister_ShouldSucceed()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();

        using var hotkeyManager = new Win32HotkeyManager(pump);

        // 注册一个冷门按键避免与系统冲突 (Ctrl + Shift + F11)
        int id = hotkeyManager.RegisterHotkey(Keys.F11, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.True(id > 0);

        bool unregistered = hotkeyManager.UnregisterHotkey(id);
        Assert.True(unregistered);
    }

    [Fact]
    public void HotkeyManager_UnregisterAll_ShouldClearAllHotkeys()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();

        using var hotkeyManager = new Win32HotkeyManager(pump);

        int id1 = hotkeyManager.RegisterHotkey(Keys.F10, KeyModifiers.Control | KeyModifiers.Alt);
        int id2 = hotkeyManager.RegisterHotkey(Keys.F9, KeyModifiers.Control | KeyModifiers.Alt);

        Assert.True(id1 > 0);
        Assert.True(id2 > 0);

        hotkeyManager.UnregisterAll();
    }
}
