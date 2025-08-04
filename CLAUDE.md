# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

McpAgent is a .NET 8 AI agent that integrates local LLM providers (Ollama) with Model Context Protocol (MCP) servers for extensible tool capabilities. The architecture follows clean separation of concerns with dependency injection, hosted services, and provider patterns.

## Build and Run Commands

```bash
# Build the solution (from repository root)
dotnet build first-ai-agent.sln

# Run the agent
cd src/McpAgent
dotnet run

# Run with CLI options
dotnet run -- --help
dotnet run -- --model llama3.1 --temperature 0.9 --prompt-style react

# Restore packages if needed
dotnet restore

# Clean build
dotnet clean && dotnet build
```

## Core Architecture

### Entry Point and DI Setup
- **Program.cs**: Main entry point with full dependency injection configuration
- **AgentHostedService.cs**: Background service managing interactive CLI

### Core Agent Flow
- **Agent.cs**: Main orchestrator handling LLM interaction, MCP tool execution, and conversation management
- **Tool chaining**: Supports iterative tool calls with configurable max iterations (default: 5)
- **Dual prompt styles**: "direct" (JSON tool calls) or "react" (Thought → Action → Observation)

### MCP Integration Layer
- **McpClient.cs**: Main MCP protocol implementation with JSON-RPC over stdio
- **Multiple transports**: Named pipes, TCP, and standard stdio
- **Tool discovery**: Dynamic registration of tools from connected MCP servers
- **Error handling**: Retry logic with exponential backoff for server communication

### LLM Provider Abstraction
- **ILlmProvider**: Provider-agnostic interface for LLM integration
- **OllamaProvider**: Semantic Kernel integration with tool-aware generation
- **Tool metadata**: Automatically passes available MCP tools to LLM for structured responses

### Configuration System
- **Hierarchical config**: appsettings.json → environment variables → CLI overrides
- **Environment prefix**: `MCPAGENT_` (e.g., `MCPAGENT_Agent__Llm__Model=llama3.1`)
- **CLI options**: Rich command-line interface with help system via CliOptions.cs

### Memory and Conversation Management
- **IConversationManager**: Interface for conversation persistence
- **InMemoryConversationManager**: Default implementation with configurable history limits
- **Context preservation**: Maintains conversation state across tool chains

## Key Configuration Points

### appsettings.json Structure
- **Agent.Llm**: Provider settings (model, temperature, max tokens)
- **Agent.Mcp**: Server definitions with command/args/environment
- **Agent.Agent**: Core agent settings (prompts, chaining, history)

### Prompt Templates (/Prompts)
- **system-prompt.txt**: Direct JSON tool calling style
- **system-prompt-react.txt**: ReAct reasoning pattern
- **tool-chain.txt**: Tool chaining continuation prompt
- **tool-result.txt**: Tool result formatting

## Development Patterns

### Adding New LLM Providers
1. Implement `ILlmProvider` interface
2. Register in Program.cs DI container
3. Update configuration model
4. Add provider string to configuration

### Adding MCP Server Support
Add server configuration to appsettings.json:
```json
{
  "Name": "server-name",
  "Command": "executable",
  "Args": ["arg1", "arg2"],
  "Env": {"KEY": "value"}
}
```

### Extending Conversation Management
Implement `IConversationManager` for:
- Database persistence
- File-based storage
- Distributed caching
- Custom serialization

### Error Handling Patterns
- **AgentException**: Custom exceptions with structured error info
- **RetryHelper**: Exponential backoff for transient failures
- **Graceful degradation**: Agent continues if MCP servers fail

## Tool Chain Behavior

The agent supports sophisticated tool chaining:
- **Iteration limit**: Configurable max iterations (Agent.MaxToolChainIterations)
- **Context preservation**: Full conversation history maintained
- **Style switching**: Can use direct JSON or ReAct patterns
- **Error recovery**: Continues processing on tool failures when possible

## Dependencies and Tech Stack

### Core Frameworks
- **.NET 8**: Modern C# with nullable reference types
- **Microsoft.Extensions.Hosting**: Background service architecture
- **Microsoft.SemanticKernel**: LLM abstraction and tool integration

### Key NuGet Packages
- **Microsoft.SemanticKernel.Connectors.Ollama**: Local LLM integration
- **System.Text.Json**: JSON serialization for MCP protocol
- **Microsoft.Extensions.Configuration**: Hierarchical configuration system

### External Dependencies
- **Ollama**: Local LLM provider (default: http://localhost:11434)
- **Node.js**: Required for many MCP servers
- **MCP servers**: External tools (filesystem, web, APIs, etc.)

## Service Architecture

The application uses a hosted service pattern:
- **AgentHostedService**: Main CLI interaction loop
- **IStreamingService**: Response streaming abstraction
- **IPromptService**: Template-based prompt management
- **Scoped services**: Agent, conversation manager, MCP client per request

This architecture supports both interactive CLI usage and potential future API hosting scenarios.