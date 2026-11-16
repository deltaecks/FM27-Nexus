# AGENTS.md — FM27 Nexus

Read this file before writing or modifying any code in this repository.

## Product

**FM27 Nexus** is an open-source Windows desktop companion for Football Manager 27 (Windows 10/11 x64). It supports Steam, Epic Games, and Xbox Game Pass / Microsoft Store editions. Distribution is via GitHub Releases as a Velopack standalone installer with Desktop + Start Menu shortcuts and delta auto-updates.

## Dual-engine architecture (mandatory)

| Engine | Role | Privilege |
|--------|------|-----------|
| **Engine A** | Offline `.fm` save parser + HTML/CSV importer | Non-elevated UI process |
| **Engine B** | Live memory bridge via versioned JSON offset/AOB schemas | Elevated `FM27Nexus.MemoryAgent.exe` only |

### Anti-hallucination rules

1. **Never invent memory offsets, vtables, strides, or AOBs.** FM27 (Unity 6000.3.18f1 IL2CPP) shifts addresses every patch. Offsets live only in build-verified files under `offsets/<build_id>_<storefront>.json`, loaded and signature-validated before any write.
2. **Never emit stubs:** no `TODO`, `NotImplementedException`, hardcoded dummy squads, or fake success states.
3. **Unreadable / unconfirmed fields** must be assigned `null` and flagged `Unverified` — never guessed.
4. **Milestone discipline:** implement and verify one milestone at a time. Do not collapse Milestones 2–4 into a single pass.

## Privilege separation

- **`FM27Nexus.exe`** — non-elevated, installed under `%LocalAppData%\FM27Nexus\current\` (Velopack-managed).
- **`FM27Nexus.MemoryAgent.exe`** — `requireAdministrator` manifest. Launched via UAC only when the user clicks **Attach to Live Game**. Talks to the UI over local Named Pipes (`FM27Nexus.MemoryAgent`).
- Velopack updates must remain silent and UAC-free; never elevate the main UI for updates.

## User data persistence

Velopack **replaces** `%LocalAppData%\FM27Nexus\current\` on every update.

**Store all persistent user files under `%AppData%\FM27Nexus\`:**

| Path | Contents |
|------|----------|
| `%AppData%\FM27Nexus\Settings\` | `settings.json`, path overrides |
| `%AppData%\FM27Nexus\Backups\` | Timestamped `.fm` backups |
| `%AppData%\FM27Nexus\Offsets\` | Imported community offset packs |
| `%AppData%\FM27Nexus\Templates\` | Player templates |
| `%AppData%\FM27Nexus\Logs\` | Application logs |
| `%AppData%\FM27Nexus\VanillaState\` | Pre-mod AssetBundle snapshots |

## Storefront & process safety

- Steam App ID: **4530370** → `...\Football Manager 27\fm.exe`
- Epic: `Epic Games\Football Manager 27\` **or** `FootballManager27\` under Program Files / Program Files (x86)
- Game Pass: `\XboxGames\Football Manager 27\Content` (`fm.exe` / `fm_gamepass`); distinguish **PC Standard** vs **Console Edition (Windows)** — never load mismatched offset schemas
- Documents: resolve Known Folder dynamically (OneDrive-safe); support FM Preferences path override
- Process attach: match **exact** names `fm.exe` / `fm_gamepass` **and** verify executable path under a discovered install root — never substring-match `"fm"`

## Milestone gates

| Milestone | Scope | Gate before advancing |
|-----------|-------|------------------------|
| **1** | Docs, solution, discovery, Velopack, MemoryAgent Ping/Status, release.yml | `dotnet build` + tests pass; `vpk pack` produces Setup.exe |
| **2** | Engine A `.fm` reader, HTML/CSV import, Scouting Grid, Moneyball, IP/OOP fit | Parse real `.fm` against fmsave-style tables; unverified fields null |
| **3** | Save Vault, mod/skin/AssetBundle manager, VanillaState | Install/restore round-trip on real directories |
| **4** | Live MemoryAgent editors, offset JSON loader, Pending/Applied/Undo | Read-only verified first; no writes without matching schema |

### Milestone 1 MemoryAgent contract

Allowed now: Named Pipe `Ping` / `GetStatus` with real elevation and process discovery state.

**Forbidden until Milestone 4:** offset loading, `ReadProcessMemory` / `WriteProcessMemory`, attribute editors.

## Distribution

- Velopack pack id: `FM27Nexus`
- Main exe: `FM27Nexus.exe`
- Shortcuts: Desktop, StartMenuRoot (`--packTitle "FM27 Nexus"`)
- Updates: `GithubSource("https://github.com/deltaecks/FM27-Nexus", null, false)`

- Release workflow: tag `v*.*.*` → publish win-x64 → `vpk pack` → upload Setup.exe, nupkgs, `releases.win.json`, `SHA256SUMS.txt`

## Coding standards

- C# / .NET 8, WPF + MVVM (CommunityToolkit.Mvvm)
- Prefer small, focused services in `FM27Nexus.Core`
- Unit-test discovery and path helpers with fixtures
- Do not commit secrets, personal save files, or fabricated offset JSON

## How to continue in a fresh chat

1. Read `@AGENTS.md` and `@PRD.md`
2. Confirm the previous milestone gate passed
3. Implement **only** the next milestone
4. Build, test, and stop
