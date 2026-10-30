# Third-party notices

SSnip uses the following third-party software. Each component remains under its own license. This file is updated whenever a dependency is added.

## External tools (downloaded at runtime, not redistributed)

These tools are downloaded by SSnip on first run from their official sources into `%LOCALAPPDATA%\SSnip\bin\`. SSnip runs them as separate processes and does not redistribute them.

| Name | Version | License | Project URL | How it is used |
| --- | --- | --- | --- | --- |
| yt-dlp | Latest release at first run | Unlicense | https://github.com/yt-dlp/yt-dlp | Downloaded at runtime; fetches info, formats, subtitles and downloads |
| FFmpeg / ffprobe | Current gyan.dev "release essentials" build at first run | GPL v3 (this build includes libx264); FFmpeg itself is LGPL 2.1+ | https://ffmpeg.org/ (build: https://www.gyan.dev/ffmpeg/builds/) | Downloaded at runtime; cuts, converts and probes media |
| Deno | Latest release at first run | MIT | https://deno.com/ | Downloaded at runtime; JavaScript runtime used by yt-dlp |

If a release ever bundles FFmpeg, it will use an LGPL build and ship its license and a source link with it.

## NuGet packages

Versions are filled in when each package is added to the solution.

| Name | Version | License | Project URL | How it is used |
| --- | --- | --- | --- | --- |
| LibVLCSharp.WPF | Not yet added | LGPL 2.1+ | https://github.com/videolan/libvlcsharp | NuGet, dynamically linked; preview player |
| VideoLAN.LibVLC.Windows | Not yet added | LGPL 2.1+ | https://www.videolan.org/vlc/libvlc.html | NuGet, dynamically linked; native player library |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet | NuGet; MVVM source generators |
| WPF-UI (with Wpf.Ui.Abstractions) | 4.3.0 | MIT | https://github.com/lepoco/wpfui | NuGet; Fluent styling and navigation |
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | https://github.com/dotnet/runtime | NuGet; DI, options, logging host |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | MIT | https://github.com/dotnet/runtime | NuGet; service registration in Infrastructure |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | MIT | https://github.com/dotnet/runtime | NuGet; logging interfaces in Infrastructure |
| CliWrap | 3.10.5 | MIT | https://github.com/Tyrrrz/CliWrap | NuGet; process execution |
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 | MIT | https://github.com/CommunityToolkit/WindowsCommunityToolkit | NuGet; Windows toast notifications |
| Microsoft.Windows.SDK.NET (Windows SDK projection) | Comes with the net10.0-windows10.0.19041.0 target | MIT | https://github.com/microsoft/CsWinRT | .NET SDK; Windows Runtime APIs used by toasts |
| Microsoft.EntityFrameworkCore.Sqlite (with Microsoft.Data.Sqlite) | 10.0.14 | MIT | https://github.com/dotnet/efcore | NuGet; SQLite storage for the queue and library |
| Microsoft.EntityFrameworkCore.Design | 10.0.14 | MIT | https://github.com/dotnet/efcore | NuGet, build time only (not shipped); creates migrations |
| SQLitePCLRaw (bundle_e_sqlite3) | 2.1.13 | Apache 2.0 | https://github.com/ericsink/SQLitePCL.raw | NuGet, via EF Core; native SQLite binding |
| SQLite | Bundled with SQLitePCLRaw | Public domain | https://sqlite.org/ | Native database engine |
| dotnet-ef | 10.0.14 | MIT | https://github.com/dotnet/efcore | .NET local tool, development only; runs migrations |
| Serilog | 4.4.0 | Apache 2.0 | https://github.com/serilog/serilog | NuGet; logging |
| Serilog.Extensions.Hosting | 10.0.0 | Apache 2.0 | https://github.com/serilog/serilog-extensions-hosting | NuGet; plugs Serilog into the host |
| Serilog.Sinks.File | 7.0.0 | Apache 2.0 | https://github.com/serilog/serilog-sinks-file | NuGet; rolling log files |
| Velopack | Not yet added | MIT | https://github.com/velopack/velopack | NuGet; installer and self-update |
| xUnit (xunit, xunit.runner.visualstudio) | 2.9.3, 3.1.4 | Apache 2.0 | https://github.com/xunit/xunit | NuGet, tests only |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | https://github.com/microsoft/vstest | NuGet, tests only |
| Shouldly | 4.3.0 | BSD 3-Clause | https://github.com/shouldly/shouldly | NuGet, tests only |

## License texts

- GNU Lesser General Public License 2.1: [licenses/LGPL-2.1.txt](licenses/LGPL-2.1.txt)
