# McpAgent - AI Agent with Local LLM and MCP Integration

A robust AI Agent built with .NET 8 that integrates with Local LLM providers (Ollama) and supports Model Context Protocol (MCP) servers for extensible tool capabilities.

## Features

- 🤖 **Local LLM Integration**: Seamless integration with Ollama for private, local AI inference
- 🔌 **MCP Server Support**: Connect to multiple MCP servers for extended tool capabilities
- 💬 **Conversation Management**: Intelligent conversation history and context management
- 🏗️ **Plugin Architecture**: Extensible system for adding new capabilities
- ⚙️ **Configuration-Driven**: Flexible configuration system with environment variable support
- 📊 **Comprehensive Logging**: Built-in logging with configurable levels

## Architecture

```
McpAgent/
├── Core/                    # Core agent logic
│   ├── IAgent.cs           # Main agent interface
│   └── Agent.cs            # Core agent implementation
├── Configuration/          # Configuration management
│   ├── AgentConfiguration.cs
│   └── AppSettings.cs (deprecated)
├── Providers/              # LLM provider abstractions
│   ├── ILlmProvider.cs
│   └── OllamaProvider.cs
├── Mcp/                    # MCP client implementation
│   ├── IMcpClient.cs
│   └── McpClient.cs
├── Memory/                 # Conversation management
│   ├── IConversationManager.cs
│   └── InMemoryConversationManager.cs
├── Models/                 # Data models
│   └── AgentModels.cs
└── Services/               # Background services
    └── AgentHostedService.cs
```

## Prerequisites

- .NET 8.0 SDK
- Ollama running locally (default: http://localhost:11434)
- Node.js (for MCP servers)

## Quick Start

### 1. Install Ollama and a Model

```bash
# Install Ollama (macOS/Linux)
curl -fsSL https://ollama.ai/install.sh | sh

# Pull a model (e.g., Llama 3)
ollama pull llama3
```

### 2. Install MCP Server (Optional)

```bash
# Install a filesystem MCP server
npm install -g @modelcontextprotocol/server-filesystem
```

### 3. Configure the Agent

Edit `appsettings.json`:

```json
{
  "Agent": {
    "Llm": {
      "Provider": "ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "llama3",
      "MaxTokens": 4096,
      "Temperature": 0.7
    },
    "Mcp": {
      "Enabled": true,
      "Servers": [
        {
          "Name": "filesystem",
          "Command": "npx",
          "Args": ["@modelcontextprotocol/server-filesystem", "/tmp"],
          "Env": {}
        }
      ]
    },
    "Agent": {
      "Name": "McpAgent",
      "SystemPrompt": "You are a helpful AI assistant with access to various tools.",
      "MaxHistoryLength": 50,
      "EnableLogging": true
    }
  }
}
```

### 4. Run the Agent

```bash
cd src/McpAgent
dotnet run
```

## Configuration Options

### LLM Configuration

- **Provider**: LLM provider type (currently supports "ollama")
- **Endpoint**: LLM server endpoint
- **Model**: Model name to use
- **MaxTokens**: Maximum tokens in response
- **Temperature**: Response creativity (0.0-1.0)

### MCP Configuration

- **Enabled**: Enable/disable MCP functionality
- **Servers**: List of MCP servers to connect to
  - **Name**: Unique server identifier
  - **Command**: Executable command
  - **Args**: Command arguments
  - **Env**: Environment variables

### Agent Settings

- **Name**: Agent display name
- **SystemPrompt**: System instruction for the agent
- **MaxHistoryLength**: Maximum conversation history to maintain
- **EnableLogging**: Enable detailed logging

## Environment Variables

Override configuration using environment variables:

```bash
export MCPAGENT_Agent__Llm__Model=llama3.1
export MCPAGENT_Agent__Llm__Temperature=0.5
export MCPAGENT_Agent__Agent__Name="My Custom Agent"
```

## Usage Examples

### Basic Chat

```
You: Hello, how are you?
Assistant: Hello! I'm doing well, thank you for asking. I'm here to help you with any questions or tasks you might have. How can I assist you today?
```

### Using MCP Tools (with filesystem server)

```
You: Can you list the files in the current directory?
Assistant: {"tool_call": {"name": "list_directory", "arguments": {"path": "."}}}

[Used 1 tool(s): list_directory]
I can see several files in the current directory including Program.cs, appsettings.json, and the McpAgent.csproj project file, along with various source code directories like Core/, Models/, and Services/.
```

### Commands

- `help`: Show available commands
- `quit` or `exit`: Stop the agent

## Extending the Agent

### Adding New LLM Providers

1. Implement `ILlmProvider` interface
2. Register in dependency injection
3. Update configuration

### Adding MCP Servers

Add server configuration to `appsettings.json`:

```json
{
  "Name": "my-server",
  "Command": "python",
  "Args": ["my_mcp_server.py"],
  "Env": {
    "API_KEY": "your-api-key"
  }
}
```

### Custom Conversation Managers

Implement `IConversationManager` for persistent storage:

- Database storage
- File-based storage
- Redis cache
- etc.

## Logging

The agent provides comprehensive logging at multiple levels:

- **Information**: General operation info
- **Debug**: Detailed MCP operations
- **Warning**: Non-critical issues
- **Error**: Critical errors

Configure logging in `appsettings.json` under the `Logging` section.

## Troubleshooting

### Common Issues

1. **Ollama Connection Failed**
   - Ensure Ollama is running: `ollama serve`
   - Check endpoint configuration
   - Verify model is available: `ollama list`

2. **MCP Server Connection Failed**
   - Verify MCP server is installed
   - Check command path and arguments
   - Review server logs for errors

3. **Build Errors**
   - Ensure .NET 8.0 SDK is installed
   - Restore packages: `dotnet restore`
   - Clean build: `dotnet clean && dotnet build`

## License

This project is licensed under the MIT License - see the LICENSE file for details.