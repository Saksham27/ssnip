using SSnip.Core.Tools;

namespace SSnip.Core.Processes;

/// <summary>A request to run one external tool. Arguments are a list, never a joined string.</summary>
public sealed record ProcessRequest(ExternalTool Tool, IReadOnlyList<string> Arguments, string? WorkingDirectory = null);

public enum OutputStream
{
    StandardOutput,
    StandardError,
}

public sealed record OutputLine(OutputStream Stream, string Text);

/// <summary>The outcome of a finished process. <see cref="CommandLine"/> is the exact command, for "Show command".</summary>
public sealed record ProcessResult(int ExitCode, string CommandLine, DateTimeOffset StartTime, TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Everything needed to start a tool: resolved executable, final arguments and environment.</summary>
public sealed record ProcessLaunchPlan(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string CommandLine);
