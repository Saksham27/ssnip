# SSnip

**Footage and clip tool for creators.** Turn "I need that scene for my video" into an editor-ready clip in under a minute.

> 📸 Screenshot coming soon. SSnip is in early development.

## Features

- **Fetch and download**: paste one or many links from any site supported by yt-dlp, including YouTube; pick a quality preset or exact formats.
- **Proxy-first clipping**: scrub a fast low-res preview, mark in/out points, then download only those sections at full quality.
- **Editor-ready output**: convert clips to H.264 MP4, DNxHR or ProRes so they import into DaVinci Resolve and scrub smoothly.
- **Transcript search**: index subtitles and search what is said across a project, then make a clip around the line.
- **Persistent queue**: line up jobs, run them overnight, resume after a restart.
- **Projects and source log**: every clip stays traceable to its original URL and channel; export `credits.md` in one click.
- **Extras**: audio extraction, subtitles, metadata, chapters, SponsorBlock, playlists and channel sync.

## Requirements

- Windows 10 or Windows 11, x64

## Install

Installers will be published on the [Releases](https://github.com/Saksham27/ssnip/releases) page. On first run, SSnip downloads yt-dlp, FFmpeg and Deno into its own folder.

## Quick start

1. Start SSnip and choose a library folder for your footage.
2. Let the first-run wizard download the tools.
3. Paste a link, pick a preset, and press **Download**, or open it in the clip editor to mark clips.

## How it works

SSnip is a WPF app on .NET 10. It does not reimplement any downloading or video processing; it builds command lines for and runs:

- [yt-dlp](https://github.com/yt-dlp/yt-dlp) to fetch info, formats, subtitles and downloads
- [FFmpeg](https://ffmpeg.org/) to cut, convert and extract
- [Deno](https://deno.com/) as the JavaScript runtime yt-dlp needs for some sites

## Disclaimer

SSnip is a desktop front end for yt-dlp and FFmpeg. It does not host, store or distribute any content.

You are responsible for how you use it. Follow the terms of service of every site you use and the copyright law of your country. Only download content you own, have permission to use, or that is licensed for reuse.

SSnip does not bypass DRM or paywalls. It is not affiliated with or endorsed by YouTube, Google, or the yt-dlp or FFmpeg projects.

## Credits

SSnip stands on the work of the yt-dlp, FFmpeg, Deno, libVLC and .NET communities. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for every dependency and its license.

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md) first. Report security issues as described in [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE) © 2026 Saksham Singh
