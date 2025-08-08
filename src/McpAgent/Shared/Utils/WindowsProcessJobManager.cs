using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace McpAgent.Shared.Utils;

/// <summary>
/// Windows Job Object를 사용한 프로세스 관리 구현.
/// 부모 종료 시 자식 프로세스를 자동으로 종료시킵니다.
/// </summary>
internal sealed class WindowsProcessJobManager : IProcessJobManager
{
    private IntPtr _job;
    private bool _disposed;

    public bool IsSupported => OperatingSystem.IsWindows();

    public WindowsProcessJobManager()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Windows Job Objects are only supported on Windows.");

        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job == IntPtr.Zero)
            ThrowLastError("CreateJobObject failed");

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(info)))
            ThrowLastError("SetInformationJobObject failed");
    }

    public void Assign(Process process)
    {
        if (process is null) throw new ArgumentNullException(nameof(process));
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsProcessJobManager));

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