using System;
using System.Diagnostics;

namespace McpAgent.Shared.Utils;

/// <summary>
/// 프로세스 관리 인터페이스. 부모 종료 시 자식 프로세스를 자동으로 종료시킵니다.
/// </summary>
public interface IProcessJobManager : IDisposable
{
    /// <summary>
    /// 프로세스를 관리 대상에 추가합니다.
    /// </summary>
    /// <param name="process">관리할 프로세스</param>
    void Assign(Process process);
    
    /// <summary>
    /// 현재 플랫폼에서 지원되는지 여부를 반환합니다.
    /// </summary>
    bool IsSupported { get; }
}