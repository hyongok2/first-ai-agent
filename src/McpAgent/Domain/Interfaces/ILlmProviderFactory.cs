using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

/// <summary>
/// 파이프라인별 LLM Provider를 생성하는 팩토리 인터페이스
/// </summary>
public interface ILlmProviderFactory
{
    /// <summary>
    /// 특정 파이프라인 타입에 맞는 LLM Provider를 생성합니다.
    /// </summary>
    /// <param name="pipelineType">파이프라인 타입</param>
    /// <returns>해당 파이프라인에 최적화된 LLM Provider</returns>
    ILlmProvider CreateForPipeline(PipelineType pipelineType);

    /// <summary>
    /// 기본 LLM Provider를 생성합니다.
    /// </summary>
    /// <returns>기본 LLM Provider</returns>
    ILlmProvider CreateDefault();

    /// <summary>
    /// 특정 설정으로 LLM Provider를 생성합니다.
    /// </summary>
    /// <param name="settings">LLM 설정</param>
    /// <returns>설정에 맞는 LLM Provider</returns>
    ILlmProvider CreateWithSettings(PipelineLlmSettings settings);
}