# SSnip — yt-dlp Windows App Build Plan

Oct 29, 2026 · @Saksham SIngh

## How to use this plan (for Claude Code)

Build SSnip, a Windows desktop app that wraps yt-dlp and ffmpeg, in a setup phase plus seven build phases, all listed under Build phases. Finish one phase, demo it, and wait for approval before you start the next.

- Start with Phase S, which creates the GitHub repository with its license and notice files; after that, work only inside that repo and keep those files. Read the whole plan first, then write a short `CLAUDE.md` in the repo root that records the stack, folder layout and conventions below.
- Never reimplement anything yt-dlp or ffmpeg already does. Every download, fetch and cut goes through the process wrapper in Tech stack and architecture.
- Build command lines as argument lists, never as concatenated strings, so titles and paths with spaces, quotes or Hindi text cannot break them.
- Keep the UI thread free: every process call is async, cancellable and reports progress.
- Ask before adding a NuGet package that is not listed here, or before changing a decision in this plan.
- Write unit tests for the argument builder and every parser as you build them, not at the end.
- Commit after each feature with a clear message.
- When something here is unclear, ask one focused question instead of guessing.

## Purpose and use case

SSnip (the owner's initials, SS for Saksham Singh, plus "snip") turns "I need that scene for my video" into an editor-ready clip in under a minute, without playing or screen-recording anything.

The owner is a full-time software engineer who makes YouTube videos on a tight schedule: about 1 hour on weekdays and 3 hours a day on weekends. The channels are Jemma Live (gaming: Valorant, Dota 2, story recaps such as the Call of Duty Modern Warfare reboot trilogy) and Geeky Saksham (tech). He has YouTube Premium and edits in DaVinci Resolve.

What the app must make easy:

- **Source footage**: gameplay, cutscenes, trailers and B-roll from YouTube and other sites, instead of recording it himself.
- **Cut clips**: grab only the 20 to 60 seconds he needs, not a 3-hour playthrough.
- **Edit straight away**: files that import into Resolve and scrub smoothly.
- **Queue and walk away**: line up work in a short session, let it run overnight.
- **Credit sources**: every clip traceable to its original URL and channel.

The app is not a video editor or a media player. It prepares footage; Resolve does the editing.

## Tech stack and architecture

A WPF app on .NET 10 (LTS) that drives yt-dlp, ffmpeg and Deno as child processes. Use .NET 10, not .NET 8: .NET 8 support ends in November 2026.

| Layer | Choice | Notes |
| --- | --- | --- |
| UI | WPF + WPF-UI (Fluent styling) | Dark theme by default, MVVM throughout |
| MVVM | CommunityToolkit.Mvvm | Source-generated observable properties and commands |
| Hosting / DI | Microsoft.Extensions.Hosting | Services, options, logging in one place |
| Process calls | CliWrap | Async streams of stdout/stderr, cancellation |
| Preview player | LibVLCSharp.WPF | Scrub proxies and local files to mark clips |
| Storage | SQLite + EF Core | Jobs, projects, clips, transcripts (FTS5) |
| Logging | Serilog | Rolling app log + one log file per job |
| Tests | xUnit + Shouldly | Core logic fully unit tested |
| Packaging | Velopack | Installer and app self-update |
| External tools | yt-dlp.exe, ffmpeg.exe, ffprobe.exe, deno.exe | Downloaded on first run into the app's bin folder |

&#91;embedded content: architecture · 4 layers\]

The UI only produces options, Core turns them into argument lists and jobs, and ProcessRunner is the one place that starts yt-dlp, ffmpeg or Deno.

### Process wrapper rules

- One `ProcessRunner` runs every external tool. It prepends the app's bin folder to the child's `PATH` so yt-dlp finds ffmpeg and Deno, sets `PYTHONIOENCODING=utf-8`, and always passes `--ignore-config` so a global yt-dlp config cannot change behaviour.
- Deno is required: YouTube downloads need an external JavaScript runtime since early 2026. Confirm the exact JS-runtime flag with `yt-dlp --help` for the installed version.
- Fetch info with `yt-dlp -J --no-warnings <url>`; for playlists and channels add `--flat-playlist` so listing is fast.
- Report progress with `--newline --progress-template "download:FSPROG %(progress)j"` and parse the JSON after the `FSPROG` prefix. Get the final file path with `--print "after_move:FSFILE %(filepath)s"`.
- `YtDlpArgsBuilder` is a pure function: `DownloadOptions` in, argument list out. The UI never builds arguments itself.
- Cancel kills the whole process tree (`Process.Kill(entireProcessTree: true)`), because yt-dlp spawns ffmpeg.
- Every job keeps its exact command line, so a "Show command" button can display and copy it.

## Solution and folder structure

Three projects keep the UI thin and the logic testable: App (WPF), Core (models and pure logic), Infrastructure (processes, database, files).

```
SSnip/
  SSnip.sln
  CLAUDE.md
  src/
    SSnip.App/              WPF: Views, ViewModels, Controls, Converters, App.xaml
    SSnip.Core/             Models, DownloadOptions, YtDlpArgsBuilder, parsers,
                                    JobQueue, ErrorMapper, service interfaces
    SSnip.Infrastructure/   ProcessRunner, YtDlpService, FfmpegService,
                                    DependencyManager, AppDbContext, file system helpers
  tests/
    SSnip.Core.Tests/         unit tests, no network
    SSnip.Integration.Tests/  opt-in, real binaries and network
  tools/
    fetch-binaries.ps1              dev helper to download yt-dlp, ffmpeg, deno
```

Runtime data lives under `%LOCALAPPDATA%\SSnip\`:

```
bin\       yt-dlp.exe, ffmpeg.exe, ffprobe.exe, deno.exe
data\      footage.db (SQLite)
logs\      app log + one log per job
cache\     thumbnails, proxies
settings.json
```

Footage goes to a library root the user picks on first run (for example `D:\Footage`). Each project gets its own folder:

```
<Library root>\<Project name>\
  raw\       full downloads
  clips\     cut sections
  audio\     extracted audio
  frames\    still images
  subs\      subtitles and transcripts
  proxies\   low-res preview copies (safe to delete)
  credits.md
```

## Fetch, formats, downloads and queue

Paste a link, see what it is, pick a quality preset or exact formats, and drop it into a persistent queue.

### Input

- One box that accepts one or many URLs (one per line), drag-and-dropped links, and an optional "fetch the URL on the clipboard when the app gets focus" setting.
- Detect the kind of link: single video, playlist, channel, live stream, or a search term (sent to search).
- Works on any site yt-dlp supports, not only YouTube.

### Info panel (from `-J`)

- Thumbnail, title, channel, duration, upload date, view count, live status.
- Chapter list with timestamps, available subtitle languages (manual vs auto), description.

### Format selection

Simple mode shows presets; Advanced mode shows every format.

| Preset | yt-dlp arguments | When to use |
| --- | --- | --- |
| Editor-safe (default) | `-S "vcodec:h264,res,fps,acodec:aac" --merge-output-format mp4` | H.264 MP4, usually up to 1080p, imports into Resolve as is |
| Max quality | `-f "bv*+ba/b" --merge-output-format mkv` | 1440p/4K, often VP9 or AV1; convert before editing |
| Max quality, then convert | Max quality + an editor profile (see Clipping) | Best picture that still edits smoothly |
| 1080p cap | `-S "res:1080,fps"` | Smaller files, enough for most edits |
| Audio only | `-x --audio-format wav` (or mp3, m4a, opus, flac) | Music beds, voice lines |
| Preview proxy | `-f "bv*[height<=360]+ba/b[height<=360]" -S "vcodec:h264"` | Fast low-res copy for marking clips |

Advanced mode:

- Sortable table of all formats: id, container, resolution, fps, video codec, audio codec, bitrate, size, HDR, protocol, note.
- Pick one video and one audio format, or type a sort string (`-S`) with helper chips for resolution, fps, codec preference and size.
- Show estimated total size before download; flag formats whose note says Premium (see Account).

### Per-job options

- Target project and folder, filename template (presets plus custom, for example `%(upload_date)s - %(title)s [%(id)s].%(ext)s`), container, overwrite or skip.
- Rate limit, concurrent fragments (`-N 4` default), retries, `--trim-filenames 150` to stay under Windows path limits.

### Queue

- Job states: Queued, Fetching, Downloading, Post-processing, Done, Failed, Cancelled, Paused.
- Run 2 jobs at once by default (setting 1 to 5); reorder, pause, resume (keeps `.part` files), cancel, retry, remove.
- Each row shows percent, speed, ETA, downloaded/total size and current stage (video, audio, merging, converting).
- The queue is stored in SQLite and resumes after the app or PC restarts.
- Taskbar progress, a Windows toast per finished job and when the whole queue is done.
- "When queue finishes" option: do nothing, sleep, or shut down the PC, for overnight runs.

### Live streams

- Record from the start (`--live-from-start`) or from now; wait for a scheduled stream (`--wait-for-video 60`).
- A Stop button ends recording and leaves a playable file (remux the partial download with ffmpeg if needed).

## Clipping, frame grabs and editor-ready output

Clipping is the core feature: mark scenes on a cheap preview, then download only those sections at full quality, ready for Resolve.

### Proxy-first workflow

1. Download a 360p preview proxy of the source (fast, small), stored in `proxies\`.
2. Scrub it in the built-in player and mark in/out points for each clip.
3. Download every marked section at the chosen full quality in one batch.
4. Optionally convert each clip with an editor profile.

### Clip editor

- LibVLC player with a timeline, current time to the millisecond and frame stepping.
- Keys: `I` / `O` set in and out, `J` / `K` / `L` reverse, pause, forward (press again to speed up), arrow keys step a frame, `Enter` adds the clip, `Ctrl+Z` undoes.
- Each clip has a name, tags and a note, and shows its duration.
- Handles: add 0 to 5 seconds of padding before and after each clip (default 2 s) to leave room for transitions.

### Section downloads from a URL

- Uses `--download-sections "*00:12:30-00:13:10"`, one argument per clip, for any marked range or typed time range.
- Cut mode toggle: **Fast** (cuts on keyframes, can be off by a few seconds, no re-encode) or **Accurate** (`--force-keyframes-at-cuts`, re-encodes, slower).
- Chapters: tick chapters in the info panel to download each as its own clip (`--download-sections` with the chapter title, or `--split-chapters` for all).

### Timestamp import

- Paste lines such as `12:30-13:10 Soap scene` or `1:02:15 - 1:03:00 Final mission` and turn each into a clip.
- Scan the description (and comments, if fetched) for timestamps and offer them as candidate clips.

### Local file clipping

- Open any video already on disk, mark clips the same way, and cut with ffmpeg: stream copy (fast) or re-encode (accurate).

### Frame grabs

- Save the current frame as a full-resolution PNG (taken from the full-quality file or stream, not the proxy).
- Batch grab one frame every N seconds into `frames\`, plus an optional contact sheet image for storyboarding.
- Download the source's best thumbnail for reference.

### Editor-ready profiles (ffmpeg)

Resolve imports MP4 or MOV with H.264, H.265, ProRes or DNxHR most reliably; WebM, MKV and VP9 files often fail or scrub badly.

| Profile | Output | Encoder | Trade-off |
| --- | --- | --- | --- |
| H.264 MP4 (default) | `.mp4`, high bitrate | `h264_amf` on the AMD GPU, fallback `libx264 -crf 16` | Small, imports everywhere |
| DNxHR HQ | `.mov` | `dnxhd -profile:v dnxhr_hq` | Smoothest scrubbing, very large files |
| ProRes 422 | `.mov` | `prores_ks -profile:v 2` | Alternative intermediate, large |
| Keep original | as downloaded | none | No conversion |

Profile options: force constant frame rate (YouTube files can be variable, which drifts audio in editors), target fps (keep, 30, 60), scale (keep, 1080p, 1440p, 2160p), audio as 48 kHz AAC or PCM.

Detect available hardware encoders once with `ffmpeg -hide_banner -encoders` and only offer those (`h264_amf`, `hevc_amf`, `av1_amf` on AMD; `h264_nvenc` on NVIDIA; `h264_qsv` on Intel).

## Search, playlists, channels and transcript search

Find footage inside the app, pull it in bulk, and search what is said in videos to jump straight to the right moment.

### YouTube search

- Search box that runs `ytsearch30:<query>` (or `ytsearchdate30:` for newest first) with `--flat-playlist -J`.
- Results grid: thumbnail, title, channel, duration, upload date.
- Client-side filters: duration range, upload date range, hide Shorts.
- Actions on any result: Preview, Add to project, Queue download, Open in clip editor.

### Playlists and channels

- List entries fast with `--flat-playlist`, show them with checkboxes, select all, ranges, or `-I 1:10` style item lists.
- Channel tabs: Videos, Shorts, Live.
- Filters: upload date after/before (`--dateafter`, `--datebefore`), min/max duration and title text or regex (`--match-filter`), max downloads.

### Channel sync

- Save a channel or playlist as a watched source with its own preset and target project.
- "Sync" downloads only new uploads, using a per-source `--download-archive` file.
- Optional: sync all watched sources when the app starts.

### Transcript search

The biggest time saver for story recaps: find the line of dialogue, get the timestamp, make the clip.

- Fetch subtitles without the video (`--write-subs --write-auto-subs --sub-langs "en.*,hi.*" --sub-format vtt --skip-download`).
- Parse each VTT cue (start, end, text) into a SQLite FTS5 table linked to its source video.
- Search across one project or the whole library; results show the video, timestamp and matching line.
- Click a result to preview from that moment; "Make clip" creates a clip around the line with its own padding setting (default 5 s before and after).

## Audio, subtitles, metadata and SponsorBlock

Every yt-dlp extra that helps editing is a checkbox, with sensible footage defaults.

### Audio

- Extract audio as WAV (default for editing), MP3, M4A, Opus or FLAC, with quality setting (`-x --audio-format <fmt> --audio-quality 0`).
- Extract audio from a clip section only.
- Prefer the original audio language over auto-dubbed tracks when a video has several.

### Subtitles

- List manual and auto-generated languages; pick with priority (default English, then Hindi).
- Save as separate SRT (`--convert-subs srt`) or embed into the file (`--embed-subs`).
- Saved subtitles are indexed for transcript search automatically.

### Metadata

- Embed metadata, thumbnail and chapters (`--embed-metadata --embed-thumbnail --embed-chapters`).
- Always save `info.json` into the project's data (not next to the video) to feed the source log.
- Optional: save description and comments (`--write-comments` with a limit such as `--extractor-args "youtube:max_comments=200"`).

### SponsorBlock

- Category checkboxes: sponsor, intro, outro, selfpromo, interaction, preview, music\_offtopic, filler.
- Mode: mark as chapters (`--sponsorblock-mark`) or cut out (`--sponsorblock-remove`).
- Default for footage: remove sponsor, selfpromo and interaction; mark the rest.

## Account mode and YouTube Premium

Account mode passes the user's YouTube cookies to yt-dlp, which unlocks Premium formats and account-only videos; it is off by default and toggled per job.

### Cookie sources

- **From a browser**: `--cookies-from-browser <browser>[:<profile>]` with a dropdown for Firefox, Chrome, Edge, Brave and a profile picker. Firefox is the most reliable on Windows; Chrome and Edge encrypt their cookie store and often fail while the browser is open, so show a clear message and suggest the file option.
- **From a file**: `--cookies <path>` pointing at a Netscape-format `cookies.txt` the user exports with a browser extension. The app stores only the path.
- Never copy cookie contents into the database, settings or logs. Mask the cookie path in logs.

### What it unlocks

- **Premium high-bitrate 1080p**: detect it from `format_note` containing "Premium" in the `-J` output (do not hard-code a format id), badge it in the format table, and offer a "Prefer Premium bitrate" preset that falls back to normal best when it is missing.
- Members-only, age-restricted, private and unlisted videos the account can see.
- The account's own playlists, including Watch Later (`https://www.youtube.com/playlist?list=WL`) and Liked videos (`list=LL`).

### Protecting the account

- When account mode is on, add polite pacing by default: `--sleep-requests 1 --sleep-interval 2 --max-sleep-interval 5`.
- Show an "Account mode" indicator in the status bar so it is never on by accident.
- If YouTube returns a bot check or rate limit, pause the queue and tell the user, instead of retrying hard.

## Projects, library and source log

A project is one video being made (for example "COD MW Recap"); everything downloaded or cut belongs to a project and stays traceable to its source.

### Data model

| Entity | Key fields |
| --- | --- |
| Project | id, name, root folder, created, notes, archived |
| Source | id, url, site, video id, title, channel, channel url, upload date, duration, info.json path |
| Asset | id, project id, source id, kind (raw, clip, audio, frame, subtitle, proxy), file path, format, profile, size, created |
| Clip | id, asset id, source id, in, out, handles, name, tags, note, status (Unused, Used, Rejected) |
| Job | id, project id, kind, options JSON, command line, state, progress, error, log path, timestamps |
| WatchedSource | id, url, preset, project id, archive file path, last synced |
| TranscriptCue | source id, language, start, end, text (FTS5) |

### Library view

- Grid or list of assets with thumbnails; filter by project, kind, tag, channel, status; search titles and transcripts.
- Actions: preview, open in clip editor, open in Explorer, copy path, re-download at another quality, delete file.
- Drag any asset straight into DaVinci Resolve's media pool (WPF `DataFormats.FileDrop`).
- Warn before downloading a source and section that the project already has.
- Disk usage per project and a "Delete proxies" button.

### Source log and credits

- One click writes `credits.md` in the project folder and copies it to the clipboard: one line per source used, as `Channel – Title – URL`, ready for the video description.
- Filter to clips marked Used only.
- CSV export of all sources and clips for records.

## Settings, dependencies and updater

YouTube changes often and old yt-dlp builds break, so keeping yt-dlp current is built in, not left to the user.

### First-run wizard

1. Choose the library root folder.
2. Download yt-dlp (latest GitHub release), ffmpeg + ffprobe (a current Windows build) and Deno into the bin folder, with progress; or point to existing copies.
3. Run a self-check: each tool's version, a JS runtime check, and a 5-second test section download.
4. Optionally pick a browser for account mode.

### Dependency manager

- Shows each tool's version and path, and an Update button per tool.
- yt-dlp update via `yt-dlp -U`, with a channel choice: stable (default) or nightly (`--update-to nightly`).
- Check for a yt-dlp update once a day at startup and offer it in the status bar.
- When an error looks like a broken extractor (signature or nsig failures), suggest updating yt-dlp first.

### Settings

- Defaults: preset, container, filename template, concurrency, rate limit, cut mode, handles, editor profile, encoder, subtitle languages, SponsorBlock categories.
- Network: proxy, retries, fragment concurrency.
- Appearance: dark, light or system theme.
- Advanced: an "extra arguments" field appended to every yt-dlp call (with a warning), and the "Show command" button on every job.
- Stored as `settings.json`; export and import buttons.

The app itself updates through Velopack, separate from the tools.

## UI layout and UX rules

One main window with a left navigation rail, a content area and a status bar, designed for a 1920×1080 monitor and usable down to 1280×720.

| Screen | Purpose | Key elements |
| --- | --- | --- |
| Home | Quick grab | URL box, info panel, preset picker, Download / Clip / Add to project buttons |
| Search | Find footage | Search box, filters, results grid with actions |
| Clip editor | Mark and cut | Player, timeline with in/out markers, clip list, batch download button |
| Queue | Watch jobs | Job rows with progress, controls, "when finished" option, log viewer |
| Library | Browse footage | Project picker, asset grid, filters, transcript search, credits export |
| Channels | Watched sources | List of sources, Sync button, last synced time |
| Settings | Configure | Tabs: General, Downloads, Clipping, Account, Tools, Advanced |

The status bar always shows tool health (green when all found and current), queue summary (for example "2 running, 5 queued"), and the account mode indicator.

### UX rules

- Keyboard first: `Ctrl+V` pastes and fetches, `Ctrl+Enter` downloads with the default preset, `Ctrl+K` opens search, clip editor keys as listed in Clipping.
- Drag and drop everywhere: links into the app, files out to Resolve or Explorer.
- Never freeze: long work shows progress and a Cancel button.
- Confirm before deleting files or clearing the queue; everything else is undoable or harmless.
- Toasts for finished jobs and errors; no modal pop-ups for routine events.
- Titles and paths display Hindi and other Unicode text correctly.

## Error handling and edge cases

An `ErrorMapper` in Core turns yt-dlp and ffmpeg output into a plain message plus one suggested action; the raw output stays in the job log.

| Problem | How to detect | App response |
| --- | --- | --- |
| Bot check ("Sign in to confirm you're not a bot") | stderr text | Pause queue, suggest account mode or waiting |
| Rate limited | `HTTP Error 429` | Pause queue for 10 minutes, then resume one job at a time |
| Outdated extractor | signature / nsig extraction errors | Offer one-click yt-dlp update, then retry |
| JS runtime missing | yt-dlp JS runtime warning | Open dependency manager, offer Deno download |
| Format gone | `Requested format is not available` | Re-fetch formats and ask again |
| Private, members-only or age-restricted | `Private video`, `members`, `age` | Suggest account mode |
| DRM-protected | `DRM` in error | Say it is not supported; do not retry |
| ffmpeg missing or failed | not found / non-zero exit | Show tool status; keep the downloaded file |
| Disk full | IO exception / ffmpeg error | Pause queue, show free space |
| Path too long or bad characters | `PathTooLongException` | Use `--trim-filenames` and `--windows-filenames`; suggest enabling Windows long paths |
| Network drop | timeouts | Retry with backoff, resume from `.part` |
| Live stream ended early | process exit while live | Finalise the partial file, mark job Done with a note |
| Preview stream link expired | player error | Re-fetch the stream URL silently |

General rules:

- One log file per job holding the full command line (cookie path masked) and all output, with a "Copy log" button.
- A global exception handler writes a crash log and keeps the queue safe in the database.
- A failed job never blocks the rest of the queue.

## Build phases and acceptance criteria

Phase S sets up the GitHub repository, then seven build phases follow, each usable on its own; the app is useful for real footage work from Phase 1. Tick the boxes as each phase is accepted.

### Phase S: GitHub repository setup (do this first)

Claude Code creates the public repo on the owner's personal account with every file under Open-source release, using the GitHub CLI. The owner signs in himself; Claude Code never handles credentials.

1. Check `git --version` and `gh --version`. If the GitHub CLI is missing, ask the owner to install it (`winget install GitHub.cli`).
2. Run `gh auth status`. If not signed in, stop and ask the owner to run `gh auth login` himself. Never ask for, type, print or store a token or password.
3. Read the username with `gh api user --jq .login`. Check that `<username>/ssnip` does not exist yet; if it does, ask for another name.
4. Before creating anything, confirm with the owner: repo name, public visibility, and that he has checked his employment contract's IP / side-project clause. Creating a public repo needs his explicit yes.
5. Create an empty repo and clone it: `gh repo create ssnip --public --description "<description from Repository settings>" --clone`.
6. Add every file in the Repository files table, the `.gitignore` additions and the README disclaimer word for word. Copy the plan file the owner provided into `docs/PLAN.md`.
7. Commit as `chore: initial repository setup` on `main` and push.
8. Apply settings: `gh repo edit --enable-wiki=false --enable-projects=false --enable-discussions --add-topic yt-dlp,ffmpeg,wpf,dotnet,csharp,windows,video-clipping,content-creation`, then enable private vulnerability reporting with `gh api -X PUT repos/<username>/ssnip/private-vulnerability-reporting`.
9. After the first green CI run in Phase 0, add branch protection on `main` through `gh api` with the rules in Repository settings.

- [ ] Repo is public at `github.com/<username>/ssnip` with the description and topics
- [ ] GitHub shows "MIT license" in the repo sidebar
- [ ] Every file in the Repository files table exists and `git status` is clean
- [ ] `git check-ignore -v cookies.txt test.db clip.mp4` reports all three as ignored
- [ ] No token, password, cookie value or personal file path appears in any committed file
- [ ] README contains the disclaimer word for word

### Phase 0: Skeleton and tools

Solution and projects, DI host, Serilog, settings file, `ProcessRunner`, dependency manager, first-run wizard, navigation shell with empty screens.

- [ ] App starts, shows the navigation rail and status bar in dark theme
- [ ] First run downloads yt-dlp, ffmpeg, ffprobe and Deno, and shows their versions
- [ ] `ProcessRunner` runs `yt-dlp --version` and streams output; cancel kills it
- [ ] CI passes on GitHub, branch protection is on for main, and the unit test project runs with `dotnet test`

### Phase 1: Fetch and single download

URL input, `-J` info panel, presets, advanced format table, `YtDlpArgsBuilder`, progress parsing, cancel, Show command, open folder.

- [ ] A public YouTube video downloads at Editor-safe 1080p with a live progress bar, speed and ETA
- [ ] Advanced mode lists all formats and downloads a chosen video + audio pair
- [ ] Cancel stops yt-dlp and ffmpeg and leaves no zombie processes
- [ ] A video with a Hindi title saves with the correct filename
- [ ] Show command displays the exact arguments used

### Phase 2: Queue, bulk and extras

SQLite, persistent queue, concurrency, pause/resume/retry, toasts, taskbar progress, when-finished option, playlists and channels with selection and filters, audio extraction, subtitles, metadata, SponsorBlock, live recording.

- [ ] 10 jobs queued, 2 run at a time, and the queue survives closing and reopening the app
- [ ] Select 5 items from a playlist and download only those
- [ ] Audio-only WAV, SRT subtitles and SponsorBlock removal all work on one test video
- [ ] "Sleep when finished" puts the PC to sleep after the last job

### Phase 3: Clipping

Proxy download, clip editor with player and keys, section downloads with fast/accurate modes and handles, chapter clips, timestamp import, local file clipping, frame grabs, editor profiles with encoder detection.

- [ ] Mark 3 clips on a proxy and batch-download them at full quality; each starts and ends within 0.1 s of the marks in Accurate mode
- [ ] Pasted timestamp lines become clips with names
- [ ] H.264 profile uses `h264_amf` when available and the output imports into DaVinci Resolve and scrubs smoothly
- [ ] Frame grab saves a full-resolution PNG

### Phase 4: Projects and library

Project entity and folders, assets and clips in the database, library view, drag to Resolve, duplicate warning, credits export, disk usage.

- [ ] Every download and clip appears in its project with source and timestamps
- [ ] Dragging a clip into Resolve's media pool imports it
- [ ] `credits.md` lists every used source as Channel – Title – URL

### Phase 5: Search, transcripts and account

Search screen, subtitle indexing into FTS5, transcript search to clip, account mode with browser or file cookies, Premium format detection, pacing, watched channels with archive sync.

- [ ] Searching a spoken line across a project returns the video and timestamp, and "Make clip" creates the clip
- [ ] With account mode on, a Premium 1080p format is detected and badged
- [ ] Channel sync downloads only uploads not already in the archive

### Phase 6: Polish and packaging

Full error mapping, per-job logs, crash handler, shortcuts, settings import/export, light theme, Velopack installer and updates, daily yt-dlp update check.

- [ ] Each error in the Error handling table shows its message and action
- [ ] Installer installs, the app updates itself from a new release, and uninstall removes it cleanly
- [ ] A full day of real use (sourcing footage for one video) runs without a crash

## Open-source release

SSnip is a public, MIT-licensed repo on the owner's personal GitHub account; these rules keep it clear of licensing, takedown and employer problems.

### Repository

- Repo: `ssnip` on the owner's personal account, created by Claude Code in Phase S. Phase S adds `LICENSE`, `README.md`, `THIRD_PARTY_NOTICES.md`, `.gitignore`, `CONTRIBUTING.md`, `SECURITY.md`, issue and pull request templates, and this plan as `docs/PLAN.md`.
- Claude Code builds only inside this repo. Never delete or rewrite those files without asking; update `THIRD_PARTY_NOTICES.md` whenever a package is added.
- Built on a personal laptop, on personal time, with a personal GitHub account. No employer code, data, tools or credentials ever go in. Check the employment contract's IP / side-project clause before the first public release.

### Repository settings

- Name: `ssnip`. Visibility: public. Default branch: `main`.
- Description: `Footage and clip tool for creators: a Windows app for yt-dlp and FFmpeg with clipping, transcript search and editor-ready output.`
- Topics: `yt-dlp`, `ffmpeg`, `wpf`, `dotnet`, `csharp`, `windows`, `video-clipping`, `content-creation`. Do not add topics such as `youtube-downloader`.
- Features: Issues on, Discussions on, Wiki off, Projects off, private vulnerability reporting on.
- Branch protection on `main` (added once CI exists): pull request required, CI must pass, no force pushes.

### Repository files

| File | Contents |
| --- | --- |
| `LICENSE` | MIT License, `Copyright (c) 2026 Saksham Singh` |
| `README.md` | Name and one-line tagline, screenshot placeholder, features, requirements (Windows 10/11 x64), install, quick start, how it works (wraps yt-dlp, FFmpeg and Deno), the disclaimer below word for word, credits, contributing, license |
| `THIRD_PARTY_NOTICES.md` | One entry per dependency: name, version, license, project URL, how it is used (downloaded at runtime or NuGet). Full LGPL 2.1 text in `licenses/LGPL-2.1.txt` |
| `.gitignore` | GitHub's VisualStudio template plus the additions below |
| `.gitattributes` | `* text=auto`, `*.sln text eol=crlf`, binary rules for images |
| `.editorconfig` | C# conventions: 4-space indent, file-scoped namespaces, `var` when type is obvious, nullable warnings as errors |
| `global.json` | Pins the .NET 10 SDK with `rollForward: latestFeature` |
| `Directory.Build.props` | `Nullable` enabled, `TreatWarningsAsErrors` true, `LangVersion` latest, shared version number |
| `CONTRIBUTING.md` | How to build and test, branch and commit style (Conventional Commits), tests required for Core changes, no copyrighted URLs in tests or docs, contributions accepted under MIT |
| `CODE_OF_CONDUCT.md` | Contributor Covenant 2.1, contact via GitHub private reporting |
| `SECURITY.md` | Report privately through GitHub's private vulnerability reporting; never include cookies, tokens or personal URLs |
| `.github/ISSUE_TEMPLATE/bug_report.yml` | Fields: app version, yt-dlp version, Windows version, site, steps, expected vs actual, log. A required checkbox "I updated yt-dlp" and a bold warning: never paste cookies or account details |
| `.github/ISSUE_TEMPLATE/feature_request.yml` | Problem, proposed solution, alternatives |
| `.github/ISSUE_TEMPLATE/config.yml` | Blank issues off; link to Discussions for questions |
| `.github/PULL_REQUEST_TEMPLATE.md` | Checklist: tests added, `dotnet test` passes, no secrets or media committed, `THIRD_PARTY_NOTICES.md` updated if a package was added |
| `.github/dependabot.yml` | Weekly updates for `nuget` and `github-actions` |
| `.github/workflows/ci.yml` | On push and pull request: `windows-latest`, `actions/setup-dotnet` for .NET 10, restore, build Release, run unit tests only |
| `docs/PLAN.md` | This plan, exported as Markdown |

The release workflow (`.github/workflows/release.yml`: tag `v*` → publish win-x64 → Velopack pack → SHA-256 checksums → GitHub release) is added in Phase 6, not now.

### .gitignore additions

```
# Secrets and account data
cookies*.txt
*.cookies
.env
*.pfx

# Local app data
*.db
*.db-wal
*.db-shm
logs/
settings.local.json

# Downloaded tools
tools/bin/

# Media and partial downloads
*.mp4
*.mkv
*.webm
*.mov
*.m4a
*.mp3
*.wav
*.opus
*.part
*.ytdl
```

### README disclaimer (copy word for word)

```markdown
## Disclaimer

SSnip is a desktop front end for yt-dlp and FFmpeg. It does not host, store or distribute any content.

You are responsible for how you use it. Follow the terms of service of every site you use and the copyright law of your country. Only download content you own, have permission to use, or that is licensed for reuse.

SSnip does not bypass DRM or paywalls. It is not affiliated with or endorsed by YouTube, Google, or the yt-dlp or FFmpeg projects.
```

### Licensing

| Component | License | How the app uses it |
| --- | --- | --- |
| SSnip code | MIT | `LICENSE` in the repo root |
| yt-dlp | Unlicense | Downloaded at first run, not redistributed; credited |
| FFmpeg / ffprobe | LGPL 2.1+ or GPL, depending on the build | Downloaded at first run from official build sites, not redistributed; credited |
| Deno | MIT | Downloaded at first run; credited |
| libVLC + LibVLCSharp | LGPL 2.1+ | NuGet packages, dynamically linked; license text in notices |
| CommunityToolkit.Mvvm, WPF-UI, CliWrap, EF Core, Velopack | MIT | NuGet packages; listed in notices |
| Serilog | Apache 2.0 | NuGet package; listed in notices |

If a release ever bundles FFmpeg, use an LGPL build and ship its license and source link with it.

### Takedown-safe rules

- No copyrighted music videos, films or other creators' videos in the README, screenshots, docs or tests. Use the owner's own videos or Creative Commons videos.
- No "YouTube" in the app name, icon or logo. Mention it only descriptively: "works with sites supported by yt-dlp, including YouTube".
- Describe the app as a footage and clipping tool for creators. Never advertise it as a way to rip videos, bypass Premium or avoid paying for content.
- No DRM circumvention, ever. Account mode is described neutrally: it uses the user's own browser cookies.
- Never commit cookies, tokens, databases, logs or downloaded media; `.gitignore` covers them and the bug report template warns users not to paste cookies.
- The README carries a disclaimer: users are responsible for following each site's terms and copyright law.

### Releases

- Build releases with GitHub Actions from tagged commits, attach SHA-256 checksums, and write release notes.
- Later, apply to SignPath's free open-source code signing to remove the SmartScreen warning.

### Sharing on Reddit

- Candidate subreddits: r/csharp, r/dotnet, r/opensource, r/DataHoarder, r/SideProject, r/youtubedl. Read each one's self-promotion rules before posting.
- Lead with what is different: transcript search to clip, proxy-first clipping, and DaVinci Resolve-ready output.

## Testing, packaging and scope

### Testing

- **Unit tests (Core, no network)**: `YtDlpArgsBuilder` for every option and preset, progress JSON parser with recorded sample lines, `ErrorMapper` with recorded stderr samples, timestamp-line parser, VTT parser, filename template preview.
- **Integration tests (opt-in, real tools and network)**: info fetch, a 5-second section download, audio extraction, subtitle download, editor-profile conversion. Mark them with a trait so they never run by default.
- **Manual checklist per phase**: the acceptance boxes above, run on the real PC.

### Packaging

- `dotnet publish -c Release -r win-x64 --self-contained` wrapped by Velopack for the installer and app updates.
- Tools are downloaded on first run, not bundled, so the installer stays small and yt-dlp can update itself.
- No code signing for personal use; expect a SmartScreen prompt on first install.

### Out of scope

- A timeline editor, effects or rendering (Resolve does this).
- Getting around DRM or paywalls.
- Uploading to YouTube, cloud sync, multiple users.
- macOS and Linux (WPF is Windows-only).

### Usage note

Downloading from YouTube is against its terms of service, Premium included, and footage from other creators can trigger Content ID claims. Use clips briefly and transform them (commentary, recap, review), and keep the source log so credits are always ready.

### Sources

- [SUSE Package Hub – yt-dlp 2026.01.31 changelog](https://packagehub.suse.com/packages/yt-dlp/2026_01_31-bp160_1_1) (JS runtime requirement, Python 3.10 minimum)
- [pkgsrc – net/yt-dlp](https://backiee.wasmer.app/https_pkgsrc_se/net/yt-dlp) (release cadence)
