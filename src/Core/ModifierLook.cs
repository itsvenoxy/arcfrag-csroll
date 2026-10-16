using Arcfrag.Core.Contract;

using SwiftlyS2.Shared;

using CSRoll.Config;
using CSRoll.Hud;
using CSRoll.Modifiers;

namespace CSRoll.Core;

/// <summary>
/// Category (icon) and tone (color: Buff green, Nerf red, Chaos orange, Ability gold) of every modifier, for the Arcfrag
/// UI HUD. Keyed on the internal Name (the one DisabledModifiers and admin commands use), not the display name.
/// Unknown names (cvar modifiers from ConVarModifiers/*.cfg, anything added later) get a category guessed from the name
/// and the Chaos tone.
/// </summary>
public static class ModifierLook
{
    private static readonly Dictionary<string, (ModeHudCategory Category, ModeHudTone Tone)> Looks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AtomicExplosions"] = (ModeHudCategory.Grenade, ModeHudTone.Buff),
        ["Bounty"] = (ModeHudCategory.Weapon, ModeHudTone.Buff),
        ["BunnyHop"] = (ModeHudCategory.Movement, ModeHudTone.Buff),
        ["ButterflyEffect"] = (ModeHudCategory.Chaos, ModeHudTone.Chaos),
        ["ClusterGrenades"] = (ModeHudCategory.Grenade, ModeHudTone.Buff),
        ["ConditionalInvisibility"] = (ModeHudCategory.Vision, ModeHudTone.Buff),
        ["MoreDamage"] = (ModeHudCategory.Weapon, ModeHudTone.Buff),
        ["DisarmingBullets"] = (ModeHudCategory.Weapon, ModeHudTone.Buff),
        ["Drunk"] = (ModeHudCategory.Movement, ModeHudTone.Nerf),
        ["Flanker"] = (ModeHudCategory.Movement, ModeHudTone.Ability),
        ["FlashingBullets"] = (ModeHudCategory.Weapon, ModeHudTone.Buff),
        ["LongerFlashes"] = (ModeHudCategory.Grenade, ModeHudTone.Buff),
        ["ChineseGrenades"] = (ModeHudCategory.Grenade, ModeHudTone.Chaos),
        ["HardHead"] = (ModeHudCategory.Health, ModeHudTone.Buff),
        ["Juggernaut"] = (ModeHudCategory.Health, ModeHudTone.Buff),
        ["RandomHealth"] = (ModeHudCategory.Health, ModeHudTone.Chaos),
        ["IncreasedSpread"] = (ModeHudCategory.Weapon, ModeHudTone.Nerf),
        ["InfiniteAmmo"] = (ModeHudCategory.Weapon, ModeHudTone.Buff),
        ["Jetpack"] = (ModeHudCategory.Movement, ModeHudTone.Ability),
        ["MasterZeus"] = (ModeHudCategory.Weapon, ModeHudTone.Buff),
        ["Mimic"] = (ModeHudCategory.Chaos, ModeHudTone.Chaos),
        ["Butterfingers"] = (ModeHudCategory.Weapon, ModeHudTone.Nerf),
        ["BoomerangBullets"] = (ModeHudCategory.Weapon, ModeHudTone.Nerf),
        ["PlantAnywhere"] = (ModeHudCategory.Chaos, ModeHudTone.Chaos),
        ["PoisonousSmoke"] = (ModeHudCategory.Grenade, ModeHudTone.Buff),
        // The design drew Recall with the Chaos icon; it rewinds your position, so it gets the Vision icon.
        ["Recall"] = (ModeHudCategory.Vision, ModeHudTone.Ability),
        ["Regeneration"] = (ModeHudCategory.Health, ModeHudTone.Buff),
        ["Revive"] = (ModeHudCategory.Health, ModeHudTone.Buff),
        ["Saint"] = (ModeHudCategory.Health, ModeHudTone.Buff),
        ["SmallPlayers"] = (ModeHudCategory.Health, ModeHudTone.Nerf),
        ["SmokeImmunity"] = (ModeHudCategory.Vision, ModeHudTone.Buff),
        ["SteelBody"] = (ModeHudCategory.Health, ModeHudTone.Buff),
        ["SuicideBomber"] = (ModeHudCategory.Grenade, ModeHudTone.Chaos),
        ["SwapOnDeath"] = (ModeHudCategory.Chaos, ModeHudTone.Chaos),
        ["SwapOnHit"] = (ModeHudCategory.Chaos, ModeHudTone.Chaos),
        ["TeleportOnReload"] = (ModeHudCategory.Chaos, ModeHudTone.Chaos),
        ["TeleportOnHit"] = (ModeHudCategory.Chaos, ModeHudTone.Chaos),
        ["Vampire"] = (ModeHudCategory.Health, ModeHudTone.Buff),
        ["Vanish"] = (ModeHudCategory.Vision, ModeHudTone.Ability),
        ["Speedhack"] = (ModeHudCategory.Movement, ModeHudTone.Buff),
        ["HeavyBoots"] = (ModeHudCategory.Movement, ModeHudTone.Nerf),
        ["OnePerReload"] = (ModeHudCategory.Weapon, ModeHudTone.Nerf),
        ["NoRecoil"] = (ModeHudCategory.Weapon, ModeHudTone.Buff),
        ["RandomLoadout"] = (ModeHudCategory.Weapon, ModeHudTone.Chaos),
        ["WalkingGrenadier"] = (ModeHudCategory.Grenade, ModeHudTone.Chaos),
        ["WeaponRoulette"] = (ModeHudCategory.Weapon, ModeHudTone.Chaos),
        ["Wallhack"] = (ModeHudCategory.Vision, ModeHudTone.Buff),
    };

    public static bool IsKnown(string name) => Looks.ContainsKey(name);

    /// <summary>Category and tone of a modifier by its internal name.</summary>
    public static (ModeHudCategory Category, ModeHudTone Tone) For(string name)
        => Looks.TryGetValue(name, out var look) ? look : (GuessCategory(name), ModeHudTone.Chaos);

    private static ModeHudCategory GuessCategory(string name)
    {
        bool Has(params string[] words) => words.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (Has("grenade", "smoke", "molotov", "decoy", "explos")) return ModeHudCategory.Grenade;
        if (Has("invis", "vision", "xray", "wall", "flash", "blind", "fog")) return ModeHudCategory.Vision;
        if (Has("health", "hp", "armor", "damage", "heal", "life")) return ModeHudCategory.Health;
        if (Has("speed", "jump", "gravity", "hop", "boots", "move", "fly", "air")) return ModeHudCategory.Movement;
        if (Has("weapon", "ammo", "bullet", "recoil", "spread", "gun", "zeus", "knife", "reload", "shot")) return ModeHudCategory.Weapon;
        return ModeHudCategory.Chaos;
    }

    /// <summary>
    /// A modifier as the Arcfrag UI HUD shows it: display name, plain description (no chat color tokens), category, tone, and
    /// for the Random Perk v2 look (contract 0.15.0) the icon key, colour family, rarity tier and short line.
    /// <paramref name="rarity"/> null or switched off: no tier tag.
    /// </summary>
    public static ModeHudModifier ToHud(ISwiftlyCore core, GameModifierBase modifier, bool withDescription = true, RarityConfig? rarity = null)
    {
        var (category, tone) = For(modifier.Name);
        var description = withDescription ? CSRollUtils.PlainTextFromChatColors(CSRollUtils.GetModifierDescription(core, modifier)) : "";
        string? shortText;
        try
        {
            shortText = CSRollUtils.PlainTextFromChatColors(CSRollUtils.GetModifierShortDescription(core, modifier));
        }
        catch (Exception)
        {
            shortText = null;
        }

        return new ModeHudModifier(CSRollUtils.PlainTextFromChatColors(CSRollUtils.GetModifierDisplayName(core, modifier)), description, category, tone)
        {
            Icon = HudCatalog.Icon(modifier),
            Group = Group(HudCatalog.Category(modifier)),
            Rarity = rarity is { Enabled: true } ? Rarity(ModifierRarity.Resolve(modifier, rarity)) : ModeHudRarity.None,
            Short = string.IsNullOrWhiteSpace(shortText) ? null : shortText,
        };
    }

    /// <summary>A reel tile for a modifier: icon, colour family, tier (none while rarity is off).</summary>
    public static ModeHudTile Tile(GameModifierBase modifier, RarityConfig rarity)
        => new(HudCatalog.Icon(modifier), Group(HudCatalog.Category(modifier)),
            rarity.Enabled ? Rarity(ModifierRarity.Resolve(modifier, rarity)) : ModeHudRarity.None);

    /// <summary>The upstream HudCatalog category key (move/weap/util/surv/stealth/chaos) as the contract's colour family.</summary>
    public static ModeHudGroup Group(string category) => category switch
    {
        "move" => ModeHudGroup.Move,
        "weap" => ModeHudGroup.Weap,
        "util" => ModeHudGroup.Util,
        "surv" => ModeHudGroup.Surv,
        "stealth" => ModeHudGroup.Stealth,
        _ => ModeHudGroup.Chaos,
    };

    public static ModeHudRarity Rarity(ModifierTier tier) => tier switch
    {
        ModifierTier.Restricted => ModeHudRarity.Restricted,
        ModifierTier.Classified => ModeHudRarity.Classified,
        ModifierTier.Covert => ModeHudRarity.Covert,
        ModifierTier.Gold => ModeHudRarity.Gold,
        _ => ModeHudRarity.MilSpec,
    };

    /// <summary>Short gauge labels of the list rows (reference rp.js GAUGE); others keep the gauge's own label.</summary>
    private static readonly Dictionary<string, string> GaugeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jetpack"] = "Fuel",
        ["Recall"] = "Recall",
        ["Flanker"] = "Flanker",
        ["Vanish"] = "Vanish",
        ["KamikazeChickens"] = "Chickens",
        ["ButterflyEffect"] = "Next roll",
        ["WeaponRoulette"] = "Next weapon",
    };

    /// <summary>Abilities used with the inspect key: the row shows the key box "F".</summary>
    private static readonly HashSet<string> InspectAbilities = new(StringComparer.OrdinalIgnoreCase) { "Recall", "Flanker", "Vanish", "KamikazeChickens" };

    /// <summary>
    /// A modifier's live gauge (upstream HudGauge) as the widget drawn inside its list row: short label, "39%" / "2.5s" /
    /// "READY", the bar, and the key box for inspect abilities.
    /// </summary>
    public static ModeHudWidget GaugeWidget(GameModifierBase owner, HudGauge gauge)
    {
        var label = GaugeLabels.TryGetValue(owner.Name, out var known) ? known : gauge.Label;
        var value = gauge.Ready ? "READY" : gauge.Value.Contains(" · ", StringComparison.Ordinal) ? gauge.Value[(gauge.Value.LastIndexOf(" · ", StringComparison.Ordinal) + 3)..] : gauge.Value;
        var key = InspectAbilities.Contains(owner.Name) ? "F" : null;
        return new ModeHudWidget(key is null ? ModeHudWidgetKind.Gauge : ModeHudWidgetKind.Ability, label, value.Replace(',', '.'), "",
            Percent(gauge.Fill), gauge.Ready ? ModeHudState.Ready : ModeHudState.None, key)
        {
            Modifier = HudCatalog.Icon(owner),
        };
    }

    /// <summary>"12,4s" style is the center HTML's; the Arcfrag HUD uses "12.4s".</summary>
    public static string Seconds(float seconds) => $"{Math.Max(0f, seconds).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}s";

    /// <summary>A 0 … 1 ratio as a 0 … 100 bar fill.</summary>
    public static int Percent(float ratio) => (int)Math.Round(Math.Clamp(ratio, 0f, 1f) * 100f);
}
