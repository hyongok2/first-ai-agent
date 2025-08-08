using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace McpAgent.Shared.Utils;

/// <summary>
/// Windows Job Object 유틸. 부모 종료 시 자식 프로세스 자동 종료.
/// - Initialize()로 Job 생성 및 KILL_ON_JOB_CLOSE 설정
/// - Assign(process)로 자식 프로세스 Job에 등록
/// - Dispose() 시 자식 전부 종료됨
/// </summary>
public sealed class ProcessJobManager : IDisposable
{
    private static readonly object _lock = new();
    private static ProcessJobManager? _instance;

    public static ProcessJobManager Instance
    {
        get
        {
            if (_instance is null)
                throw new InvalidOperationException("ProcessJobManager is not initialized. Call Initialize() first.");
            return _instance;
        }
    }

    public static void Initialize()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ProcessJobManager supports only Windows.");

        lock (_lock)
        {
            if (_instance != null) return;
            _instance = new ProcessJobManager();
        }
    }

    private IntPtr _job;
    private bool _disposed;

    private ProcessJobManager()
    {
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job == IntPtr.Zero)
            ThrowLastError("CreateJobObject failed");

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(info)))
            ThrowLastError("SetInformationJobObject failed");
    }

    /// <summary>
    /// 실행된 프로세스를 Job에 추가.
    /// </summary>
    public void Assign(Process process)
    {
        if (process is null) throw new ArgumentNullException(nameof(process));
        if (_disposed) throw new ObjectDisposedException(nameof(ProcessJobManager));

        // 프로세스는 이미 Start() 된 상태여야 함
        if (process.HasExited)
            throw new InvalidOperationException("Process already exited.");

        if (!AssignProcessToJobObject(_job, process.Handle))
            ThrowLastError("AssignProcessToJobObject failed");
    }

    private static void ThrowLastError(string message)
    {
        var error = Marshal.GetLastWin32Error();
        throw new Win32Exception(error, $"{message}. Win32Error={error}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_job != IntPtr.Zero)
        {
            // 이 순간 Job 핸들이 닫히며, KILL_ON_JOB_CLOSE 플래그로 모든 자식이 종료됩니다.
            CloseHandle(_job);
            _job = IntPtr.Zero;
        }
    }

    #region P/Invoke

    private const int JobObjectExtendedLimitInformation = 9;
    private const int JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int JobObjectInfoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo,
        int cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public int LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public int ActiveProcessLimit;
        public long Affinity;
        public int PriorityClass;
        public int SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    #endregion
}
