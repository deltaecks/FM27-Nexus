# Offset / AOB schemas

Place **build-verified** memory profiles here (or import into `%AppData%\FM27Nexus\Offsets\`):

```text
offsets/<build_id>_<storefront>.json
```

Storefront keys: `steam`, `epic`, `gamepass_pc`, `gamepass_console`.

See `schema.example.json` for the JSON shape only. **Do not invent AOB bytes or field offsets.**  
`FM27Nexus.MemoryAgent` refuses writes until:

1. A real profile loads for the running build + storefront
2. AOB signatures resolve in the process image
3. `ValidateSanity` passes numeric bounds in read-only mode
4. A timestamped `.fm` vault backup exists for the session

Community packs: use **Live Editor → Import Pack…** in the app.
