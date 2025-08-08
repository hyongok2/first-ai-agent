using System;
using System.Diagnostics;

namespace McpAgent.Shared.Utils;

/// <summary>
/// 크로스 플랫폼 프로세스 관리자 팩토리.
/// OS에 따라 적절한 구현체를 생성합니다.
/// </summary>
public static class ProcessJobManagerFactory
{
    /// <summary>
    /// 현재 OS에 적합한 IProcessJobManager 구현체를 생성합니다.
    /// </summary>
    public static IProcessJobManager Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsProcessJobManager();
        }

        if (OperatingSystem.IsLinux() ||
            OperatingSystem.IsMacOS() ||
            OperatingSystem.IsFreeBSD())
        {
            return new UnixProcessJobManager();
        }

        // 지원하지 않는 OS의 경우 NoOp 구현체 사용
        return new NoOpProcessJobManager();
    }
}

/// <summary>
/// 지원하지 않는 플랫폼을 위한 NoOp 구현체.
/// </summary>
internal sealed class NoOpProcessJobManager : IProcessJobManager
{
    public bool IsSupported => false;

    public void Assign(Process process)
    {
        // 아무것도 하지 않음 - 플랫폼이 지원되지 않음
    }

    public void Dispose()
    {
        // 아무것도 하지 않음
    }
}
