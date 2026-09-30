using Arcfrag.Core.Contract;

using SwiftlyS2.Shared;

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

    /// <summary>A modifier as the Arcfrag UI HUD shows it: display name, plain description (no chat color tokens), category, tone.</summary>
    public static ModeHudModifier ToHud(ISwiftlyCore core, GameModifierBase modifier, bool withDescription = true)
    {
        var (category, tone) = For(modifier.Name);
        var description = withDescription ? CSRollUtils.PlainTextFromChatColors(CSRollUtils.GetModifierDescription(core, modifier)) : "";
        return new ModeHudModifier(CSRollUtils.PlainTextFromChatColors(CSRollUtils.GetModifierDisplayName(core, modifier)), description, category, tone);
    }

    /// <summary>"12,4s" style is the center HTML's; the Arcfrag HUD uses "12.4s".</summary>
    public static string Seconds(float seconds) => $"{Math.Max(0f, seconds).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}s";

    /// <summary>A 0 … 1 ratio as a 0 … 100 bar fill.</summary>
    public static int Percent(float ratio) => (int)Math.Round(Math.Clamp(ratio, 0f, 1f) * 100f);
}
