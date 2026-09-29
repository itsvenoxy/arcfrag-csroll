# Arcfrag CSRoll – UI-Brief

Referenz 1920×1080, Arcfrag-Design (wie Welcome/Toasts/Alert im UI-Kit). Die Panels kommen als Erweiterung in das bestehende Addon `arcfrag-ui` (`hud.xml`). ArcfragCore bleibt Besitzer des Layouts, CSRoll steuert es über den Core.

## Muss

### 1. Roll-Reveal (Rundenstart, ca. 6 s)
- Phase A „Rolling…“: Modifier-Namen laufen schnell durch (Slot-Machine, ca. 20 Frames in 3 s).
- Phase B Reveal: 1–3 Karten (Standard ist 1, jeder Spieler hat einen eigenen Modifier).
  - pro Karte: Name, Beschreibung (1–2 Zeilen, wird Buchstabe für Buchstabe „entschlüsselt“), Kategorie-Icon, Tone (Buff / Nerf / Chaos / Fähigkeit)
- Auch mitten in der Runde nutzbar, wenn jemand einzeln einen Modifier bekommt.

### 2. Modifier-Leiste (während der Runde, dauerhaft)
Chip mit aktivem Modifier (Icon + Name). Darunter bzw. daneben 0–3 gestapelte Status-Widgets, eines pro Typ:

| Widget | Modifier | Daten | Zustände |
|---|---|---|---|
| Fähigkeit mit Cooldown | Vanish, Recall, Flanker (Teleporter) | Name, Taste („F“ / „Inspect“), Rest-Sekunden, Balken 0–1 | Ready / Aktiv (läuft) / Cooldown |
| Gauge | Jetpack (Treibstoff), Conditional Invisibility (Tarnung %) | Label, Wert 0–1, % | voll / mittel / leer (Farbe) |
| Wert | Regeneration | HP/s | normal / stehend (höher) |
| Timer-Wechsel | Weapon Roulette, Butterfly Effect | aktuelle Waffe bzw. Modifier, „nächster Wechsel in X s“ | Idle / Spin (Namen rotieren) / gelandet |
| Gestohlen | Mimic | gestohlener Modifier oder „—“ | leer / aktiv |

Update-Rate bis ca. 10×/s: Balken nur über Breite bzw. Klasse, keine komplexen Animationen pro Update.

### 3. Zuschauer-Panel
Wenn du tot bist oder zuschaust: Name des Spielers + seine Modifier (Name + kurze Beschreibung).

## Kann über bestehende Arcfrag-Elemente laufen (kein neues Design nötig, sag nur, falls du es anders willst)
- Butterfly-Swap-Ansage, Mimic-Diebstahl, Revive ausgelöst → Toast
- „Random Rounds an/aus“ (Admin) → Alert
- Übersicht „wer hat was“ beim Rundenstart → Chat

## Optional
- Tab/Übersicht-Panel: alle Spieler mit ihren Modifiern
- `!arc`-Menü-Tab „CSRoll“: Liste aller Modifier mit Beschreibung

## Regeln / Einschränkungen
- Kollidiert nicht mit Welcome, Toast-Stack, Alert, Vote und Summary. Das Reveal darf das Alert-Feld mitbenutzen (sie laufen nie gleichzeitig), die Leiste braucht einen eigenen festen Platz.
- Fadenkreuz und untere Bildmitte (HP/Munition, Buy-Hinweise) frei lassen.
- Image-Panels ohne `texturewidth`/`textureheight` (CS2 lehnt sie ab).
- Icons: ca. 6 Kategorie-Icons statt 42 einzelne (Waffe, Bewegung, Gesundheit, Granate, Sicht/Unsichtbar, Chaos). Einzel-Icons nur, wenn du Lust hast.
- Alle Texte sind dynamisch (Englisch), Längen: Name ≤ 22 Zeichen, Beschreibung ≤ 70 Zeichen.

## Modifier (42)
AtomicExplosions, Bounty, BunnyHop, ButterflyEffect, ClusterGrenades, ConditionalInvisibility, Cvar-basierte, Damage, DisarmingBullets, Drunk, Flanker, FlashingBullets, Grenade, HardHead, Health, IncreasedSpread, InfiniteAmmo, Invisibility, Jetpack, MasterZeus, Mimic, MissedShot (Butterfingers/Boomerang), PlantAnywhere, PoisonousSmoke, Recall, Regeneration, Revive, Saint, ScalePlayer, SmokeImmunity, SteelBody, SuicideBomber, Teleport, TeleportNavMesh, Vampire, Vanish, Velocity (Speedhack/HeavyBoots), Weapon, WeaponLimits, WeaponRoulette, Xray
