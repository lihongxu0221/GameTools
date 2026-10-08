using System.Collections.Concurrent;
using System.Diagnostics;

namespace GameTools.Win32.Helpers;

/// <summary>
/// 进程名缓存。
/// </summary>
/// <remarks>
/// 低级钩子与窗口事件在高频场景下会为每个事件查询所属进程名，
/// 而 <see cref="Process.GetProcessById(int)"/> 每次调用都会打开进程对象并读取路径，
/// 属于重量级操作且在目标进程退出时会抛出异常。
/// 本缓存按 PID 保存结果，使同一进程在存活期间的重复查询退化为字典查找。
/// </remarks>
public static class ProcessInfoCache
{
    /// <summary>无法获取进程名时的占位值。</summary>
    public const string UnknownProcessName = "Unknown";

    /// <summary>系统空闲进程的 PID。</summary>
    private const int SystemIdleProcessId = 0;

    private const int MaxCachedEntries = 1024;

    private static readonly ConcurrentDictionary<int, string> _cache = new();

    /// <summary>
    /// 获取进程名；查询失败时返回 <see cref="UnknownProcessName"/>，不向上抛出异常。
    /// </summary>
    /// <param name="processId">目标进程 ID。</param>
    public static string TryGetProcessName(int processId)
    {
        if (processId == SystemIdleProcessId)
        {
            return "Idle";
        }

        if (processId < 0)
        {
            return UnknownProcessName;
        }

        if (_cache.TryGetValue(processId, out string? cached) && cached is not null)
        {
            return cached;
        }

        string name = QueryProcessName(processId) ?? UnknownProcessName;

        // 缓存容量上限：防止长时间运行后由大量短生命周期进程撑爆内存
        if (_cache.Count >= MaxCachedEntries)
        {
            _cache.Clear();
        }

        _cache[processId] = name;
        return name;
    }

    /// <summary>
    /// 清空缓存。目标进程重启后 PID 可能被复用，此时需要主动清除以避免返回过期名称。
    /// </summary>
    public static void Clear() => _cache.Clear();

    private static string? QueryProcessName(int processId)
    {
        try
        {
            using Process proc = Process.GetProcessById(processId);
            return proc.ProcessName;
        }
        catch (ArgumentException)
        {
            // 进程已退出或 PID 不存在
            return UnknownProcessName;
        }
        catch (InvalidOperationException)
        {
            return UnknownProcessName;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 无权访问目标进程
            return UnknownProcessName;
        }
    }
}
