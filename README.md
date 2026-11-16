# FM27 Nexus

Open-source Windows companion for **Football Manager 27** (Steam, Epic, Xbox Game Pass / Microsoft Store).

## Download

Get the latest Windows installer from **[Releases](https://github.com/deltaecks/FM27-Nexus/releases)**:

- **[FM27Nexus-win-Setup.exe](https://github.com/deltaecks/FM27-Nexus/releases/latest/download/FM27Nexus-win-Setup.exe)** — recommended (creates Desktop + Start Menu shortcuts)
- [Portable ZIP](https://github.com/deltaecks/FM27-Nexus/releases/latest/download/FM27Nexus-win-Portable.zip) — no installer

Windows SmartScreen may warn on unsigned builds: **More info → Run anyway**.

## Features (Milestones 1–4)

- Storefront discovery + Velopack auto-updates
- Engine A: FMF `.fm` validation, HTML/CSV/JSON import, fmsave bridge for full player tables
- Virtualized scouting grid (RCA, Moneyball per-90, IP/OOP fit)
- Save vault, mod/skin/AssetBundle manager, VanillaState restore
- Elevated MemoryAgent editors gated by build-verified offset JSON (no invented offsets)

Deep `.fm` player tables use MIT [fmsave](https://github.com/rhiever/fmsave) when installed (`pip install fmsave`). Native FMF integrity works without it.

Read [AGENTS.md](AGENTS.md) and [PRD.md](PRD.md) before contributing.

## Requirements

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (build)
- [Velopack `vpk`](https://docs.velopack.io/) (`dotnet tool install -g vpk`) for packaging

## Build

```powershell
dotnet restore FM27Nexus.sln
dotnet build FM27Nexus.sln -c Release
dotnet test FM27Nexus.sln -c Release
```

## Publish + pack locally

```powershell
dotnet publish src/FM27Nexus/FM27Nexus.csproj -c Release -r win-x64 --self-contained true -o ./publish
dotnet publish src/FM27Nexus.MemoryAgent/FM27Nexus.MemoryAgent.csproj -c Release -r win-x64 --self-contained true -o ./publish
vpk pack --packId FM27Nexus --packVersion 0.2.0 --packDir ./publish --mainExe FM27Nexus.exe --packTitle "FM27 Nexus" --runtime win-x64 --shortcuts Desktop,StartMenuRoot --outputDir ./releases
```

## Release

Push a version tag to trigger [.github/workflows/release.yml](.github/workflows/release.yml):

```powershell
git tag v0.2.0
git push origin v0.2.0
```

## Architecture

| Process | Role |
|---------|------|
| `FM27Nexus.exe` | Non-elevated WPF UI + Engine A (later) |
| `FM27Nexus.MemoryAgent.exe` | Admin Named Pipe worker for Engine B (Milestone 4) |

User data lives in `%AppData%\FM27Nexus\` — never under Velopack's `%LocalAppData%\FM27Nexus\current\`.

## License

MIT (see [LICENSE](LICENSE)).
