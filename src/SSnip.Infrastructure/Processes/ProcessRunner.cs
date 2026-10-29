using System.Diagnostics;
using System.Text;
using CliWrap;
using Microsoft.Extensions.Logging;
using SSnip.Core;
using SSnip.Core.Processes;

namespace SSnip.Infrastructure.Processes;

/// <summary>
/// Runs every external tool. CliWrap kills the entire process tree on cancellation
/// (<c>Process.Kill(entireProcessTree: true)</c>), so ffmpeg children of yt-dlp die with it.
/// </summary>
public sealed partial class ProcessRunner(AppPaths paths, ILogger<ProcessRunner> logger) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<OutputLine>? output = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = ProcessLaunchPlanner.Plan(request, paths, Environment.GetEnvironmentVariable("PATH"));
        if (!File.Exists(plan.ExecutablePath))
        {
            throw new FileNotFoundException($"{Path.GetFileName(plan.ExecutablePath)} is not installed.", plan.ExecutablePath);
        }

        var command = Cli.Wrap(plan.ExecutablePath)
            .WithArguments(plan.Arguments)
            .WithEnvironmentVariables(env =>
            {
                foreach (var (name, value) in plan.Environment)
                {
                    env.Set(name, value);
                }
            })
            .WithWorkingDirectory(request.WorkingDirectory ?? paths.Root)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(line => output?.Report(new OutputLine(OutputStream.StandardOutput, line)), Encoding.UTF8))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(line => output?.Report(new OutputLine(OutputStream.StandardError, line)), Encoding.UTF8));

        LogStarting(plan.CommandLine);
        var started = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            LogFinished(Path.GetFileName(plan.ExecutablePath), result.ExitCode, stopwatch.Elapsed);
            return new ProcessResult(result.ExitCode, plan.CommandLine, started, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            LogCancelled(Path.GetFileName(plan.ExecutablePath), stopwatch.Elapsed);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting {CommandLine}")]
    private partial void LogStarting(string commandLine);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Tool} exited with {ExitCode} after {Elapsed}")]
    private partial void LogFinished(string tool, int exitCode, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Tool} cancelled after {Elapsed}; process tree killed")]
    private partial void LogCancelled(string tool, TimeSpan elapsed);
}
