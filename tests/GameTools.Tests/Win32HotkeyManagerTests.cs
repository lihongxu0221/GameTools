using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Infrastructure.Host;
using GameTools.Infrastructure.Hotkeys;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// <see cref="Win32HotkeyManager"/> 的注册、冲突与生命周期测试。
/// </summary>
/// <remarks>
/// 覆盖 REQ-HK-01/02/03：注册与注销、冲突诊断、消息线程归属。
/// 测试只注册并立即注销测试组合键，不会触发输入模拟。
/// </remarks>
[Collection("MessagePump")]
public sealed class Win32HotkeyManagerTests
{
    [Fact]
    public void RegisterAndUnregister_ShouldSucceed()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();
        using var manager = new Win32HotkeyManager(pump);

        int id = manager.RegisterHotkey(VirtualKey.F12, KeyModifiers.Control | KeyModifiers.Shift, () => { });

        Assert.True(id > 0);
        Assert.True(manager.UnregisterHotkey(id));
    }

    [Fact]
    public void Register_SameCombinationTwice_ShouldThrowDuplicateError()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();
        using var manager = new Win32HotkeyManager(pump);

        int id = manager.RegisterHotkey(VirtualKey.F11, KeyModifiers.Control | KeyModifiers.Alt, () => { });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => manager.RegisterHotkey(VirtualKey.F11, KeyModifiers.Control | KeyModifiers.Alt));

        // 本进程重复注册与被外部占用必须给出可区分的诊断
        Assert.Contains("当前进程中注册", ex.Message);

        manager.UnregisterHotkey(id);
    }

    [Fact]
    public void Register_AfterReleasingSameCombination_ShouldSucceed()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();
        using var manager = new Win32HotkeyManager(pump);

        int first = manager.RegisterHotkey(VirtualKey.F10, KeyModifiers.Control | KeyModifiers.Shift, () => { });
        Assert.True(manager.UnregisterHotkey(first));

        // 注销后应能再次注册同一组合
        int second = manager.RegisterHotkey(VirtualKey.F10, KeyModifiers.Control | KeyModifiers.Shift, () => { });
        Assert.True(second > 0);
        Assert.True(manager.UnregisterHotkey(second));
    }

    [Fact]
    public void Register_AfterStop_ShouldThrowInsteadOfHanging()
    {
        var pump = new BackgroundMessagePump();
        pump.Start();

        using var manager = new Win32HotkeyManager(pump);
        pump.Stop();

        Assert.Throws<InvalidOperationException>(
            () => manager.RegisterHotkey(VirtualKey.F9, KeyModifiers.Control, () => { }));

        manager.Dispose();
        pump.Dispose();
    }

    [Fact]
    public void Unregister_WithUnknownId_ShouldReturnFalse()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();
        using var manager = new Win32HotkeyManager(pump);

        Assert.False(manager.UnregisterHotkey(999999));
    }

    [Fact]
    public void UnregisterAll_ShouldClearRegistrationsAndAllowReregistration()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();
        using var manager = new Win32HotkeyManager(pump);

        int a = manager.RegisterHotkey(VirtualKey.F7, KeyModifiers.Control, () => { });
        int b = manager.RegisterHotkey(VirtualKey.F8, KeyModifiers.Control, () => { });

        manager.UnregisterAll();

        // 批量注销后同一组合可重新注册，说明原生侧确实已释放
        int again = manager.RegisterHotkey(VirtualKey.F7, KeyModifiers.Control, () => { });
        Assert.True(again > 0);

        _ = a;
        _ = b;
    }

    [Fact]
    public void Dispose_ShouldReleaseAllHotkeys()
    {
        var pump = new BackgroundMessagePump();
        pump.Start();

        var manager = new Win32HotkeyManager(pump);
        manager.RegisterHotkey(VirtualKey.F6, KeyModifiers.Control | KeyModifiers.Shift, () => { });
        manager.Dispose();

        // 释放后另一管理器应能注册同一组合
        using var next = new Win32HotkeyManager(pump);
        int id = next.RegisterHotkey(VirtualKey.F6, KeyModifiers.Control | KeyModifiers.Shift, () => { });
        Assert.True(id > 0);

        pump.Dispose();
    }

    [Fact]
    public void Register_WithNoRepeat_ShouldBeAccepted()
    {
        using var pump = new BackgroundMessagePump();
        pump.Start();
        using var manager = new Win32HotkeyManager(pump);

        int id = manager.RegisterHotkey(
            VirtualKey.F5,
            KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.NoRepeat,
            () => { });

        Assert.True(id > 0);
        Assert.True(manager.UnregisterHotkey(id));
    }
}