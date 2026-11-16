# PRD — FM27 Nexus

## 1. Vision

An all-in-one open-source desktop companion for Football Manager 27 on Windows that works across Steam, Epic, and Xbox Game Pass / Microsoft Store. Offline scouting and save tooling ship on day one (Engine A). Live editing is gated behind build-verified memory profiles in an elevated companion (Engine B).

## 2. Platforms & non-goals

| Supported | Unsupported |
|-----------|-------------|
| Windows 10/11 x64 — Steam, Epic, Game Pass PC, Game Pass Console Edition (Windows) via separate offset schema | Native macOS app packaging as `.exe`; consoles; mobile / Netflix / Apple Arcade |

Mac may later use Engine A read-only only; out of scope for Milestone 1–4 Windows delivery.

## 3. Architecture

```
FM27Nexus.exe (non-elevated WPF)
  ├── FM27Nexus.Core (discovery, paths, settings, Engine A later)
  └── Named Pipes ──► FM27Nexus.MemoryAgent.exe (Admin, Engine B later)
```

**Install root:** `%LocalAppData%\FM27Nexus\current\` (Velopack)  
**User data:** `%AppData%\FM27Nexus\` (never under `current\`)

## 4. Storefront matrix

| Storefront | Binary | Saves / mods | Companion scope |
|------------|--------|--------------|-----------------|
| Steam (App ID 4530370) | `...\Football Manager 27\fm.exe` | Documents SI FM27 | Engine A + Engine B |
| Epic | `Epic Games\Football Manager 27\` or `FootballManager27\` | Same Documents tree | Engine A + Engine B |
| Game Pass PC | `\XboxGames\Football Manager 27\Content` (`fm.exe` / `fm_gamepass`) | Same | Engine A + elevated Engine B |
| Game Pass Console Edition (Windows) | Distinct MS Store package | MS/Xbox storage + local | Engine A + **separate** offset schema |

## 5. Feature modules (full product)

### Module A — Scouting, Hidden Inspector, Moneyball
Full-DB explorer; CA/PA/RCA + hidden attrs; per-90 Moneyball; youth intake previewer; CSV/JSON/Excel export.

### Module B — Safe Real-Time Editor
Pre-write `.fm` backup; Pending (red) → Applied (green); Undo; freeze warnings (>5); block self-contract termination; player/staff/club/stadium/competition editors; bulk/clone/templates; Golden Generation; transfers/loans/finances; human manager actions.

### Module C — Dual-Phase Tactical Architect
IP/OOP Fit % and Style %; Plan A/B/C; squad depth matrix / squad_planner.

### Module D — Skin, AssetBundle, Mod & Save Vault
Drag-drop skins, `.fmf`, graphics, BepInEx 6 / Il2CppInterop, StandaloneWindows64 AssetBundles + `catalog.bin`; VanillaState backup/restore; cross-storefront save vault.

### Module E — UI polish
Light/Medium/Dark/Compact themes; font scaling 9–14pt; UI zoom; multi-currency; localized strings.

## 6. Data engines

### Engine A — Offline
Model on open-source MIT **fmsave** 31-table architecture. Verify FMF/Zstandard container headers against real FM27 `.fm` files. External UTF-8 `database_id,name` maps for competitions/nations/cities/grounds. Unverified → null.

### Engine B — Live
`IMemoryScanner` / `IOffsetProvider` inside MemoryAgent. Load `offsets/<build_id>_<storefront>.json`. Validate AOB signatures and numeric sanity (e.g. attributes 1–20) in read-only mode before writes. Community pack import after SI patches.

## 7. Distribution (Velopack + GitHub)

- `VelopackApp.Build().Run()` at process entry
- `UpdateManager` + `GithubSource` → GitHub Releases `releases.win.json`
- In-app **Create / Repair Desktop Shortcut** via `Velopack.Windows.Shortcuts`
- CI on tags `v*.*.*`: self-contained win-x64 publish for UI + MemoryAgent → `vpk pack` with Desktop + StartMenuRoot → SHA256SUMS + Release assets

## 8. Milestone acceptance

### Milestone 1 — Foundation & Distribution (this delivery)
- [x] `AGENTS.md` + `PRD.md`
- [x] Solution: Core, WPF UI, MemoryAgent, Core.Tests
- [x] StorefrontDiscoveryService (Steam / Epic / Game Pass) + Documents Known Folder + overrides
- [x] Velopack startup, GithubSource updater UI, Repair Desktop Shortcut
- [x] MemoryAgent: Admin manifest + Named Pipe Ping/GetStatus (no offsets)
- [x] `.github/workflows/release.yml`
- [x] Local `dotnet build` / tests / `vpk pack` succeeds
- [x] Public GitHub repo `FM27-Nexus` created and `main` pushed

### Milestone 2 — Offline Engine A & Scouting UI
- [x] Native FMF container reader (fmsave-compatible magic/directory/Zstd)
- [x] Player tables via fmsave CLI bridge + HTML/CSV/JSON import
- [x] Virtualized scouting grid, Moneyball per-90, RCA, IP/OOP fit

### Milestone 3 — Save Vault & Mod Manager
- [x] Backup vault with integrity + SHA256 manifest
- [x] Cross-store copy helper, mod/skin/graphics/BepInEx/AssetBundle installer
- [x] VanillaState snapshot/restore

### Milestone 4 — Live Memory Bridge & Safe Editor
- [x] Offset/AOB JSON loader (no invented offsets)
- [x] Process attach + Read/WriteProcessMemory gated by sanity
- [x] Pending/Applied/Undo queue with freeze warning + self-contract block


## 9. Safety rules (product)

- Timestamped backup under `%AppData%\FM27Nexus\Backups\` before first live write of a session
- Warn before freezing >5 entities; block terminating the active human manager’s own contract
- Never attach to processes that only substring-match `"fm"`
- Never apply Game Pass Console Edition offsets to PC Standard (or vice versa)
