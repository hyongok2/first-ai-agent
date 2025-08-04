namespace McpAgent.Configuration;

public class SimpleCliOptions
{
    public string? Model { get; set; }
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 8192;
    public string? SystemPrompt { get; set; }
    public bool ShowTools { get; set; } = false;
    public bool EnableStreaming { get; set; } = true;
    public int Timeout { get; set; } = 300;
    public string PromptStyle { get; set; } = "react";

    public static SimpleCliOptions Parse(string[] args)
    {
        var options = new SimpleCliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--model":
                case "-m":
                    if (i + 1 < args.Length)
                    {
                        options.Model = args[++i];
                    }
                    break;

                case "--temperature":
                case "-t":
                    if (i + 1 < args.Length && double.TryParse(args[++i], out var temp))
                    {
                        options.Temperature = temp;
                    }
                    break;

                case "--max-tokens":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var maxTokens))
                    {
                        options.MaxTokens = maxTokens;
                    }
                    break;

                case "--system-prompt":
                    if (i + 1 < args.Length)
                    {
                        options.SystemPrompt = args[++i];
                    }
                    break;

                case "--show-tools":
                    options.ShowTools = true;
                    break;

                case "--no-streaming":
                    options.EnableStreaming = false;
                    break;

                case "--timeout":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var timeout))
                    {
                        options.Timeout = timeout;
                    }
                    break;

                case "--prompt-style":
                    if (i + 1 < args.Length)
                    {
                        options.PromptStyle = args[++i];
                    }
                    break;

                case "--help":
                case "-h":
                    ShowHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        return options;
    }

    private static void ShowHelp()
    {
        Console.WriteLine(@"
McpAgent - AI Agent with MCP (Model Context Protocol) support

Usage: McpAgent [options]

Options:
  -m, --model <model>           Specify the LLM model (e.g., llama3, qwen2.5)
  -t, --temperature <value>     Set response creativity (0.0-1.0, default: 0.7)
  --max-tokens <number>         Maximum tokens in response (default: 8192)
  --system-prompt <text>        Override default system prompt
  --show-tools                  Display detailed tool execution info
  --no-streaming                Disable streaming responses
  --timeout <seconds>           Operation timeout (default: 300)
  --prompt-style <style>        Use 'direct' or 'react' style (default: react)
  -h, --help                    Show this help message

Examples:
  McpAgent --model llama3.1 --temperature 0.9
  McpAgent --prompt-style direct --show-tools
  McpAgent --system-prompt ""You are a coding assistant""
        ");
    }
}