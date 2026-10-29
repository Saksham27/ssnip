using SSnip.Core.Tools;

namespace SSnip.Core.Processes;

/// <summary>
/// Turns a <see cref="ProcessRequest"/> into a <see cref="ProcessLaunchPlan"/>. Pure, so the
/// rules every tool call follows are unit tested in one place.
/// </summary>
public static class ProcessLaunchPlanner
{
    public const string IgnoreConfigFlag = "--ignore-config";

    public const string JsRuntimesFlag = "--js-runtimes";

    public const string EncodingFlag = "--encoding";

    public static ProcessLaunchPlan Plan(ProcessRequest request, AppPaths paths, string? inheritedPath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(paths);

        var executable = paths.ToolPath(request.Tool);
        var arguments = request.Tool == ExternalTool.YtDlp ? YtDlpArguments(request.Arguments, paths) : [.. request.Arguments];

        // The bin folder goes first on PATH so yt-dlp finds our ffmpeg and Deno, not a system copy.
        var path = string.IsNullOrEmpty(inheritedPath) ? paths.Bin : $"{paths.Bin}{System.IO.Path.PathSeparator}{inheritedPath}";

        var environment = new Dictionary<string, string>
        {
            ["PATH"] = path,
            ["PYTHONIOENCODING"] = "utf-8",
            ["PYTHONUTF8"] = "1",
        };

        return new ProcessLaunchPlan(executable, arguments, environment, CommandLineFormatter.Format(executable, arguments));
    }

    private static IReadOnlyList<string> YtDlpArguments(IReadOnlyList<string> requested, AppPaths paths)
    {
        var arguments = new List<string>(requested.Count + 3);

        // A global yt-dlp config must never change what SSnip asked for.
        if (!requested.Contains(IgnoreConfigFlag))
        {
            arguments.Add(IgnoreConfigFlag);
        }

        // When its output is piped, yt-dlp ignores PYTHONIOENCODING and prints in the ANSI code page,
        // silently dropping Hindi and other characters from titles, JSON and file paths.
        if (!requested.Contains(EncodingFlag))
        {
            arguments.Add(EncodingFlag);
            arguments.Add("utf-8");
        }

        // YouTube needs a JavaScript runtime; point yt-dlp at our Deno instead of relying on PATH lookup.
        if (!requested.Contains(JsRuntimesFlag))
        {
            arguments.Add(JsRuntimesFlag);
            arguments.Add($"deno:{paths.ToolPath(ExternalTool.Deno)}");
        }

        arguments.AddRange(requested);
        return arguments;
    }
}
