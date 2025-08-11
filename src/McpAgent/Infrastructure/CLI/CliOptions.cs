namespace McpAgent.Infrastructure.CLI;

/// <summary>
/// CLI 명령줄 옵션을 처리하는 클래스
/// </summary>
public static class CliOptions
{
    /// <summary>
    /// CLI 인수를 파싱하여 환경 변수로 설정
    /// </summary>
    /// <param name="args">CLI 인수</param>
    public static void ParseAndSetEnvironmentVariables(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--model":
                case "-m":
                    if (i + 1 < args.Length)
                    {
                        var model = args[++i];
                        Environment.SetEnvironmentVariable("MCPAGENT_Agent__PipelineLlm__DefaultSettings__Model", model);
                        Console.WriteLine($"🤖 기본 모델을 '{model}'로 설정했습니다.");
                    }
                    break;

                case "--integrated-model":
                case "--im":
                    if (i + 1 < args.Length)
                    {
                        var model = args[++i];
                        Environment.SetEnvironmentVariable("MCPAGENT_Agent__PipelineLlm__PipelineSettings__IntegratedAnalysis__Model", model);
                        Console.WriteLine($"🔄 통합 분석 모델을 '{model}'로 설정했습니다.");
                    }
                    break;

                case "--response-model":
                case "--rm":
                    if (i + 1 < args.Length)
                    {
                        var model = args[++i];
                        Environment.SetEnvironmentVariable("MCPAGENT_Agent__PipelineLlm__PipelineSettings__ResponseGeneration__Model", model);
                        Console.WriteLine($"💬 응답 생성 모델을 '{model}'로 설정했습니다.");
                    }
                    break;

                case "--parameter-model":
                case "--pm":
                    if (i + 1 < args.Length)
                    {
                        var model = args[++i];
                        Environment.SetEnvironmentVariable("MCPAGENT_Agent__PipelineLlm__PipelineSettings__ParameterGeneration__Model", model);
                        Console.WriteLine($"⚙️ 파라미터 생성 모델을 '{model}'로 설정했습니다.");
                    }
                    break;

                case "--temperature":
                case "-t":
                    if (i + 1 < args.Length && double.TryParse(args[++i], out double temperature))
                    {
                        Environment.SetEnvironmentVariable("MCPAGENT_Agent__PipelineLlm__DefaultSettings__Temperature", temperature.ToString());
                        Console.WriteLine($"🌡️ 기본 온도를 {temperature}로 설정했습니다.");
                    }
                    break;

                case "--endpoint":
                case "-e":
                    if (i + 1 < args.Length)
                    {
                        var endpoint = args[++i];
                        Environment.SetEnvironmentVariable("MCPAGENT_Agent__PipelineLlm__DefaultSettings__Endpoint", endpoint);
                        Console.WriteLine($"🌐 Endpoint를 '{endpoint}'로 설정했습니다.");
                    }
                    break;

                case "--legacy-pipeline":
                case "-l":
                    Environment.SetEnvironmentVariable("MCPAGENT_Agent__Agent__UseOptimizedPipeline", "false");
                    Console.WriteLine("🔄 기존 파이프라인을 사용합니다.");
                    break;

                case "--optimized-pipeline":
                case "-o":
                    Environment.SetEnvironmentVariable("MCPAGENT_Agent__Agent__UseOptimizedPipeline", "true");
                    Console.WriteLine("⚡ 최적화 파이프라인을 사용합니다.");
                    break;

                case "--help":
                case "-h":
                    ShowHelp();
                    Environment.Exit(0);
                    break;

                case "--version":
                case "-v":
                    ShowVersion();
                    Environment.Exit(0);
                    break;
            }
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine(@"
McpAgent - AI 에이전트 시스템

사용법: dotnet run [옵션]

모델 설정:
  --model, -m <model>           기본 모델 설정
  --integrated-model, --im <model>  통합 분석용 모델 설정
  --response-model, --rm <model>    응답 생성용 모델 설정
  --parameter-model, --pm <model>   파라미터 생성용 모델 설정

설정 옵션:
  --temperature, -t <value>     온도 설정 (0.0-2.0)
  --endpoint, -e <url>          LLM 엔드포인트 URL

파이프라인 옵션:
  --legacy-pipeline, -l         기존 파이프라인 사용
  --optimized-pipeline, -o      최적화 파이프라인 사용 (기본값)

기타:
  --help, -h                    이 도움말 표시
  --version, -v                 버전 정보 표시

예제:
  dotnet run --model qwen3:32b --temperature 0.7
  dotnet run --integrated-model gpt-oss:120b --response-model llama3.1:8b
  dotnet run --legacy-pipeline
");
    }

    private static void ShowVersion()
    {
        Console.WriteLine("McpAgent v1.0.0 - AI 에이전트 시스템");
        Console.WriteLine("https://github.com/your-repo/first-ai-agent");
    }

    /// <summary>
    /// 사용 가능한 모델 목록 표시 (Ollama 기준)
    /// </summary>
    public static void ShowAvailableModels()
    {
        Console.WriteLine(@"
🤖 추천 모델:

소형 모델 (빠른 응답):
  - llama3.1:8b         - 일반적인 대화와 간단한 작업
  - qwen2.5:7b          - 다국어 지원, 빠른 처리
  - gemma2:9b           - Google의 효율적인 모델

대형 모델 (정확한 분석):
  - qwen3:32b           - 복잡한 분석과 추론
  - gpt-oss:120b        - 최고 성능의 오픈소스 모델
  - llama3.1:70b        - 고급 추론 능력

특화 모델:
  - qwen2.5-coder:7b    - 코딩 작업 특화
  - deepseek-coder:6.7b - 프로그래밍 전용
");
    }
}