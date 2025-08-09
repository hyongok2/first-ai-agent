using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace McpAgent.Shared.Utils;

/// <summary>
/// Unix/Linux/macOS용 프로세스 관리 구현.
/// Process Group과 시그널을 사용하여 부모 종료 시 자식 프로세스를 종료시킵니다.
/// </summary>
internal sealed class UnixProcessJobManager : IProcessJobManager
{
    private readonly List<Process> _managedProcesses = new();
    private readonly object _lock = new();
    private bool _disposed;

    public bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ||
                               OperatingSystem.IsFreeBSD();

    public UnixProcessJobManager()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Unix process management is only supported on Unix-like systems.");

        // 부모 프로세스 종료 시 자식들도 종료되도록 설정
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    public void Assign(Process process)
    {
        if (process is null) throw new ArgumentNullException(nameof(process));
        if (_disposed) throw new ObjectDisposedException(nameof(UnixProcessJobManager));

        if (process.HasExited)
            throw new InvalidOperationException("Process already exited.");

        lock (_lock)
        {
            _managedProcesses.Add(process);

            // Unix 시스템에서는 프로세스 그룹을 설정하여 관리
            // 대부분의 경우 자식 프로세스는 부모와 같은 프로세스 그룹에 속하므로
            // 부모 종료 시 SIGHUP 시그널을 받게 됨
            try
            {
                // PR_SET_PDEATHSIG를 사용하여 부모 종료 시 시그널 전송 (Linux only)
                if (OperatingSystem.IsLinux())
                {
                    SetParentDeathSignal(process.Id);
                }
            }
            catch
            {
                // 시그널 설정 실패 시에도 프로세스 추적은 계속
            }
        }
    }

    private void SetParentDeathSignal(int pid)
    {
        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            // Linux에서 PR_SET_PDEATHSIG 시스템 콜 사용
            // SIGTERM(15)을 부모 죽음 시그널로 설정
            const int PR_SET_PDEATHSIG = 1;
            const int SIGTERM = 15;

            // 자식 프로세스에서 prctl 호출되어야 하므로,
            // 여기서는 대안적 방법을 사용
            // 실제로는 fork 후 exec 전에 설정해야 함

            // .NET Process로는 직접 설정 불가
            // 따라서 현재 이벤트 기반 방식을 유지
        }
        catch
        {
            // prctl 설정 실패 시 이벤트 방식으로 대체
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        TerminateAllProcesses();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        TerminateAllProcesses();
    }

    private void TerminateAllProcesses()
    {
        lock (_lock)
        {
            foreach (var process in _managedProcesses)
            {
                TerminateProcess(process);
            }
            _managedProcesses.Clear();
        }
    }

    private void TerminateProcess(Process process)
    {
        if (process is null) return;

        try
        {
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);// Unix 시스템에서는 Kill() 메서드가 SIGTERM을 보냄
            if (process.WaitForExit(5000)) return;
            process.Kill();// 짧은 시간 대기 후 강제 종료
        }
        catch
        {
            // 프로세스가 이미 종료되었거나 접근 권한이 없는 경우 무시
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        Console.CancelKeyPress -= OnCancelKeyPress;

        TerminateAllProcesses();
    }
}