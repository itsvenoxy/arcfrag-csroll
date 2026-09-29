Design the in-game HUD for "CSRoll" on Arcfrag, a CS2 community server brand. CSRoll is a chaos mode: at every round start each player rolls a random modifier (e.g. Jetpack, Vanish, Vampire, Drunk, Weapon Roulette). The HUD is rendered by CS2 Panorama (XML + a CSS subset), so design it as flat HTML/CSS mockups that a developer can port 1:1.

Canvas: 1920×1080 frames over a darkened CS2 gameplay screenshot (placeholder is fine). Show every screen in all listed states. All UI text in English.

## Arcfrag visual language (must match the existing HUD)
- Panel: background #111418 at 94% opacity, 1px border #ffffff14, radius 12px, shadow 0 12px 32px #00000073
- Accent gold #ffd60a (fills, highlights, level emblem, progress bars); gold tint backgrounds #ffd60a1a / #ffd60a24
- Text: white #ffffff, muted #9aa3ad, dim #5b636c; surface 2 #1a1e24; track #ffffff14
- States: success/buff #3ddc84, danger/nerf #ff4d4f, warning #ff8a00
- Font: Stratum2 (CS2's font; use a condensed sans like "Barlow Semi Condensed" as a stand-in). Eyebrows: 11–12px, bold, uppercase, letter-spacing 2px, muted. Big values bold.
- Progress bar: 6px (large 10px) track #ffffff14, fill gold, radius 3px
- Toast (existing, reuse don't redesign): 360×64 right edge, 4px colored bar left, icon, title + text
- Motion: fade + 12px slide in 0.3s, pop "punch" scale 1.3→1.0 for big moments

## Existing HUD occupancy (don't overlap)
- Top center y 96–~300: Welcome panel (860 wide), map Summary (900 wide), Alert (big word, y≈110)
- Right edge, x from right 48px, y 300–580: toast stack (4 lanes)
- Keep free: crosshair area, bottom center (CS2 health/ammo/money), top center score bar (y 0–90), bottom-left CS2 radar/chat area
- The new modifier bar needs a fixed spot that coexists with all of the above. Suggested: bottom left above the chat, or left edge middle. Your call, show it in context.

## Screens

### 1. Roll reveal (round start, ~6 s, center; may reuse the Alert area)
- State A "Rolling…": slot machine, modifier names cycle fast (show 3 frames)
- State B reveal: 1 card (default), also show the 2- and 3-card variant
  - card: category icon, modifier name (≤22 chars), description (≤70 chars, 1–2 lines) that decodes letter by letter (show a half-scrambled frame), tone color: Buff green / Nerf red / Chaos orange / Ability gold
- State C: a single card mid-round, "NEW MODIFIER" eyebrow (someone received one late)

### 2. Modifier bar (persistent during the round)
A chip with the active modifier (icon + name), plus 0–3 stacked status widgets below/next to it. Five widget types; show each in every state:
- Ability with cooldown (Vanish, Recall, Teleporter): name, key hint ("F" / "Inspect"), bar 0–100%, states READY (gold, glowing) / ACTIVE (running, green, time left) / COOLDOWN (red, "12.4s")
- Gauge (Jetpack fuel, Invisibility concealment %): label, bar, %, color full/mid/empty
- Value (Regeneration): "12 HP/s", normal vs standing-still (boosted, gold)
- Rotating timer (Weapon Roulette, Butterfly Effect): current weapon/modifier, "next swap in 8s", states IDLE / SPINNING (names blur-cycle) / LANDED
- Stolen (Mimic): "Stolen: Vampire" or empty "—"
Also show: bar with no widget (passive modifier like Hard Head), bar with 3 widgets stacked.
Constraint: bars update up to 10×/s. Animate only width and color, nothing fancy per update.

### 3. Spectator panel
When dead/spectating: "SPECTATING" eyebrow, player name, their modifiers (icon, name, one-line description), 1–3 entries.

### Optional (if time)
4. Round overview: all players with their modifier, two columns CT / T, max 10 each.
5. A "CSRoll" tab for the existing !arc menu (1400×800 window): searchable grid/list of all modifiers with category, name, description.

## Icons
Make 6 category icons (single-color, 32px, gold or white, simple shapes): Weapon, Movement, Health, Grenade, Vision/Stealth, Chaos. Modifier → category examples: Jetpack/Drunk/BunnyHop/Speedhack → Movement; Vampire/Regeneration/Juggernaut/Revive → Health; Cluster Grenades/Poisonous Smoke/Suicide Bomber → Grenade; Vanish/Invisibility/X-ray/Smoke Immunity → Vision; Weapon Roulette/Infinite Ammo/Master Zeus/Flashing Bullets → Weapon; Butterfly Effect/Mimic/Swap On Death/Teleport → Chaos.

## Panorama rules (the port must work)
- Layout via flow-children (down/right) and horizontal-align/vertical-align + margins; no CSS grid, no flex gap, no position absolute tricks
- No texturewidth/textureheight on images; icons as SVG/PNG files
- Progress widths in 10% steps are fine (P0–P10 classes)
- Box-shadow, border-radius, linear gradients, opacity, transform translate/scale OK; no backdrop blur, no filters
- Keep everything in one component sheet plus a "HUD in context" frame per screen

Deliver: a component overview page (all widgets/states side by side) and full-frame 1920×1080 compositions for screens 1–3 in context with the existing HUD elements.
