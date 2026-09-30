# Brief: CSRoll HUD in the Arcfrag UI (headless build task)

You work across three repos on this server. Do all of it, verify, commit on a branch `feat/csroll-hud` in each repo and push. Do NOT deploy to game servers, do NOT merge to main.

- `~/projects/arcfrag-ui` – CS2 Workshop addon, Panorama `panorama/layout/custom_game/arcfrag/hud.xml` + `panorama/styles/custom_game/arcfrag/arcfrag.css`. Read its README (no `texturewidth`/`textureheight`, CustomHud validation). It cannot be compiled here (Windows resourcecompiler); write valid Panorama by hand, match the existing file's syntax exactly (hex `#rrggbbaa` colors, `flow-children`, `horizontal-align`, keyframes syntax already used there).
- `~/projects/arcfrag-core` – SwiftlyS2 plugin (.NET 10, `~/.dotnet/dotnet`). `src/ArcfragCore/Ui/ArcfragHud.cs` drives the layout per player via `CCSCustomHudLayout` (`SetDialogVariableString(panel, var, value)`, `SetHasClass(panel, class, on)`). Shared interfaces in `src/ArcfragCore.Contract/` (`IPlatformUi` etc., registered in `ArcfragCorePlugin.cs` ConfigureSharedInterface). Tests in `tests/`.
- `~/projects/arcfrag-csroll` – fork of LCrew/S2-CSRoll (remote `upstream`). HUD today: `SendCenterHTML` in `src/Core/ModifierRuntime.cs` (roll reveal, spin, spectator HUD, per-modifier `SetHud` sections) and `src/Core/CSRollUtils.cs` (HTML builders).

## Design (source of truth)
`~/projects/arcfrag-csroll/design/claude-design/`: `arcfrag-csroll.css` (component sheet; its header has tokens, positions 1920×1080, timelines, Panorama porting notes – follow them), `CSRoll HUD.dc.html` (all components and states), `CSRollModBar.dc.html`, `icons/csroll/*.svg` (6 categories × white/gold), `overview.png` (render). Port 1:1 in look. Three screens:
1. Roll reveal – top center, top 100: slot "ROLLING… / ROUND n" reel with progress, then 1–3 cards (icon, name, tone tag Buff/Nerf/Chaos/Ability, description decoding letter by letter), variant C "NEW MODIFIER" late card with gold glow + punch.
2. Modifier bar – left 48, top 340, 320 wide: chip (icon, name, tone; multi chip "3 MODIFIERS") + up to 3 widgets: Ability (Ready/Active/Cooldown, key hint, bar, time), Gauge (%, gold/Warn/Bad), Value (HP/s, Boosted), Rotor (Idle countdown / Spinning reel / Landed), Stolen (Mimic, Empty).
3. Spectator panel – same place, 380 wide, replaces the bar while dead.
Recall in the design uses the Chaos icon with Ability tag – use Vision icon for Recall instead.

## Part A – arcfrag-ui
- Add panels `Roll` (slot + card row with 3 card slots), `ModBar` (chip + 3 widget slots, each widget slot containing all 5 widget layouts, switched by class `WAbility/WGauge/WValue/WRotor/WStolen`), `Spec` (header + 3 entry slots) to hud.xml, ids prefixed `Cr`. Texts as `{s:...}` dialog variables, states as classes, bars with `P0..P10`. `hittest="false"` everywhere.
- Port the CSS into arcfrag.css (new section, `AfCr*` classes). Convert rgba() to `#rrggbbaa`.
- Icons: copy SVGs to `panorama/images/custom_game/arcfrag/csroll/`. Show them as a Panel with `background-image: url("file://{images}/custom_game/arcfrag/csroll/<name>.svg")` + `background-size: 100% 100%` chosen by category class (`CatWeapon`…`CatChaos`, plus `Gold`) – NO `<Image>` panels. Add the images folder to build.ps1 (compile .svg too) and note in README.
- Leave existing panels untouched. Update the header comment listing ids/classes.

## Part B – arcfrag-core (version 0.8.0)
- New contract `IPlatformModeHud` (shared interface key `arcfrag:core_hud`, constant in `PlatformCoreCapabilities`) in `ArcfragCore.Contract`, plain records only:
  - `bool Available` (Arcfrag UI addon active on this server)
  - `ShowRoll(int slot, ModeHudRoll roll)` – round number, list of 1–3 cards (Name, Description, Category, Tone), `bool Late`
  - `SetModBar(int slot, ModeHudBar? bar)` – chip (names, category, tone, count) + up to 3 widgets (kind + fields: title, key, text, value, percent 0–100, state enum). Null hides.
  - `SetSpectator(int slot, ModeHudSpectator? spec)` – target name + 1–3 entries. Null hides.
  - `Toast(int slot, string title, string message, string tone)` – reuses the existing toast stack.
- Implement in ArcfragHud: the roll timeline per the css header (slot 0–1.8 s with name swaps easing out and P0→P10, landing, cards at 2.0 s with 150 ms stagger, description decode ~35 chars/s by sending a partially scrambled string, cards leave at 7.0 s). Driven by the existing `_later`/Tick scheduling. The mod bar is called up to 10×/s per player: cache the last value of every dialog variable and class per player and only send changes.
- Must not collide with the Alert/Welcome/Vote/Menu logic; Reset() on map change clears the new state too.
- Unit tests for the diff cache, roll timeline scheduling (pure parts), decode string, clamping. `dotnet build` + `dotnet test` must be green. Bump version in csproj; the existing test guards plugin metadata version.

## Part C – arcfrag-csroll
- Reference `ArcfragCore.Contract` the way other consumers are meant to (see arcfrag-core README / csproj: exported contract, reference with runtime excluded). If a project reference to `../arcfrag-core/src/ArcfragCore.Contract` is simplest for local builds, also vendor the built `ArcfragCore.Contract.dll` into `lib/` and reference that with `Private=false` so the repo builds standalone.
- In `UseSharedInterface` get `arcfrag:core_hud`. When present and `Available`: route roll reveal / late modifier / spectator HUD / per-modifier HUD to it and stop sending center HTML for those. When absent: keep the original behavior unchanged (fallback).
- Per-modifier HUD: add a structured status API next to `SetHud` (e.g. `SetStatus(slot, ModeHudWidget)`), emit it from Vanish, Recall, Flanker (Ability), Jetpack, ConditionalInvisibility (Gauge), Regeneration (Value), WeaponRoulette, ButterflyEffect (Rotor), Mimic (Stolen). Keep the HTML path for the fallback.
- Category + tone for every modifier: a static map (Buff/Nerf/Chaos/Ability; Weapon/Movement/Health/Grenade/Vision/Chaos), sensible defaults for unknown ones. Drunk, Butterfingers, Boomerang, Small Players, IncreasedSpread, HeavyBoots = Nerf.
- One-off announcements (Butterfly swap, Mimic steal, Revive) → `Toast`. Chat summary stays.
- Version suffix `-arcfrag.1`. `dotnet build -c Release` must succeed.

## Finish
- Each repo: commits (Conventional Commits, English), branch `feat/csroll-hud`, pushed.
- Write `~/projects/arcfrag-csroll/docs/hud-port-report.md`: what was done per repo, how the flow works, what could not be verified (Panorama compile, in-game look), exact test steps for Janis in German (Swiss "ss"): build addon on Windows with build.ps1, upload, which servers need restart.
