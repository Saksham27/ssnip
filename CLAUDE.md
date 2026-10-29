# CLAUDE.md

SSnip is a Windows desktop app (WPF, .NET 10) that wraps yt-dlp, FFmpeg and Deno to source footage and cut editor-ready clips. The full spec is `docs/PLAN.md`; build one phase at a time and wait for approval before the next.

## Stack

WPF + WPF-UI (dark Fluent theme), CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting, CliWrap, LibVLCSharp.WPF, SQLite + EF Core (FTS5), Serilog, xUnit + Shouldly, Velopack. Ask before adding any other NuGet package, and update `THIRD_PARTY_NOTICES.md` when adding one.

## Layout

- `src/SSnip.App`: WPF views, view models, controls, converters (MVVM only, no logic)
- `src/SSnip.Core`: models, `DownloadOptions`, `YtDlpArgsBuilder`, parsers, `JobQueue`, `ErrorMapper`, interfaces (pure, unit tested)
- `src/SSnip.Infrastructure`: `ProcessRunner`, yt-dlp/ffmpeg services, `DependencyManager`, `AppDbContext`, file helpers
- `tests/SSnip.Core.Tests` (no network), `tests/SSnip.Integration.Tests` (opt-in, trait `Category=Integration`)
- `tools/fetch-binaries.ps1`: dev helper; binaries go to `tools/bin/` (ignored)

## Conventions

- Never reimplement yt-dlp or FFmpeg features; every external call goes through `ProcessRunner` (bin folder on PATH, `PYTHONIOENCODING=utf-8`, `--ignore-config`, `--js-runtimes deno:<bin>\deno.exe`, kill whole process tree on cancel). The pure rules live in `ProcessLaunchPlanner`.
- yt-dlp ignores `PYTHONIOENCODING` when piped and drops non-ANSI characters (Hindi titles, paths); the planner always passes `--encoding utf-8`.
- yt-dlp skips an existing file quietly (path printed, no real progress); `DownloadResult.WasSkipped` reports it. Section downloads are never skipped.
- Unit-test fixtures in `tests/SSnip.Core.Tests/Fixtures` are hand-written: fake ids and example.com only, never real `-J` output (it holds signed URLs with the user's IP).
- WPF projects drop `System.IO` and `System.Net.Http` from implicit usings; add them explicitly in `SSnip.App`.
- Bind read-only view-model properties to `ProgressBar.Value` and similar two-way-by-default properties with `Mode=OneWay`.
- `Page`s in the navigation frame do not inherit the window foreground; set `Foreground="{DynamicResource TextFillColorPrimaryBrush}"`.
- Integration tests: `dotnet test --filter Category=Integration` (downloads tools into `%TEMP%\SSnip.IntegrationTests` on first run).
- Never add Claude attribution (Co-Authored-By trailers or "Generated with Claude Code") to commits or PRs.
- Build command lines as argument lists, never concatenated strings.
- All process calls are async, cancellable and report progress; never block the UI thread.
- Write unit tests for the args builder and every parser alongside the code.
- Conventional Commits; commit after each feature. File-scoped namespaces, nullable on, warnings as errors.
- Never commit cookies, tokens, databases, logs or media; never use copyrighted URLs in tests or docs.
- Do not delete or rewrite the Phase S repo files (LICENSE, README, notices, templates) without asking.
