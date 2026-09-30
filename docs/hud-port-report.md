# Report: CSRoll HUD in the Arcfrag UI

Brief: `docs/hud-port-brief.md`. Branch `feat/csroll-hud` in all three repos, pushed, **not** merged, nothing deployed.

| Repo | Commit | Verified here |
|---|---|---|
| arcfrag-ui | `5097a26` feat(hud): CSRoll HUD panels … | XML parses; CSS braces balanced; every Cr* id the core sends exists in hud.xml (checked by a core unit test). **Not compiled** (Windows resourcecompiler). |
| arcfrag-core | `f1769cc` feat(hud): IPlatformModeHud … (0.8.0) | `dotnet build -c Release` 0 warnings, `dotnet test -c Release` **478/478 green** (420 before + 58 new). |
| arcfrag-csroll | `a74699f` feat(hud): Arcfrag UI HUD … (1.37.3-arcfrag.1) | `dotnet build -c Release` and `dotnet publish -c Release` succeed, 0 warnings. No test project in this repo. |

## What was done

### arcfrag-ui
- `hud.xml`: new panels between the map vote and the `!arc` menu (so the menu still covers them), all `hittest="false"`:
  - `CrRoll`: `CrSlot` (head "Rolling…" + `CrSlotRound`, window `CrSlotWin` with reel `CrSlotReel` of 4 rows `CrSlotR0..3`, bar `CrSlotFill`),
    `CrReveal` with `CrEyebrow` and card slots `CrCard0..2` (tone bar, icon box `CrCardNBox`, `Cat`, `Tag`, `Name`, `Desc`).
  - `CrModBar`: chip `CrChip` (tone bar, 3 icon boxes `CrChipI0..2`, `CrChipName`, `CrChipTag`) + widget slots `CrW0..2`. Each widget has a
    shared head (`Label`, `Key`), five body layouts (`Ab*` Ability, `Ga*` Gauge, `Va*` Value, `Ro*` Rotor incl. spinning row `RoG1/RoC/RoG2`,
    `St*` Stolen with icon box) switched by `WAbility/WGauge/WValue/WRotor/WStolen`, and the bar `CrWNFill`.
  - `CrSpec`: eyebrow "Spectating", `CrSpecCount`, `CrSpecName`, `CrSpecMeta`, entries `CrSpec0..2`.
  - Header comment lists all panels.
- `arcfrag.css`: new section at the end, all classes `AfCr*` (design class names prefixed, e.g. `AfRollSlot` → `AfCrRollSlot`), state classes as
  in the design (`Buff/Nerf/Chaos/Ability`, `Ready/Active/Cooldown/Warn/Bad/Boosted/Spinning/Landed/Empty`, `P0..P10`, `Gold/Green/Red/Orange/Muted`,
  `Visible`/`Leaving`, plus `Late`, `Multi`, `Hidden`, `NoKey`, `NoBar`, `Moving/Landing/Offset/Spin/Cur`). Ported per the design's Panorama notes:
  inline-block rows → `flow-children: right`, blocks → `flow-children: down`, `text-align` on blocks → `horizontal-align`, `line-height` → `vertical-align`/padding,
  `overflow: hidden` → `overflow: clip`, rgba → `#rrggbbaa`, box-shadow in the file's `color x y blur spread` order, keyframes in the file's `@keyframes 'Name'` syntax.
  Show/hide works like the existing `AfHud` (collapse + keyframe in/out), because transitions cannot animate out of `visibility: collapse`.
- Icons: the 12 SVGs are in `panorama/images/custom_game/arcfrag/csroll/`. Shown as `Panel.AfCrIcon` / `.AfCrStolenIcon` with
  `background-image: url("file://{images}/custom_game/arcfrag/csroll/<cat>[-gold].svg")` + `background-size: 100% 100%`, chosen by `CatWeapon … CatChaos`
  (+ `Gold`) on the box around it. No `Image` panels.
- `build.ps1` also compiles `*.svg`; README notes the images folder and the CSRoll panels.

### arcfrag-core (0.8.0, contract 0.8.0)
- Contract (`ArcfragCore.Contract/IPlatformModeHud.cs`, plain records/enums only): `IPlatformModeHud { Available; ShowRoll; SetModBar; SetSpectator; Toast }`,
  `ModeHudModifier(Name, Description, Category, Tone)`, `ModeHudRoll(Round, Cards, Late, Reel)`, `ModeHudBar(Modifiers, Widgets)`,
  `ModeHudWidget(Kind, Title, Value, Text, Percent, State, Key, Category, Tone, Before, After)`, `ModeHudSpectator(TargetName, Meta, Modifiers)`,
  enums `ModeHudCategory`, `ModeHudTone`, `ModeHudWidgetKind`, `ModeHudState`. Key `PlatformCoreCapabilities.PlatformModeHudName = "arcfrag:core_hud"`.
- `ArcfragCorePlugin.ModeHud.cs`: adapter registered in `ConfigureSharedInterface`. `Available` = Arcfrag UI addon active (`_hud is ArcfragHud`), not a
  match server, mode in `HudModes`. Calls for bots / empty slots are dropped. A 50 ms timer runs the scheduled HUD steps (`ArcfragHud.RunDue`);
  the existing 1 s `Tick` still runs them too.
- `Ui/ArcfragHud.Mode.cs` (ArcfragHud is now `partial`):
  - Roll timeline per the css header: reel 0–1.8 s, name swaps eased out (inverted `n·(1-(1-t/1.8)³)`, swaps < 50 ms apart merged), `Moving`/`Offset`/`Spin`,
    progress P0→P10, landing 1.55 s (`Landing`, winner row `Cur` in gold), P10 at 1.8 s, slot `Leaving` 1.85 s, cards from 2.0 s with 150 ms stagger (punch),
    descriptions decode at 35 chars/s (sent as partially scrambled strings every 100 ms), bar in at 5.2 s, cards `Leaving` 7.0 s, hidden 7.4 s.
    Late roll: no reel, gold eyebrow "New modifier · joined mid-round", `Late` card with gold icon, leaves after 5 s, bar not held. A new roll cancels the old one (generation counter).
  - Modifier bar: chip (1 modifier: tone + name + tag; 2–3: `Multi`, per-icon tones, "n modifiers"), widgets with kind/state/fill classes; a gauge without explicit
    state gets gold > 60 %, `Warn` > 25 %, `Bad` below. Chip first, widgets +120 ms each when the bar appears. While a roll is running a non-null bar is held until 5.2 s
    (null is never held); the old bar goes when a new roll starts.
  - Spectator panel: replaces the bar; a new target leaves (0.22 s), then new content comes in.
  - Diff cache (`HudDiffCache`, per player, lives in the per-player HUD object): every Cr* dialog variable and class goes through it, only changes are sent.
  - `Toast` uses the existing 4-lane toast stack; tone accepts HUD tones and Buff/Nerf/Chaos/Ability.
  - No collision with Alert/Welcome/Vote/Menu: separate panels and state, own hide logic (the existing `Hide()` is not used for Cr panels). The map vote sits in
    the same spot as the bar, so `ShowVote` hides bar and spectator panel; the bar returns with the next update after the vote. `Reset()` on map change drops
    `_players` (incl. caches and mode state) and `_later` as before.
- Tests (`tests/ArcfragCore.Tests/ModeHudTests.cs`): diff cache, swap easing/limits, progress, full timeline (times of every step, stagger, bar-in, leave), decode
  rate/finish/stop before leave, late roll, max 3 cards, reel lands on winner without repeats, decode string (prefix, spaces, determinism, clamping), percent clamping,
  gauge/state/fill colors, toast tones, class tables, shared-interface key, and that hud.xml (sibling checkout) contains every id the core sends.
- Versions: `ArcfragCore.csproj` + `PluginMetadata` 0.8.0 (the existing version test guards it), contract 0.8.0. READMEs updated.

### arcfrag-csroll (1.37.3-arcfrag.1)
- Contract reference: `lib/ArcfragCore.Contract.dll` + `lib/Arcfrag.Contracts.dll` (built from arcfrag-core `f1769cc`), `Reference … Private=false`, and copied to
  `resources/exports/` in the build/publish output – the way the arcfrag-core README describes for mode plugins. The repo builds standalone.
- `CSRoll.UseSharedInterface` reads `arcfrag:core_hud` (in a `NoInlining` method; a missing/old contract logs an error and falls back) and hands it to the runtime,
  also after a `Load()` rebuild.
- `ModifierRuntime.ModeHud.cs`: `UseModeHud` is checked live. When true:
  - round-start roll (per-player and global), `!reroll`-style immediate rolls → `ShowRoll`; the modifiers are committed at 1.55 s (reel landing), like the center-HTML
    spin commits on its landing; generation guard unchanged. Chat summary "Your modifiers:" and the reveal fade stay.
  - `!memodifier` / admin-added modifiers → `ShowRoll(Late)` (gold card).
  - Spectator HUD → `SetSpectator` (target name, "T · 64 HP", up to 3 modifiers); hidden again when the player stops spectating.
  - Per-modifier HUD → one `SetModBar` per player at 10 Hz: chip from the modifiers that apply to the player, widgets from the new status sections (priority, max 3).
    Dead players get no bar.
  - The center-HTML path is untouched and used whenever `UseModeHud` is false.
- `GameModifierBase.SetStatus(slot, ModeHudWidget)` next to `SetHud` (same TTL, cleared with it). Emitted by Vanish, Recall, Flanker (Ability, key F / Inspect),
  Jetpack (Gauge fuel, key Space), ConditionalInvisibility (Gauge concealment), Regeneration (Value, Boosted when standing still), WeaponRoulette and ButterflyEffect
  (Rotor: idle countdown, spinning with ghost names, "Landed" for 1.5 s), Mimic (Stolen with the stolen modifier's icon/tone and "from <victim>", Empty before).
  Modifiers publish both HTML and status; the runtime draws one of them.
- `ModifierLook`: category + tone for all 47 modifiers; Drunk, Butterfingers, BoomerangBullets, SmallPlayers, IncreasedSpread, HeavyBoots (and OnePerReload) are Nerf;
  Recall uses the Vision icon; unknown names (cvar modifiers) get a category guessed from the name and tone Chaos.
- Butterfly swap, Mimic steal, Revive → `Toast` when the HUD is available, otherwise the old chat line.
- Version `1.37.3-arcfrag.1` (PluginVersion + csproj). README section.

## How the flow works
1. Server start: ArcfragCore loads, registers `arcfrag:core_hud`. CSRoll's `UseSharedInterface` picks it up. `Available` is true only with `UiAddon` set
   (and in AddonsManager's list), on a non-match server, mode in `HudModes`.
2. Round start: CSRoll rolls (unchanged), 1 s later `ShowRoll` per player. ArcfragCore schedules the ~50 steps of the timeline on the player's
   `custom_hud_layout` (dialog variables + classes of `CrRoll`). At 1.55 s CSRoll activates the modifiers.
3. From then on CSRoll's OnTick composes a `ModeHudBar` every 100 ms; ArcfragCore holds it until 5.2 s into the roll, then draws it and afterwards only sends
   what changed (a timer counting down changes one text and sometimes one P-class).
4. Death: bar null (it leaves), spectating → `SetSpectator` every `SpectatorHud.RefreshIntervalSeconds`.
5. Toasts go into the shared toast stack (right). Map change: ArcfragCore `Reset()`, CSRoll `ResetMapRelativeTimeState`.
6. No ArcfragCore / no addon / not available: CSRoll behaves exactly as before (center HTML).

## Not verified / known limits
- **Panorama compile and the in-game look are untested** (no Windows resourcecompiler, no CS2 client here). Specifically unverified:
  `overflow: clip`, multiple animations on the Ready widget, `url("file://{images}/…svg")` backgrounds from a Workshop addon, the label padding used for
  vertical centering, and that CustomHud validation accepts the new panels.
- The scrambled part of a description is the same color as the decoded part (a plain `{s:desc}` label cannot color half a string); the design shows it dim.
- Late card / multi-kill alert can overlap at the top centre (both there by design; the roll is drawn on top).
- The reel's tick sound per frame is gone on the Arcfrag path (the center-HTML path still plays it). Screen fade on reveal stays.
- Spectator meta shows team + HP, not "kills this round" (not tracked by CSRoll).
- FlashingBullets is tagged Buff (it blinds whoever you hit), unlike the design's sample card.
- **Deploy dependency:** CSRoll ships `ArcfragCore.Contract.dll` 0.8.0 as an export. If an older ArcfragCore (≤ 0.7.x) runs on the same server, its older
  export may win SwiftlyS2's one-copy-per-name load; then the modifiers' widget code cannot load. ArcfragCore 0.8.0 must go on every CSRoll server together
  with this CSRoll build (log line: "ArcfragCore.Contract on this server has no IPlatformModeHud").
- Keep `lib/` in arcfrag-csroll in sync when the contract changes (rebuild from arcfrag-core and copy).

## Testanleitung für Janis

### 1. Addon auf dem Windows-PC bauen
1. Branch holen: `git fetch && git checkout feat/csroll-hud` in `arcfrag-ui`.
2. Den Inhalt von `panorama/` (inkl. dem neuen Ordner `panorama/images/custom_game/arcfrag/csroll/` mit den 12 SVGs) nach
   `…\Counter-Strike Global Offensive\content\csgo_addons\arcfrag_ui\panorama\` kopieren (bestehende Dateien überschreiben).
3. PowerShell: `.\build.ps1`. In der Ausgabe darf bei `hud.xml`, `arcfrag.css` und den 12 `.svg` kein `error`/`fail` stehen.
   In `game\csgo_addons\arcfrag_ui\panorama\` müssen danach `layout\…\hud.vxml_c`, `styles\…\arcfrag.vcss_c` und `images\custom_game\arcfrag\csroll\*.vsvg_c` liegen.
4. Lokal prüfen (empfohlen vor dem Upload): CS2 mit `-tools` bzw. Workshop Tools starten, Addon `arcfrag_ui` laden, eine Map mit Bots starten, ArcfragCore +
   CSRoll aus den Branches auf einem lokalen/Test-Server. In der Konsole auf Panorama-Fehler achten
   (`Layout contains disallowed …`, `Failed to load image …`, CSS-Parse-Fehler).

### 2. Upload
5. Workshop Tools → Addon `arcfrag_ui` → Publish/Update auf das bestehende Workshop-Item (dieselbe ID wie `UiAddon` in der ArcfragCore-Config).
   Kurze Änderungsnotiz: "CSRoll HUD".
6. Warten, bis Steam die neue Version ausliefert (Clients laden sie beim nächsten Verbinden über AddonsManager).

### 3. Server
7. ArcfragCore **0.8.0** bauen (`scripts/package.sh` im Branch) und auf den CSRoll-Server(n) einspielen – inkl. `resources/exports/ArcfragCore.Contract.dll`
   und `Arcfrag.Contracts.dll`. Andere Server brauchen das Update nicht zwingend (für sie ändert sich nichts), dürfen es aber bekommen.
8. CSRoll `1.37.3-arcfrag.1` bauen (`dotnet publish -c Release` in `arcfrag-csroll`, Ergebnis `build/CSRoll.zip` bzw. `build/publish/CSRoll/`) und
   auf den CSRoll-Server(n) einspielen – inkl. `resources/exports/`.
9. Config auf dem CSRoll-Server prüfen: `ArcfragCore.json` → `"UiAddon": "<Workshop-ID>"`, `"HudModes": "*"` (oder der Modus des CSRoll-Servers ist enthalten);
   AddonsManager `Main.Addons` enthält dieselbe ID.
10. **Neustart nötig:** alle Server, auf denen CSRoll läuft (neue Exports werden nur beim Start geladen, `sw plugins reload` reicht nicht).
    Server, auf denen nur ArcfragCore aktualisiert wird, ebenfalls neu starten. Server ohne Update: kein Neustart.
11. In der Server-Konsole nach dem Start suchen:
    - `ArcfragCore 0.8.0 loaded` und `ArcfragCore HUD: Arcfrag UI addon <ID>`
    - `[CSRoll] Successfully loaded! Version 1.37.3-arcfrag.1`
    - `[CSRoll] ArcfragCore mode HUD found (arcfrag:core_hud)`
    - Darf **nicht** kommen: `ArcfragCore.Contract on this server has no IPlatformModeHud`.

### 4. Im Spiel testen (mit 2 Leuten oder Bots)
12. Rundenstart: oben Mitte läuft die Walze "ROLLING… / ROUND n" (Namen erst schnell, dann langsam, Gewinner gold), danach 1–3 Karten mit Icon, Name,
    farbigem Tag (Buff grün, Nerf rot, Chaos orange, Ability gold) und Beschreibung, die sich Buchstabe für Buchstabe entschlüsselt. Nach ca. 7 s verschwinden sie.
    Kein Center-HTML mehr gleichzeitig. Der Effekt (z. B. Waffe weg bei Weapon Roulette) kommt, wenn die Walze landet.
13. Ab ca. 5 s links (unter dem Radar) die Modifier-Leiste: Chip mit Icon/Name/Tag, bei 2–3 Modifiern "3 MODIFIERS" mit Icons.
    Mit `!memodifier <Name>` (Admin) gezielt testen:
    - `Vanish` / `Recall` / `Flanker`: READY (gold, pulsiert) → ACTIVE (grün, Balken läuft ab) → Cooldown (rot, Sekunden).
    - `Jetpack`: Fuel in %, Balken gold → orange → rot, Taste "Space".
    - `ConditionalInvisibility`: Concealment 0–100 %.
    - `Regeneration`: "x HP/s", beim Stillstehen gold ("Standing still ×…").
    - `WeaponRoulette` / `ButterflyEffect`: Countdown, dann Spinning (drei Namen, Mitte orange), dann "Landed".
    - `Mimic`: "—, Kill to steal one", nach einem Kill Icon + Name + "from <Name>" und ein Toast rechts.
    - `!memodifier` selbst zeigt oben die goldene "NEW MODIFIER"-Karte.
14. Sterben: Leiste verschwindet, links erscheint "SPECTATING" mit Name, "T · 64 HP" und den Modifiern des Beobachteten; beim Wechsel des Ziels kurzes Aus-/Einblenden.
15. Butterfly-Wechsel, Mimic-Diebstahl, Revive: Toast rechts statt Chatzeile. Die Chatzeile "Your modifiers:" kommt weiterhin.
16. Map-Ende mit Map-Vote: Leiste/Spectator-Panel verschwinden, solange der Vote links steht.
17. Fallback: `UiAddon` leer setzen (oder ArcfragCore entfernen), Server neu starten → CSRoll zeigt alles wieder als Center-HTML wie vorher.
18. Fehler/Screenshots bitte mit Konsolenausgabe (Client: Panorama-Fehler, Server: `[CSRoll]`/`ArcfragCore`-Zeilen) zurückmelden.
