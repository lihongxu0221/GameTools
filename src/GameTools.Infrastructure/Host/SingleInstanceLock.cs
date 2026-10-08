namespace GameTools.Infrastructure.Host;

/// <summary>
/// 基于系统级命名互斥体的单实例进程锁
/// </summary>
public sealed class SingleInstanceLock : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _hasHandle;
    private bool _disposed;

    public bool IsOnlyInstance => _hasHandle;

    public SingleInstanceLock(string appIdentifier)
    {
        string mutexName = $"Local\\GameTools_App_{appIdentifier}";
        try
        {
            _mutex = new Mutex(true, mutexName, out _hasHandle);
        }
        catch (AbandonedMutexException)
        {
            _hasHandle = true;
            _mutex = new Mutex(false, mutexName);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hasHandle)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // 忽略释放异常
            }
        }
        _mutex.Dispose();
    }
}
