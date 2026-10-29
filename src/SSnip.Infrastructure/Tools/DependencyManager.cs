using System.IO.Compression;
using Microsoft.Extensions.Logging;
using SSnip.Core;
using SSnip.Core.Processes;
using SSnip.Core.Tools;

namespace SSnip.Infrastructure.Tools;

public sealed partial class DependencyManager(
    AppPaths paths,
    IProcessRunner processRunner,
    HttpClient httpClient,
    ILogger<DependencyManager> logger) : IDependencyManager
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);

    // One install at a time per archive, so FFmpeg and ffprobe never download twice in parallel.
    private readonly Dictionary<ToolSource, SemaphoreSlim> _installGates = new()
    {
        [ToolSources.YtDlp] = new(1, 1),
        [ToolSources.Ffmpeg] = new(1, 1),
        [ToolSources.Deno] = new(1, 1),
    };

    public async Task<ToolStatus> GetStatusAsync(ExternalTool tool, CancellationToken cancellationToken = default)
    {
        var path = paths.ToolPath(tool);
        if (!File.Exists(path))
        {
            return new ToolStatus(tool, path, IsInstalled: false, Version: null, Error: null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(VersionTimeout);
        try
        {
            var descriptor = ToolDescriptor.For(tool);
            var captured = await processRunner.RunAndCaptureAsync(new ProcessRequest(tool, descriptor.VersionArguments), timeout.Token).ConfigureAwait(false);
            var version = ToolVersionParser.Parse(tool, captured.StandardOutput);
            return version is null
                ? new ToolStatus(tool, path, true, null, $"Could not read the version (exit code {captured.Result.ExitCode}).")
                : new ToolStatus(tool, path, true, version, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ToolStatus(tool, path, true, null, "Timed out reading the version.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            LogStatusFailed(ex, tool);
            return new ToolStatus(tool, path, true, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<ToolStatus>> GetAllStatusAsync(CancellationToken cancellationToken = default) =>
        await Task.WhenAll(Enum.GetValues<ExternalTool>().Select(t => GetStatusAsync(t, cancellationToken))).ConfigureAwait(false);

    public async Task InstallAsync(ExternalTool tool, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var source = ToolSources.For(tool);
        var gate = _installGates[source];
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(paths.Bin);
            Directory.CreateDirectory(paths.Downloads);

            var download = Path.Combine(paths.Downloads, $"{Guid.NewGuid():N}{(source.IsZip ? ".zip" : ".exe")}");
            try
            {
                LogDownloading(tool, source.Url);
                await DownloadAsync(source.Url, download, progress, cancellationToken).ConfigureAwait(false);

                progress?.Report(new InstallProgress(InstallStage.Extracting));
                if (source.IsZip)
                {
                    ExtractTools(download, source.Provides);
                }
                else
                {
                    ReplaceFile(download, paths.ToolPath(source.Provides[0]));
                }
            }
            finally
            {
                TryDelete(download);
            }

            progress?.Report(new InstallProgress(InstallStage.Verifying));
            foreach (var provided in source.Provides)
            {
                var status = await GetStatusAsync(provided, cancellationToken).ConfigureAwait(false);
                if (!status.IsHealthy)
                {
                    throw new InvalidOperationException($"{ToolDescriptor.For(provided).DisplayName} was downloaded but does not run: {status.Error}");
                }

                LogInstalled(provided, status.Version!);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateAsync(ExternalTool tool, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (tool != ExternalTool.YtDlp || !File.Exists(paths.ToolPath(tool)))
        {
            await InstallAsync(tool, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        progress?.Report(new InstallProgress(InstallStage.Updating));
        var captured = await processRunner.RunAndCaptureAsync(new ProcessRequest(ExternalTool.YtDlp, ["-U"]), cancellationToken).ConfigureAwait(false);
        if (!captured.Result.Succeeded)
        {
            throw new InvalidOperationException($"yt-dlp -U failed: {captured.StandardError.Trim()}");
        }
    }

    public async Task ImportAsync(ExternalTool tool, string sourceExecutablePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceExecutablePath);
        if (!File.Exists(sourceExecutablePath))
        {
            throw new FileNotFoundException("The selected file does not exist.", sourceExecutablePath);
        }

        Directory.CreateDirectory(paths.Bin);
        var target = paths.ToolPath(tool);
        if (!string.Equals(Path.GetFullPath(sourceExecutablePath), target, StringComparison.OrdinalIgnoreCase))
        {
            var temp = target + ".import";
            File.Copy(sourceExecutablePath, temp, overwrite: true);
            ReplaceFile(temp, target);
        }

        var status = await GetStatusAsync(tool, cancellationToken).ConfigureAwait(false);
        if (!status.IsHealthy)
        {
            throw new InvalidOperationException($"The selected file is not a working {ToolDescriptor.For(tool).DisplayName}: {status.Error}");
        }
    }

    public async Task<IReadOnlyList<string>> GetJavaScriptRuntimesAsync(CancellationToken cancellationToken = default)
    {
        // about:blank fails fast without network access, but the verbose header is printed first.
        var captured = await processRunner.RunAndCaptureAsync(
            new ProcessRequest(ExternalTool.YtDlp, ["--verbose", "--simulate", "--no-warnings", "about:blank"]),
            cancellationToken).ConfigureAwait(false);

        return YtDlpDebugHeaderParser.ParseJsRuntimes(captured.StandardError + captured.StandardOutput)
            ?? throw new InvalidOperationException("yt-dlp did not report its JavaScript runtimes.");
    }

    private async Task DownloadAsync(Uri url, string destination, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long received = 0;
        var lastReport = 0L;
        int read;
        progress?.Report(new InstallProgress(InstallStage.Downloading, 0, total));
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (received - lastReport >= 512 * 1024)
            {
                lastReport = received;
                progress?.Report(new InstallProgress(InstallStage.Downloading, received, total));
            }
        }

        progress?.Report(new InstallProgress(InstallStage.Downloading, received, total));
    }

    private void ExtractTools(string zipPath, IReadOnlyList<ExternalTool> tools)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var tool in tools)
        {
            var name = ToolDescriptor.For(tool).ExecutableName;

            // Archives nest the exe in a versioned folder, for example ffmpeg-8.0-essentials_build/bin/ffmpeg.exe.
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"The download does not contain {name}.");

            var temp = paths.ToolPath(tool) + ".download";
            entry.ExtractToFile(temp, overwrite: true);
            ReplaceFile(temp, paths.ToolPath(tool));
        }
    }

    private static void ReplaceFile(string source, string target) => File.Move(source, target, overwrite: true);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading {Tool} from {Url}")]
    private partial void LogDownloading(ExternalTool tool, Uri url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installed {Tool} {Version}")]
    private partial void LogInstalled(ExternalTool tool, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the status of {Tool}")]
    private partial void LogStatusFailed(Exception exception, ExternalTool tool);
}
