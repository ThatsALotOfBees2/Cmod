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
5. **M4 GitHub sync:** add a repo by URL, host-to-client file transfer, reload
6. **M5 AI kit:** template repo, CLAUDE.md, API.md, examples, new-weapon skill
7. **M6 Assets:** runtime .glb, sound and texture loading inside mods

## Open questions
- Will we buy our own Steam App ID ($100), or stay on 480 during development?
- Do we need private mod repos (GitHub token in settings)?
- When a mod changes, do existing entities keep their state or respawn?
