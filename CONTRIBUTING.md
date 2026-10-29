# Contributing to SSnip

Thanks for your interest in SSnip. This guide covers how to build, test and send changes.

## Build and test

Requirements: Windows 10/11 x64 and the .NET 10 SDK (pinned in `global.json`).

```powershell
dotnet restore
dotnet build -c Release
dotnet test
```

`dotnet test` runs unit tests only. Integration tests use real tools and the network and are opt-in; they are marked with a trait so they never run by default.

To fetch yt-dlp, FFmpeg and Deno for local development, run `tools/fetch-binaries.ps1`. Downloaded binaries go to `tools/bin/`, which is git-ignored.

## Branches and commits

- Branch from `main` and open a pull request; `main` is protected.
- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`, `ci:`.
- Keep each pull request focused on one change.

## Tests

- Every change to `SSnip.Core` needs unit tests (xUnit + Shouldly).
- Unit tests must not use the network or external tools.

## Content rules

- Never use copyrighted URLs (music videos, films, other creators' videos) in tests, docs or screenshots. Use your own videos or Creative Commons content.
- Never commit cookies, tokens, databases, logs or downloaded media.
- Update `THIRD_PARTY_NOTICES.md` when you add a package.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
