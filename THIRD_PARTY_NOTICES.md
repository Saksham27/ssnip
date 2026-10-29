# Third-party notices

SSnip uses the following third-party software. Each component remains under its own license. This file is updated whenever a dependency is added.

## External tools (downloaded at runtime, not redistributed)

These tools are downloaded by SSnip on first run from their official sources into `%LOCALAPPDATA%\SSnip\bin\`. SSnip runs them as separate processes and does not redistribute them.

| Name | Version | License | Project URL | How it is used |
| --- | --- | --- | --- | --- |
| yt-dlp | Latest release at first run | Unlicense | https://github.com/yt-dlp/yt-dlp | Downloaded at runtime; fetches info, formats, subtitles and downloads |
| FFmpeg / ffprobe | Current Windows build at first run | LGPL 2.1+ or GPL, depending on the build | https://ffmpeg.org/ | Downloaded at runtime; cuts, converts and probes media |
| Deno | Latest release at first run | MIT | https://deno.com/ | Downloaded at runtime; JavaScript runtime used by yt-dlp |

If a release ever bundles FFmpeg, it will use an LGPL build and ship its license and a source link with it.

## NuGet packages

Versions are filled in when each package is added to the solution.

| Name | Version | License | Project URL | How it is used |
| --- | --- | --- | --- | --- |
| LibVLCSharp.WPF | Not yet added | LGPL 2.1+ | https://github.com/videolan/libvlcsharp | NuGet, dynamically linked; preview player |
| VideoLAN.LibVLC.Windows | Not yet added | LGPL 2.1+ | https://www.videolan.org/vlc/libvlc.html | NuGet, dynamically linked; native player library |
| CommunityToolkit.Mvvm | Not yet added | MIT | https://github.com/CommunityToolkit/dotnet | NuGet; MVVM source generators |
| WPF-UI | Not yet added | MIT | https://github.com/lepoco/wpfui | NuGet; Fluent styling |
| Microsoft.Extensions.Hosting | Not yet added | MIT | https://github.com/dotnet/runtime | NuGet; DI, options, logging host |
| CliWrap | Not yet added | MIT | https://github.com/Tyrrrz/CliWrap | NuGet; process execution |
| Microsoft.EntityFrameworkCore.Sqlite | Not yet added | MIT | https://github.com/dotnet/efcore | NuGet; SQLite storage |
| Serilog | Not yet added | Apache 2.0 | https://github.com/serilog/serilog | NuGet; logging |
| Velopack | Not yet added | MIT | https://github.com/velopack/velopack | NuGet; installer and self-update |
| xUnit | Not yet added | Apache 2.0 | https://github.com/xunit/xunit | NuGet, tests only |
| Shouldly | Not yet added | BSD 3-Clause | https://github.com/shouldly/shouldly | NuGet, tests only |

## License texts

- GNU Lesser General Public License 2.1: [licenses/LGPL-2.1.txt](licenses/LGPL-2.1.txt)
