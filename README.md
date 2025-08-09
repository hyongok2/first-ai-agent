# 🤖 McpAgent - 지능형 AI 에이전트 시스템

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![MCP Protocol](https://img.shields.io/badge/MCP-Protocol-blue)](https://modelcontextprotocol.io/)

## 📌 프로젝트 소개

McpAgent는 로컬 LLM(Large Language Model)과 MCP(Model Context Protocol)를 통합한 차세대 AI 에이전트 시스템입니다. Clean Architecture 원칙에 따라 설계되었으며, 도메인 주도 설계(DDD) 패턴을 적용하여 확장성과 유지보수성을 극대화했습니다.

### 🎯 핵심 특징

- **🧠 지능형 처리 파이프라인**: 입력 정제 → 능력 선택 → 파라미터 생성 → 도구 실행 → 응답 생성의 5단계 처리
- **🔌 MCP 프로토콜 지원**: 표준화된 도구 통합으로 무한한 확장 가능성
- **💾 대화 메모리 관리**: 컨텍스트 보존 및 대화 요약 기능
- **🏗️ Clean Architecture**: 계층별 책임 분리로 테스트 용이성과 유지보수성 확보
- **⚙️ 유연한 설정 시스템**: JSON, 환경변수, CLI 옵션을 통한 다단계 설정

## 🏛️ 시스템 아키텍처

```
┌─────────────────────────────────────────────────────┐
│                 Presentation Layer                   │
│         ConsoleUI / AgentHostService                 │
├─────────────────────────────────────────────────────┤
│                 Application Layer                    │
│    AgentService / ConversationService / Commands    │
├─────────────────────────────────────────────────────┤
│                   Domain Layer                       │
│   Entities / Domain Services / Orchestrator          │
├─────────────────────────────────────────────────────┤
│                Infrastructure Layer                  │
│   LLM Providers / MCP Clients / Storage / Logging   │
└─────────────────────────────────────────────────────┘
```

### 📂 프로젝트 구조

```
src/McpAgent/
├── Application/          # 애플리케이션 서비스 및 유스케이스
│   ├── Agent/           # 에이전트 핵심 서비스
│   ├── Commands/        # 명령 처리 서비스
│   ├── Conversation/    # 대화 관리 서비스
│   └── Services/        # 각 처리 단계별 서비스
│       ├── InputRefinementService.cs
│       ├── CapabilitySelectionService.cs
│       ├── ParameterGenerationService.cs
│       └── ResponseGenerationService.cs
├── Domain/              # 도메인 모델 및 비즈니스 로직
│   ├── Entities/        # 도메인 엔티티
│   ├── Interfaces/      # 도메인 인터페이스
│   └── Services/        # 도메인 서비스 (오케스트레이터)
├── Infrastructure/      # 외부 시스템 통합
│   ├── LLM/            # LLM 프로바이더 (Ollama)
│   ├── MCP/            # MCP 클라이언트 구현
│   ├── Storage/        # 데이터 저장소
│   └── Logging/        # 로깅 시스템
└── Presentation/        # 사용자 인터페이스
    ├── Console/        # 콘솔 UI
    └── Hosting/        # 호스트 서비스
```

## 🚀 빠른 시작

### 필수 요구사항

- **.NET 8.0 SDK** ([다운로드](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Ollama** ([설치 가이드](https://ollama.ai/))
- **Node.js 18+** (MCP 서버용, 선택사항)

### 설치 및 실행

```bash
# 1. 프로젝트 클론
git clone https://github.com/yourusername/first-ai-agent.git
cd first-ai-agent

# 2. Ollama 설치 및 모델 다운로드
ollama pull qwen3-32b  # 또는 llama3, mistral 등

# 3. 빌드
dotnet build

# 4. 실행
cd src/McpAgent
dotnet run

# CLI 옵션과 함께 실행
dotnet run -- --model qwen3-32b --temperature 0.7
```

## ⚙️ 설정

### appsettings.json 예시

```json
{
  "Agent": {
    "Llm": {
      "Provider": "ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "qwen3-32b",
      "MaxTokens": 4096,
      "Temperature": 0.7
    },
    "Mcp": {
      "Enabled": true,
      "Servers": [
        {
          "Name": "mcp-server-framework",
          "Command": "uv",
          "Args": ["run", "mcp-agent"],
          "Env": {
            "PYTHONPATH": "C:/src/work/mcp/mcp-server-framework"
          }
        }
      ]
    },
    "Agent": {
      "MaxHistoryLength": 50,
      "MaxToolChainIterations": 5,
      "EnableSummary": true
    }
  }
}
```

### 환경 변수 설정

```bash
# Windows PowerShell
$env:MCPAGENT_Agent__Llm__Model = "llama3"
$env:MCPAGENT_Agent__Llm__Temperature = "0.5"

# Linux/macOS
export MCPAGENT_Agent__Llm__Model=llama3
export MCPAGENT_Agent__Llm__Temperature=0.5
```

## 🔄 처리 파이프라인

### 5단계 지능형 처리 과정

1. **입력 정제 (Input Refinement)**
   - 사용자 입력 분석 및 명확화
   - 의도 파악 및 컨텍스트 이해

2. **능력 선택 (Capability Selection)**
   - 사용 가능한 도구 식별
   - 최적 도구 조합 선택

3. **파라미터 생성 (Parameter Generation)**
   - 선택된 도구의 파라미터 생성
   - 유효성 검증

4. **도구 실행 (Tool Execution)**
   - MCP 서버를 통한 도구 호출
   - 결과 수집 및 오류 처리

5. **응답 생성 (Response Generation)**
   - 도구 결과 통합
   - 자연어 응답 생성

## 🛠️ MCP 서버 통합

### 지원되는 MCP 서버

- **파일시스템 서버**: 파일 읽기/쓰기, 디렉토리 탐색
- **웹 검색 서버**: 인터넷 검색 및 정보 수집
- **데이터베이스 서버**: SQL 쿼리 실행
- **커스텀 서버**: 사용자 정의 도구 통합

### MCP 서버 추가 방법

```json
{
  "Mcp": {
    "Servers": [
      {
        "Name": "my-custom-server",
        "Command": "python",
        "Args": ["my_mcp_server.py"],
        "Env": {
          "API_KEY": "your-api-key"
        }
      }
    ]
  }
}
```

## 📊 로깅 및 모니터링

### 로깅 시스템

- **시스템 로그**: `Logs/system-{date}.log`
- **요청/응답 로그**: `Logs/RequestResponse/{date}/`
- **처리 단계별 상세 로그**: 각 파이프라인 단계별 로깅

### 로그 레벨 설정

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "McpAgent": "Debug",
      "Microsoft": "Warning"
    }
  }
}
```

## 🧪 테스트 및 디버깅

```bash
# 단위 테스트 실행
dotnet test

# 특정 테스트 실행
dotnet test --filter "FullyQualifiedName~AgentService"

# 디버그 모드 실행
dotnet run --configuration Debug
```

## 🔌 확장 가이드

### 새로운 LLM 프로바이더 추가

1. `ILlmProvider` 인터페이스 구현
2. DI 컨테이너에 등록
3. 설정 모델 업데이트

### 커스텀 처리 서비스 추가

1. 도메인 인터페이스 정의
2. Application 레이어에 서비스 구현
3. 오케스트레이터에 통합

## 📈 성능 최적화

- **토큰 관리**: TokenCounter를 통한 효율적인 토큰 사용
- **대화 요약**: 긴 대화 자동 요약으로 컨텍스트 최적화
- **캐싱**: 반복 요청에 대한 응답 캐싱
- **병렬 처리**: 독립적인 도구 호출 병렬화

## 🤝 기여하기

프로젝트 개선에 참여하고 싶으시다면:

1. 이슈 생성으로 아이디어 공유
2. Pull Request 제출
3. 문서 개선
4. 버그 리포트

## 📚 문서

- [펀더멘탈 가이드](docs/01-fundamental/README.md) - AI 에이전트 핵심 개념
- [시작하기 가이드](docs/02-getting-started/README.md) - 상세 설치 및 설정
- [API 문서](docs/api/README.md) - 개발자 API 레퍼런스
- [아키텍처 문서](docs/architecture/README.md) - 시스템 설계 상세

## 🐛 문제 해결

### 일반적인 문제

1. **Ollama 연결 실패**
   - Ollama 서비스 실행 확인: `ollama serve`
   - 모델 설치 확인: `ollama list`

2. **MCP 서버 시작 실패**
   - Node.js 버전 확인: `node --version`
   - Python 환경 확인 (Python 기반 서버의 경우)

3. **메모리 부족**
   - 더 작은 모델 사용
   - MaxTokens 값 감소

## 📝 라이선스

이 프로젝트는 [MIT 라이선스](LICENSE)에 따라 배포됩니다.

## 🙏 감사의 말

- [Ollama](https://ollama.ai/) - 로컬 LLM 실행 환경
- [Model Context Protocol](https://modelcontextprotocol.io/) - 도구 통합 프로토콜
- [Semantic Kernel](https://github.com/microsoft/semantic-kernel) - LLM 오케스트레이션

---

**Made with ❤️ by AI Agent Team**

*질문이나 제안사항이 있으시면 [이슈](https://github.com/yourusername/first-ai-agent/issues)를 생성해주세요!*