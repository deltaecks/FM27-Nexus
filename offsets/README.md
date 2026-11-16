# Offset / AOB schemas

Place build-verified memory profiles here as:

```text
offsets/<build_id>_<storefront>.json
```

Examples of storefront keys: `steam`, `epic`, `gamepass_pc`, `gamepass_console`.

**Do not invent offsets.** Milestone 4 loads these files inside `FM27Nexus.MemoryAgent` after AOB signature and numeric sanity checks. Community packs imported by users are stored under `%AppData%\FM27Nexus\Offsets\`.
