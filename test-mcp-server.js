#!/usr/bin/env node

const readline = require('readline');

const rl = readline.createInterface({
  input: process.stdin,
  output: process.stdout,
  crlfDelay: Infinity
});

// Sample MCP Server Implementation
class SimpleMcpServer {
  constructor() {
    this.tools = [
      {
        name: "echo",
        description: "Echo back the input text",
        inputSchema: {
          type: "object",
          properties: {
            text: {
              type: "string",
              description: "Text to echo back"
            }
          },
          required: ["text"]
        }
      },
      {
        name: "add_numbers",
        description: "Add two numbers",
        inputSchema: {
          type: "object",
          properties: {
            a: { type: "number", description: "First number" },
            b: { type: "number", description: "Second number" }
          },
          required: ["a", "b"]
        }
      }
    ];
  }

  handleRequest(request) {
    const response = {
      jsonrpc: "2.0",
      id: request.id
    };

    try {
      switch (request.method) {
        case "initialize":
          response.result = {
            protocolVersion: "2025-06-18",
            capabilities: {
              tools: {}
            },
            serverInfo: {
              name: "simple-test-server",
              version: "1.0.0"
            }
          };
          break;

        case "tools/list":
          response.result = {
            tools: this.tools
          };
          break;

        case "tools/call":
          const { name, arguments: args } = request.params;
          response.result = this.callTool(name, args);
          break;

        default:
          response.error = {
            code: -32601,
            message: "Method not found",
            data: { method: request.method }
          };
      }
    } catch (error) {
      response.error = {
        code: -32603,
        message: "Internal error",
        data: { error: error.message }
      };
    }

    return response;
  }

  callTool(name, args) {
    switch (name) {
      case "echo":
        return {
          content: [{
            type: "text",
            text: `Echo: ${args.text || "Hello from MCP Server!"}`
          }]
        };

      case "add_numbers":
        const sum = (args.a || 0) + (args.b || 0);
        return {
          content: [{
            type: "text", 
            text: `${args.a} + ${args.b} = ${sum}`
          }]
        };

      default:
        return {
          content: [{
            type: "text",
            text: `Unknown tool: ${name}`
          }],
          isError: true
        };
    }
  }
}

// Start the server
const server = new SimpleMcpServer();

console.error("Simple MCP Test Server started");

rl.on('line', (line) => {
  if (line.trim() === '') return;

  try {
    const request = JSON.parse(line);
    console.error(`Received request: ${request.method}`);
    
    const response = server.handleRequest(request);
    console.log(JSON.stringify(response));
  } catch (error) {
    console.error(`Error processing request: ${error.message}`);
    console.log(JSON.stringify({
      jsonrpc: "2.0",
      id: null,
      error: {
        code: -32700,
        message: "Parse error",
        data: { error: error.message }
      }
    }));
  }
});

rl.on('close', () => {
  console.error("Simple MCP Test Server shutting down");
  process.exit(0);
});