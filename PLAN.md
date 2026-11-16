# Cmod: Plan

A GMod-style sandbox in Unity. Friends write mods with AI (mainly Claude), keep them on their own GitHub repos, and live-import them into a shared peer-to-peer session.

## Decisions

| Topic | Choice | Why |
|---|---|---|
| Engine | Unity (Mono backend, PC) | It's what the project wanted |
| Mod language | **Lua** (MoonSharp), with an API modelled on GMod's | Claude already knows GMod Lua (SWEPs, ENTs, hooks, `net`) from a huge amount of training data. If we copy those patterns, Claude can write weapons with very little guidance. Lua scripts are plain text, so they're easy to sync and hot-reload. |
| Networking | **FishNet** | Free, actively maintained, good client prediction (important for weapons), and it supports Steam P2P |
| P2P transport | **Steamworks P2P** (FishySteamworks transport) | Friends join through Steam invites with no port forwarding. Development can use test App ID 480; a real App ID costs $100. Fallback: Unity Relay. |
| Authority | The host is authoritative | The host runs physics and mod logic; clients send input |
| Mod source | Each player's own GitHub repo | See "Mod distribution" |
| Asset formats | `.glb` (glTFast), `.png`, `.ogg`/`.wav` | All three can be loaded at runtime |

## Architecture

```
Unity base game (C#)
├── Core        player controller, physics, spawn menu (Q), physgun, toolgun, chat, HUD
├── Net         FishNet + FishySteamworks, lobby/invites, entity replication
├── ModRuntime  MoonSharp VM, C# -> Lua API bindings, hot reload, error console
└── ModSync     fetch from GitHub, cache, host -> client file transfer
```

### Lua API (based on GMod)
- `SWEP` table: `PrintName`, `ViewModel`, `Primary = {Damage, Delay, ClipSize, Automatic, Sound}`, `PrimaryAttack`, `SecondaryAttack`, `Reload`, `Think`
- `ENT` table: `Initialize`, `Think`, `OnTouch`, `OnDamaged`, `Use`
- `hook.Add(event, id, fn)`; `net.Start/Write*/Send/Receive`; `timer.Simple`
- Helpers: `util.TraceLine`, `ents.Create`, `effects.Muzzle/Tracer/Explosion`, `sound.Play`
- `SERVER` / `CLIENT` flags, the same idea as in GMod

### Weapons (the first target)
- C# handles the hard, shared parts: hitscan with lag compensation, projectiles, recoil, view models, ammo and animations.
- Lua only sets the stats and behaviour, so a basic gun mod is about 20 lines.

## Mod distribution (GitHub)
1. Each player has a repo, for example `github.com/jake/cmod-stuff`, and each top-level folder in it is one mod.
2. In game: **Mods → Add repo URL**. The host downloads the repo as a zip from GitHub (public repos to begin with; private ones need a token).
3. The host checks `mod.json` and then sends the files over FishNet to every peer, so clients never have to contact GitHub.
4. **Reload** button, or optional polling of the latest commit, for live updates. Lua reloads in place, and asset changes respawn the affected entities.
5. Players who join late get the host's cached mod set before they spawn.

### Live loading and host permissions
- The host gives each player a permission level: **None** (just plays), **Load** (can load mods live), or **Admin** (can also unload or kick mods and change permissions). Players can request Load, and the host gets a popup to approve.
- A player with permission runs `require(...)` (see below) from the console or the Mods menu. The host fetches the mod, and every player loads it **without a restart**.
- Unloading a mod calls its `OnUnload`, removes its entities and hooks, and frees its assets.

### `require()`: Roblox-style mod IDs
Roblox's `require(assetId)` needs a central asset server. We use GitHub paths instead, so no server is needed:
```lua
require("jake/cmod-stuff/railgun")        -- latest on default branch
require("jake/cmod-stuff/railgun@v1.2")   -- pinned tag/commit (reproducible)
local util = require("sam/libs/mathx")    -- mods can depend on other mods' code
```
- An ID has the form `owner/repo/folder[@ref]` and resolves to that folder in the GitHub repo. Any dependencies listed in a mod's `mod.json` are loaded recursively.
- Optional short aliases (`require("railgun")`) can come from a `cmod.json` index file the host adds by URL.
- Each ID is turned into a commit SHA. The SHA is the cache key, so a mod pinned to a SHA never downloads again.

### Compression and fast downloads
Mods are packed into a **`.cpak`** bundle when they're fetched:

| Content | Treatment |
|---|---|
| Models (.glb) | meshopt compression and automatic LODs (gltfpack). Usually 5–10× smaller |
| Textures | Converted to KTX2/Basis Universal, which stays compressed on the GPU, and capped at a maximum resolution the host sets |
| Sounds | Ogg Vorbis, mono for 3D sounds |
| Lua / JSON | zstd |
| Whole bundle | Split into content-addressed chunks (hash → chunk) |

- **Process once, share the result:** only the host (or the player loading the mod) downloads from GitHub and processes the files. Peers get the finished `.cpak` chunks.
- **Shared chunks:** peers download from the host and from each other, swarm-style, so the host's upload speed isn't the bottleneck.
- **Persistent local cache** keyed by hash. A mod you've loaded before appears instantly, and an update only downloads the chunks that changed.
- **Code first, assets as they stream in:** the Lua runs straight away using a placeholder model or low LOD, and full assets swap in once they arrive. Large models load the low LOD first.
- **Limits the host sets:** maximum mod size, maximum texture size and maximum triangle count, with a warning before something large is loaded.
- The template repo can include a GitHub Action that builds the `.cpak` in advance, so loading is even faster.

Trust model: friends only. Mods have full access to the API, with no hostile-code sandbox. We still catch Lua errors and limit how long a script can run, so a buggy mod can't freeze the game.

### Mod layout
```
my-mod/
├── mod.json            { "name", "author", "version", "description" }
├── init.lua
├── weapons/*.lua       SWEPs
├── entities/*.lua      ENTs
├── tools/*.lua         toolgun modes
└── assets/{models,textures,sounds}/
```

## The AI template repo: `cmod-mod-template`
The thing friends fork or "use as template":
- `CLAUDE.md`: explains what Cmod is, the API rules, the folder layout and things not to do. Claude Code reads it automatically.
- `docs/API.md`: every function with a short example
- `examples/`: pistol, shotgun, rocket launcher, melee, grenade, bouncy ball, vehicle, toolgun mode
- `.claude/skills/new-weapon`: a skill that creates a new weapon from scratch
- An in-game error console with a "Copy for Claude" button (stack trace plus the mod's file path)

## Milestones
1. **M0 Core:** walk around, physgun, spawn props, single player
2. **M1 Net:** FishNet + Steam P2P, 2+ players see the same physics
3. **M2 Lua:** MoonSharp runtime, ENT and hook API, local `Mods/` folder with hot reload
4. **M3 Weapons:** SWEP API, hitscan and projectiles, 3 built-in example guns
5. **M4 Live mods:** host permissions, `require()` IDs, GitHub fetch, live load and unload
6. **M5 AI kit:** template repo, CLAUDE.md, API.md, examples, new-weapon skill
7. **M6 Assets + .cpak:** runtime glb/KTX2/ogg, compression pipeline, chunk cache, P2P chunk sharing, streaming LODs

## Open questions
- Will we buy our own Steam App ID ($100), or stay on 480 during development?
- Do we need private mod repos (GitHub token in settings)?
- When a mod changes, do existing entities keep their state or respawn?
