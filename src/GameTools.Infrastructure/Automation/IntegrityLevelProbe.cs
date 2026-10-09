using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GameTools.Core.Abstractions;

namespace GameTools.Infrastructure.Automation;

/// <summary>
/// 通过 <c>TokenIntegrityLevel</c> 探测进程完整性级别。
/// </summary>
/// <remarks>
/// <para>
/// UIPI 会拦截低完整性级别进程向更高完整性级别进程发送窗口消息与输入，
/// 表现是操作神秘失败而调用本身返回成功。提前比对双方级别，
/// 可以把这种失败转化为明确提示。
/// </para>
/// <para>
/// 探测路径为 <c>OpenProcessToken</c> → <c>GetTokenInformation</c>，
/// 均为标准 Win32 API，net48 与 net8.0-windows 行为一致。
/// </para>
/// </remarks>
public sealed class IntegrityLevelProbe : IIntegrityLevelProbe
{
    private const int TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    /// <summary>
    /// SID 结构中各字段的字节偏移。
    /// </summary>
    /// <remarks>
    /// 布局为 Revision(1) + SubAuthorityCount(1) + IdentifierAuthority(6)
    /// + SubAuthority[](每项 4 字节)，因此第一个子权限位于偏移 8。
    /// </remarks>
    private const int SidSubAuthorityOffset = 8;

    /// <inheritdoc />
    public IntegrityLevel? GetProcessIntegrityLevel(int processId = 0)
    {
        IntPtr token = IntPtr.Zero;
        IntPtr tokenInformation = IntPtr.Zero;

        try
        {
            if (!OpenProcessToken(GetProcessHandle(processId), TokenQuery, out token) || token == IntPtr.Zero)
            {
                return null;
            }

            // 首次调用必然返回 false 并以 ERROR_INSUFFICIENT_BUFFER(122) 回填所需长度，
            // 这是查询式调用的正常约定，因此只依据 needed 判断，不能把返回值 false 当作失败。
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out int needed);

            if (needed <= 0)
            {
                return null;
            }

            tokenInformation = Marshal.AllocHGlobal(needed);

            if (!GetTokenInformation(token, TokenIntegrityLevel, tokenInformation, needed, out _))
            {
                return null;
            }

            // TOKEN_MANDATORY_LABEL 首成员即为指向完整性级别 SID 的指针
            IntPtr sid = Marshal.ReadIntPtr(tokenInformation);
            return MapRid(ReadLastSubAuthority(sid));
        }
        catch (Exception)
        {
            // 探测失败不得影响主流程，交由调用方按「未知」处理
            return null;
        }
        finally
        {
            if (tokenInformation != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(tokenInformation);
            }

            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }

    /// <inheritdoc />
    public string? CheckElevationRisk(int targetProcessId)
    {
        IntegrityLevel? self = GetProcessIntegrityLevel(0);
        IntegrityLevel? target = GetProcessIntegrityLevel(targetProcessId);

        if (self == null || target == null)
        {
            // 无法探测时不阻断操作，仅不给出风险提示
            return null;
        }

        if (self.Value >= target.Value)
        {
            return null;
        }

        return $"目标进程完整性级别为 {Describe(target.Value)}，高于当前进程的 {Describe(self.Value)}。" +
               "Windows 的用户界面特权隔离（UIPI）会拦截跨级别的写入与调用，" +
               "请以管理员身份重新运行本工具后再试。";
    }

    private static IntPtr GetProcessHandle(int processId)
    {
        if (processId == 0)
        {
            return GetCurrentProcess();
        }

        try
        {
            using Process? process = Process.GetProcessById(processId);
            return process.Handle;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// 读取 SID 的最后一个子权限值（RID）。
    /// </summary>
    /// <remarks>
    /// 完整性级别 SID 形如 <c>S-1-16-&lt;RID&gt;</c>，RID 即最后一个子权限值。
    /// 直接按 SID 结构布局读取，可避开 <c>ConvertSidToStringSid</c> 的编组差异：
    /// 后者要求按 UTF-16 分配缓冲区，且其长度函数导出名为
    /// <c>ConvertSidToStringSidLength</c>，在双目标下容易踩空。
    /// </remarks>
    private static uint ReadLastSubAuthority(IntPtr sid)
    {
        if (sid == IntPtr.Zero)
        {
            return 0;
        }

        byte count = Marshal.ReadByte(sid, 1);
        if (count == 0)
        {
            return 0;
        }

        return unchecked((uint)Marshal.ReadInt32(sid, SidSubAuthorityOffset + (count - 1) * 4));
    }

    /// <summary>
    /// 将完整性级别 RID 映射为枚举值。
    /// </summary>
    /// <param name="rid">子权限值。</param>
    /// <returns>对应的完整性级别；未知值返回 <c>null</c>。</returns>
    private static IntegrityLevel? MapRid(uint rid) => rid switch
    {
        0x0000 => IntegrityLevel.Untrusted,
        0x1000 => IntegrityLevel.Low,
        0x2000 => IntegrityLevel.Medium,
        0x3000 => IntegrityLevel.High,
        0x4000 => IntegrityLevel.System,
        _ => null
    };

    private static string Describe(IntegrityLevel level) => level switch
    {
        IntegrityLevel.Untrusted => "未分级",
        IntegrityLevel.Low => "低",
        IntegrityLevel.Medium => "中",
        IntegrityLevel.High => "高（管理员）",
        IntegrityLevel.System => "系统",
        _ => level.ToString()
    };

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

    /// <remarks>
    /// 位于 <c>advapi32.dll</c> 而非 <c>kernel32.dll</c>；放在 kernel32 会导致
    /// 运行期抛 <c>EntryPointNotFoundException</c>，且异常被探测逻辑吞掉后表现为「探测不到」。
    /// </remarks>
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
