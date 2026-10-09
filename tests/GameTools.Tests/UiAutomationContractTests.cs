using System;
using System.Diagnostics;
using System.Linq;
using GameTools.Core.Abstractions;
using GameTools.Core.Enums;
using GameTools.Core.Models;
using GameTools.Infrastructure.Automation;
using Xunit;

namespace GameTools.Tests;

/// <summary>
/// 控件操作契约与传统 Win32 控件兜底路径的回归测试。
/// </summary>
public class UiAutomationContractTests
{
    /// <summary>
    /// 完整性级别探测应能识别当前进程级别。
    /// </summary>
    /// <remarks>
    /// 当前进程必然存在且可读取自己的令牌，因此必须返回非空；
    /// 具体级别取决于运行方式（普通运行或管理员运行），不断言其值。
    /// </remarks>
    [Fact]
    public void GetProcessIntegrityLevel_CurrentProcess_ShouldReturnValue()
    {
        var probe = new IntegrityLevelProbe();
        IntegrityLevel? level = probe.GetProcessIntegrityLevel(0);

        Assert.NotNull(level);
        Assert.True(
            Enum.IsDefined(typeof(IntegrityLevel), level!.Value),
            $"返回了未定义的完整性级别: {level.Value}");
    }

    /// <summary>
    /// 对自身进程做风险判断不应提示提权。
    /// </summary>
    /// <remarks>
    /// 当前进程级别不可能低于自身，锁定不会产生误报。
    /// </remarks>
    [Fact]
    public void CheckElevationRisk_SelfProcess_ShouldNotWarn()
    {
        var probe = new IntegrityLevelProbe();

        int selfProcessId;
        using (Process current = Process.GetCurrentProcess())
        {
            selfProcessId = current.Id;
        }

        Assert.Null(probe.CheckElevationRisk(selfProcessId));
    }

    /// <summary>
    /// 对不存在的进程应返回 null 而非抛异常。
    /// </summary>
    [Fact]
    public void GetProcessIntegrityLevel_InvalidProcess_ShouldReturnNull()
    {
        var probe = new IntegrityLevelProbe();

        Assert.Null(probe.GetProcessIntegrityLevel(-1));
    }

    /// <summary>
    /// 无句柄、无名称、无自动化标识的元素不可定位。
    /// </summary>
    [Fact]
    public void UiElementInfo_IsAddressable_WithoutAnyIdentifier_ShouldBeFalse()
    {
        var element = new UiElementInfo(
            IntPtr.Zero,
            string.Empty,
            string.Empty,
            "Custom",
            "MyClass",
            CaptureBounds.Empty,
            IsEnabled: true,
            IsOffscreen: false,
            ProcessId: 100,
            Depth: 0);

        Assert.False(element.IsAddressable);
    }

    /// <summary>
    /// 三种定位依据任一存在即视为可定位。
    /// </summary>
    [Fact]
    public void UiElementInfo_IsAddressable_WithAnyIdentifier_ShouldBeTrue()
    {
        var byHandle = new UiElementInfo(
            new IntPtr(0x1000), "名称", string.Empty, "Button", "Button",
            CaptureBounds.Empty, true, false, 100, 1);
        var byName = new UiElementInfo(
            IntPtr.Zero, "确定", string.Empty, "Button", "Button",
            CaptureBounds.Empty, true, false, 100, 1);
        var byAutomationId = new UiElementInfo(
            IntPtr.Zero, string.Empty, "btnOk", "Button", "Button",
            CaptureBounds.Empty, true, false, 100, 1);

        Assert.True(byHandle.IsAddressable);
        Assert.True(byName.IsAddressable);
        Assert.True(byAutomationId.IsAddressable);
    }

    /// <summary>
    /// 显示文本必须有兜底，不得为空串。
    /// </summary>
    [Fact]
    public void UiElementInfo_Display_ShouldNeverBeEmpty()
    {
        var withName = new UiElementInfo(
            new IntPtr(0x1000), "确定", "btnOk", "Button", "Button",
            CaptureBounds.Empty, true, false, 100, 1);
        var withAutomationIdOnly = new UiElementInfo(
            IntPtr.Zero, string.Empty, "btnOk", "Button", "Button",
            CaptureBounds.Empty, true, false, 100, 1);
        var bare = new UiElementInfo(
            IntPtr.Zero, string.Empty, string.Empty, "Custom", "X",
            CaptureBounds.Empty, true, false, 100, 1);

        Assert.Equal("确定", withName.Display);
        Assert.Equal("[Button] btnOk", withAutomationIdOnly.Display);
        Assert.False(string.IsNullOrWhiteSpace(bare.Display));
    }

    /// <summary>
    /// 文本写入候选类型应覆盖 Chromium 输入框与富文本控件。
    /// </summary>
    [Fact]
    public void UiElementInfo_IsTextEntryCandidate_ShouldCoverKnownTypes()
    {
        Assert.True(Create("Edit").IsTextEntryCandidate);
        Assert.True(Create("Document").IsTextEntryCandidate);
        Assert.True(Create("ComboBox").IsTextEntryCandidate);
        Assert.False(Create("Button").IsTextEntryCandidate);
        Assert.False(Create("Image").IsTextEntryCandidate);
    }

    /// <summary>
    /// 点击候选类型应覆盖按钮与常用可点击元素。
    /// </summary>
    [Fact]
    public void UiElementInfo_IsClickCandidate_ShouldCoverKnownTypes()
    {
        Assert.True(Create("Button").IsClickCandidate);
        Assert.True(Create("CheckBox").IsClickCandidate);
        Assert.True(Create("Hyperlink").IsClickCandidate);
        Assert.False(Create("Edit").IsClickCandidate);
        Assert.False(Create("Text").IsClickCandidate);
    }

    /// <summary>
    /// 默认阈值为 0.80，低于该值不执行。
    /// </summary>
    [Fact]
    public void MatchOptions_DefaultThreshold_ShouldBe080()
    {
        var options = new MatchOptions();

        Assert.Equal(0.80, options.ScoreThreshold, precision: 5);
        Assert.True(options.ScoreThreshold <= 0.85, "默认阈值应偏保守以避免误点。");
    }

    /// <summary>
    /// 无障碍树疑似未就绪的判定：查到元素但总数异常偏少。
    /// </summary>
    /// <remarks>
    /// 实测 Chromium 未激活时仅 13 个元素且无 Edit，激活后为数百个。
    /// 该判定用于提示用户先让窗口获得焦点。
    /// </remarks>
    [Fact]
    public void UiQueryResult_LooksLikeUninitializedTree_ShouldFollowScanCount()
    {
        var thin = new UiQueryResult { Success = true, Elements = Array.Empty<UiElementInfo>(), TotalScanned = 13 };
        var rich = new UiQueryResult { Success = true, Elements = Array.Empty<UiElementInfo>(), TotalScanned = 317 };
        var failed = new UiQueryResult { Success = false, TotalScanned = 13 };

        Assert.True(thin.LooksLikeUninitializedTree);
        Assert.False(rich.LooksLikeUninitializedTree);
        Assert.False(failed.LooksLikeUninitializedTree);
    }

    /// <summary>
    /// 写入与点击结果必须能回显实际采用的机制。
    /// </summary>
    [Fact]
    public void Outcomes_ShouldCarryMechanismAndMessage()
    {
        var write = new TextEntryOutcome
        {
            Success = true,
            Mechanism = TextEntryMechanism.UiaValuePattern,
            Message = "已写入",
            Elapsed = TimeSpan.FromMilliseconds(12)
        };

        var click = new ClickOutcome
        {
            Success = true,
            Mechanism = ClickMechanism.TemplateMatchClick,
            CaptureSource = CaptureSourceKind.PrintWindowRenderFullContent,
            Message = "已点击"
        };

        Assert.Equal(TextEntryMechanism.UiaValuePattern, write.Mechanism);
        Assert.Equal(ClickMechanism.TemplateMatchClick, click.Mechanism);
        Assert.Equal(CaptureSourceKind.PrintWindowRenderFullContent, click.CaptureSource);
    }

    private static UiElementInfo Create(string controlType) =>
        new(
            new IntPtr(0x1000),
            "元素",
            "id",
            controlType,
            "Class",
            CaptureBounds.Empty,
            IsEnabled: true,
            IsOffscreen: false,
            ProcessId: 100,
            Depth: 1);
}
