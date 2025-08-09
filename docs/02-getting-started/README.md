# McpAgent 시작하기

## 🚀 빠른 시작 가이드

이 가이드는 McpAgent를 처음 사용하는 분들을 위한 단계별 설치 및 실행 안내서입니다.

## 📋 사전 준비사항

### 필수 소프트웨어

1. **.NET 8.0 SDK**
   - [다운로드 링크](https://dotnet.microsoft.com/download/dotnet/8.0)
   - 설치 확인: `dotnet --version`

2. **Ollama** (로컬 LLM 실행)
   - [공식 웹사이트](https://ollama.ai/)
   - Windows/Mac/Linux 지원

3. **Node.js** (MCP 서버용, 선택사항)
   - [다운로드 링크](https://nodejs.org/)
   - 버전 18.0 이상 권장

4. **Git** (소스 코드 다운로드)
   - [다운로드 링크](https://git-scm.com/)

## 🛠️ 설치 과정

### 1단계: 프로젝트 클론

```bash
# 저장소 클론
git clone https://github.com/yourusername/first-ai-agent.git
cd first-ai-agent
```

### 2단계: Ollama 설치 및 설정

#### Windows
```powershell
# Ollama 다운로드 및 설치 (공식 웹사이트에서 설치 파일 다운로드)
# 설치 후 PowerShell에서 실행
ollama serve
```

#### macOS/Linux
```bash
# Ollama 설치
curl -fsSL https://ollama.ai/install.sh | sh

# Ollama 서비스 시작
ollama serve
```

#### 모델 다운로드
```bash
# 추천 모델들
ollama pull llama3        # 가장 균형잡힌 성능
ollama pull mistral       # 빠른 응답
ollama pull codellama     # 코드 특화

# 설치된 모델 확인
ollama list
```

### 3단계: MCP 서버 설치 (선택사항)

```bash
# 파일시스템 MCP 서버
npm install -g @modelcontextprotocol/server-filesystem

# 웹 검색 MCP 서버
npm install -g @modelcontextprotocol/server-web-search

# SQLite MCP 서버
npm install -g @modelcontextprotocol/server-sqlite
```

### 4단계: 프로젝트 빌드

```bash
# 프로젝트 루트에서
dotnet restore
dotnet build

# 또는 솔루션 파일 직접 빌드
dotnet build first-ai-agent.sln
```

## ⚙️ 설정하기

### 기본 설정 파일

`src/McpAgent/appsettings.json` 파일을 편집합니다:

```json
{
  "Agent": {
    "Llm": {
      "Provider": "ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "llama3",
      "MaxTokens": 4096,
      "Temperature": 0.7,
      "Stream": true
    },
    "Mcp": {
      "Enabled": true,
      "Servers": [
        {
          "Name": "filesystem",
          "Command": "npx",
          "Args": [
            "@modelcontextprotocol/server-filesystem",
            "."
          ],
          "Env": {}
        }
      ]
    },
    "Agent": {
      "Name": "MyAgent",
      "SystemPrompt": "당신은 도구를 사용할 수 있는 유능한 AI 어시스턴트입니다.",
      "MaxHistoryLength": 50,
      "MaxToolChainIterations": 5,
      "PromptStyle": "react"
    }
  }
}
```

### 설정 옵션 설명

#### LLM 설정
| 옵션 | 설명 | 기본값 |
|------|------|--------|
| Provider | LLM 프로바이더 타입 | "ollama" |
| Endpoint | LLM 서버 주소 | "http://localhost:11434" |
| Model | 사용할 모델 이름 | "llama3" |
| MaxTokens | 최대 응답 토큰 수 | 4096 |
| Temperature | 창의성 (0.0-1.0) | 0.7 |
| Stream | 스트리밍 응답 사용 | true |

#### MCP 서버 설정
| 옵션 | 설명 | 예시 |
|------|------|------|
| Name | 서버 식별자 | "filesystem" |
| Command | 실행 명령어 | "npx" |
| Args | 명령어 인자 | ["@modelcontextprotocol/server-filesystem", "."] |
| Env | 환경 변수 | {"API_KEY": "your-key"} |

#### 에이전트 설정
| 옵션 | 설명 | 기본값 |
|------|------|--------|
| Name | 에이전트 이름 | "McpAgent" |
| SystemPrompt | 시스템 프롬프트 | 기본 프롬프트 |
| MaxHistoryLength | 대화 기록 최대 길이 | 50 |
| MaxToolChainIterations | 도구 체인 최대 반복 | 5 |
| PromptStyle | 프롬프트 스타일 | "react" |

### 환경 변수로 설정 재정의

```bash
# Windows (PowerShell)
$env:MCPAGENT_Agent__Llm__Model = "mistral"
$env:MCPAGENT_Agent__Llm__Temperature = "0.5"

# Linux/macOS
export MCPAGENT_Agent__Llm__Model=mistral
export MCPAGENT_Agent__Llm__Temperature=0.5
```

## 🎮 실행하기

### 기본 실행

```bash
cd src/McpAgent
dotnet run
```

### CLI 옵션과 함께 실행

```bash
# 도움말 보기
dotnet run -- --help

# 특정 모델 사용
dotnet run -- --model codellama

# 온도 설정 변경
dotnet run -- --temperature 0.9

# 프롬프트 스타일 변경
dotnet run -- --prompt-style direct

# 여러 옵션 조합
dotnet run -- --model mistral --temperature 0.5 --prompt-style react
```

### 사용 가능한 CLI 옵션

| 옵션 | 설명 | 예시 |
|------|------|------|
| --model | LLM 모델 선택 | --model llama3 |
| --temperature | 응답 창의성 (0.0-1.0) | --temperature 0.7 |
| --prompt-style | 프롬프트 스타일 (direct/react) | --prompt-style react |
| --max-tokens | 최대 토큰 수 | --max-tokens 2048 |
| --endpoint | LLM 서버 주소 | --endpoint http://localhost:11434 |
| --help | 도움말 표시 | --help |

## 💬 사용 예제

### 기본 대화

```
McpAgent가 시작되었습니다. 'help'를 입력하여 명령어를 확인하세요.

You> 안녕하세요! 오늘 날씨가 어떤가요?

Assistant> 안녕하세요! 저는 실시간 날씨 정보에 직접 접근할 수는 없지만, 
날씨 정보를 확인하는 방법을 안내해드릴 수 있습니다. 현재 위치의 
날씨를 알고 싶으시다면 구체적인 지역을 말씀해 주시면 도움을 드리겠습니다.

You> 현재 디렉토리의 파일을 보여주세요

Assistant> [도구 사용: list_directory]
현재 디렉토리에 다음 파일들이 있습니다:
- Program.cs (2.5KB)
- appsettings.json (1.2KB) 
- McpAgent.csproj (856B)
- /Core (디렉토리)
- /Models (디렉토리)
- /Services (디렉토리)
```

### 도구 체인 사용

```
You> README.md 파일을 읽고 프로젝트에 대해 요약해주세요

Assistant> [도구 사용: read_file - README.md]
[도구 사용: analyze_text]

이 프로젝트는 McpAgent라는 AI 에이전트 시스템입니다. 주요 특징:
1. .NET 8로 구축된 로컬 LLM 통합 시스템
2. Ollama를 통한 프라이빗 AI 추론
3. MCP 프로토콜로 확장 가능한 도구 지원
4. 플러그인 아키텍처로 쉬운 기능 확장
```

## 🔧 문제 해결

### 일반적인 문제와 해결 방법

#### 1. Ollama 연결 실패

**증상**: "Failed to connect to Ollama" 오류

**해결 방법**:
```bash
# Ollama 실행 확인
ollama serve

# 다른 터미널에서 연결 테스트
curl http://localhost:11434/api/tags

# 모델 존재 확인
ollama list
```

#### 2. MCP 서버 시작 실패

**증상**: "MCP server failed to start" 오류

**해결 방법**:
```bash
# Node.js 설치 확인
node --version
npm --version

# MCP 서버 재설치
npm uninstall -g @modelcontextprotocol/server-filesystem
npm install -g @modelcontextprotocol/server-filesystem

# 권한 확인 (Linux/macOS)
sudo npm install -g @modelcontextprotocol/server-filesystem
```

#### 3. 빌드 오류

**증상**: 프로젝트 빌드 실패

**해결 방법**:
```bash
# .NET SDK 버전 확인
dotnet --list-sdks

# NuGet 패키지 캐시 정리
dotnet nuget locals all --clear

# 패키지 복원 및 재빌드
dotnet restore --force
dotnet clean
dotnet build
```

#### 4. 메모리 부족

**증상**: Out of memory 오류

**해결 방법**:
- 더 작은 모델 사용 (예: llama3 대신 mistral)
- MaxTokens 값 감소
- MaxHistoryLength 값 감소

## 📚 추가 리소스

### 공식 문서
- [.NET 8 문서](https://docs.microsoft.com/dotnet/)
- [Ollama 문서](https://github.com/ollama/ollama)
- [MCP 프로토콜 사양](https://modelcontextprotocol.io/)

### 커뮤니티
- [GitHub Issues](https://github.com/yourusername/first-ai-agent/issues)
- [Discord 서버](https://discord.gg/your-invite)
- [토론 포럼](https://github.com/yourusername/first-ai-agent/discussions)

### 튜토리얼
- [첫 번째 MCP 서버 만들기](./tutorials/first-mcp-server.md)
- [커스텀 LLM 프로바이더 추가](./tutorials/custom-llm-provider.md)
- [프롬프트 엔지니어링 가이드](./tutorials/prompt-engineering.md)

## 🤝 기여하기

프로젝트 개선에 참여하고 싶으시다면:

1. 이슈를 생성하여 아이디어 공유
2. Pull Request 제출
3. 문서 개선
4. 버그 리포트

자세한 내용은 [CONTRIBUTING.md](../../CONTRIBUTING.md)를 참조하세요.

## 📝 라이선스

이 프로젝트는 MIT 라이선스 하에 배포됩니다. 자세한 내용은 [LICENSE](../../LICENSE) 파일을 참조하세요.

---

> 💡 **다음 단계**: 기본 설정이 완료되었다면 [펀더멘탈 문서](../01-fundamental/README.md)를 읽어 AI 에이전트의 핵심 개념을 학습하세요!