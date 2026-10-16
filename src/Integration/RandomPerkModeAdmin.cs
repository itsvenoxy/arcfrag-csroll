using System.Globalization;

using Arcfrag.Core.Contract;

using SwiftlyS2.Shared;

using CSRoll.Config;
using CSRoll.Core;
using CSRoll.Hud;
using CSRoll.Modifiers;

namespace CSRoll.Integration;

/// <summary>
/// The "Random Perk" tab of ArcfragCore's <c>!admin</c> panel (IPlatformModeAdmin, contract 0.15.0): the upstream
/// <c>!rolladmin</c> panel's five pages (General, Modifiers, Rarity, HUD, Config) as entries ArcfragCore draws. Changes
/// apply live exactly like the upstream panel (CustomHudService.Admin); the unsaved list, the dirty markers and Save all
/// come from the same AdminSnapshot diff, Save writes only the changed keys into config.jsonc (JsoncEditor).
/// Also fills <c>!arc</c> → Info → This match with everyone's modifiers.
/// </summary>
public sealed class RandomPerkModeAdmin(CSRoll plugin, ISwiftlyCore core) : IPlatformModeAdmin
{
    private const int MaxRounds = 10;
    private const int MaxCooldown = 10;
    private const float MaxWeight = 100f;
    private const int ListOffsets = 5;
    private const int ConfigLines = 6;

    private static readonly (string Id, string Label)[] Categories =
    [
        ("move", "Movement"), ("weap", "Weapons"), ("util", "Grenades & bomb"), ("surv", "Survival"), ("stealth", "Stealth & vision"), ("chaos", "Chaos"),
    ];

    private List<GameModifierBase>? _catalog;

    private ModifierRuntime Runtime => plugin.Runtime;
    private CSRollConfig Config => Runtime.Config;
    private IAdminPanelHost Host => plugin;

    public string TabName => "Random Perk";

    /// <summary>A 0.14 ArcfragCore asks for one flat list: the General page.</summary>
    public IReadOnlyList<ModeAdminEntry> GetEntries() => GetEntries("gen").Where(e => !e.Footer).ToList();

    // ---------------------------------------------------------------- pages

    public IReadOnlyList<ModeAdminPage> GetPages()
    {
        var (live, changes) = Changes();
        var all = Catalog();
        var enabled = all.Count(m => !live.Disabled.Contains(m.Name));
        var players = core.PlayerManager.GetAllValidPlayers().Count();
        var liveCount = Runtime.ActiveModifiers.Count;
        return
        [
            new("gen", "General") { Title = "Rolling", Meta = $"Round {Round()} · {Plural(players, "player")} · {liveCount} modifiers live", Subtitle = "What gets rolled, how often and for whom." },
            new("mod", "Modifiers") { Title = "Modifiers", Meta = $"{enabled} of {all.Count} enabled · click the tier to move it" },
            new("rar", "Rarity") { Title = "Rarity", Meta = "a tier's weight is its share of every pick", Subtitle = "Gold about once a map, however many modifiers a tier holds." },
            new("hud", "HUD") { Title = "HUD", Meta = "Arcfrag UI addon · always on", Subtitle = "How the roll and the modifiers show up for players." },
            new("cfg", "Config")
            {
                Title = "Config",
                Meta = changes.Count == 0 ? "in sync" : Plural(changes.Count, "unsaved change"),
                Subtitle = "Changes are live right away and stay until the map changes. Save writes them to disk.",
                Badge = changes.Count > 0 ? changes.Count.ToString(CultureInfo.InvariantCulture) : null,
            },
        ];
    }

    public IReadOnlyList<ModeAdminFilter> GetFilters(string page)
        => page == "mod" ? Categories.Select(c => new ModeAdminFilter(c.Id, c.Label) { Group = ModifierLook.Group(c.Id) }).ToList() : [];

    public IReadOnlyList<ModeAdminEntry> GetEntries(string page)
    {
        var (live, changes) = Changes();
        var dirty = new HashSet<string>(changes.Select(c => c.Key), StringComparer.OrdinalIgnoreCase);
        var entries = page switch
        {
            "mod" => ModifierEntries(live, dirty),
            "rar" => RarityEntries(live, dirty),
            "hud" => HudEntries(live, dirty),
            "cfg" => ConfigEntries(changes),
            _ => GeneralEntries(live, dirty),
        };
        entries.Add(new ModeAdminEntry("save", ModeAdminKind.Action, "Save to config") { Footer = true, Gold = true, Disabled = changes.Count == 0 });
        return entries;
    }

    private static List<ModeAdminEntry> GeneralEntries(AdminSnapshot live, HashSet<string> dirty)
    {
        var game = live.RollMode == ModifierRollMode.Game;
        return
        [
            ModeAdminEntry.Switch("rr", "Random rounds", live.RandomRounds) with { Subtitle = "Roll new modifiers every round", Dirty = dirty.Contains("rr") },
            new ModeAdminEntry("mode", ModeAdminKind.Choice, "Roll mode")
            {
                Subtitle = "Who shares one roll", Options = ["Player", "Team", "Game"], Selected = (int)live.RollMode, Dirty = dirty.Contains("mode"),
            },
            ModeAdminEntry.Stepper("min", "Min modifiers", live.MinRounds, 0, live.MaxRounds) with { Subtitle = "Per player per round", Dirty = dirty.Contains("min") },
            ModeAdminEntry.Stepper("max", "Max modifiers", live.MaxRounds, live.MinRounds, Math.Max(MaxRounds, live.MaxRounds)) with
            {
                Subtitle = "Per player per round", Dirty = dirty.Contains("max"),
            },
            ModeAdminEntry.Stepper("cd", "Cooldown", live.RepeatCooldown, 0, Math.Max(MaxCooldown, live.RepeatCooldown)) with
            {
                Subtitle = game ? "Not used in Game mode" : "Rounds before a player gets the same modifier again", Dirty = dirty.Contains("cd"),
            },
            ModeAdminEntry.Switch("rep", "Repeat last set", live.CanRepeat) with
            {
                Subtitle = game ? "Same modifiers can come back next round" : "Only used in Game mode", Dirty = dirty.Contains("rep"),
            },
            ModeAdminEntry.Switch("warm", "Roll in warmup", live.RollInWarmup) with { Dirty = dirty.Contains("warm") },
            ModeAdminEntry.Button("reroll", "Reroll everyone now", "Strip every active modifier and roll again?") with { Group = "Now", Hint = "new roll for all" },
            ModeAdminEntry.Button("clear", "Remove all active modifiers", "Strip every active modifier?") with { Group = "Now", Danger = true },
        ];
    }

    private List<ModeAdminEntry> ModifierEntries(AdminSnapshot live, HashSet<string> dirty)
    {
        var entries = new List<ModeAdminEntry>();
        foreach (var modifier in Catalog())
        {
            var name = modifier.Name;
            var tier = live.Tiers.TryGetValue(name, out var resolved) ? resolved : ModifierRarity.Resolve(modifier, Config.Rarity);
            var off = live.Disabled.Contains(name);
            var moved = tier != ModifierRarity.BuiltInTier(name);
            entries.Add(ModeAdminEntry.Switch("m:" + name, CSRollUtils.GetModifierDisplayName(core, modifier), !off) with
            {
                Subtitle = Short(modifier),
                Icon = HudCatalog.Icon(modifier),
                IconGroup = ModifierLook.Group(HudCatalog.Category(modifier)),
                Tag = ModifierRarity.Label(tier) + (moved ? " •" : ""),
                TagRarity = ModifierLook.Rarity(tier),
                Filter = HudCatalog.Category(modifier),
                Live = !off && Runtime.IsModifierActive(Runtime.GetRegisteredModifierByName(name)),
                Off = off,
                Dirty = dirty.Contains("m:" + name) || dirty.Contains("t:" + name),
            });
        }

        entries.Add(ModeAdminEntry.Button("allon", "All on") with { Footer = true });
        entries.Add(ModeAdminEntry.Button("alloff", "All off", "Switch every modifier off?") with { Footer = true, Danger = true });
        return entries;
    }

    /// <summary>
    /// Each tier's share of a pick the way ModifierRarity.PickWeighted works it out over today's pool: weight over the total of
    /// the tiers that have an enabled modifier (with every weight at 0 a pick is uniform, so the share is the head count).
    /// </summary>
    private List<ModeAdminEntry> RarityEntries(AdminSnapshot live, HashSet<string> dirty)
    {
        var tiers = ModifierRarity.Tiers;
        var counts = new int[tiers.Length];
        foreach (var modifier in Catalog())
        {
            if (!live.Disabled.Contains(modifier.Name) && live.Tiers.TryGetValue(modifier.Name, out var tier))
            {
                counts[(int)tier]++;
            }
        }

        var enabled = counts.Sum();
        var weights = tiers.Select(t => counts[(int)t] > 0 && float.IsFinite(live.Weights[(int)t]) ? Math.Max(0f, live.Weights[(int)t]) : 0f).ToArray();
        var total = weights.Sum();
        var shares = tiers.Select(t => total > 0f ? weights[(int)t] / total * 100f : enabled > 0 ? counts[(int)t] * 100f / enabled : 0f).ToArray();
        var strip = LargestRemainder(shares);

        var entries = new List<ModeAdminEntry>
        {
            ModeAdminEntry.Switch("rar", "Rarity tiers", live.Rarity) with
            {
                Subtitle = live.Rarity ? "Pick a tier by weight, then a modifier in it" : "Off: every enabled modifier is equally likely",
                Dirty = dirty.Contains("rar"),
            },
            new ModeAdminEntry("dist", ModeAdminKind.Distribution, "Share of every pick")
            {
                Segments = tiers.Select(t => new ModeAdminSegment(strip[(int)t], ModifierLook.Rarity(t))).ToList(),
                Off = !live.Rarity,
            },
        };
        foreach (var tier in tiers)
        {
            var k = (int)tier;
            var key = ModifierRarity.CssKey(tier);
            var count = counts[k];
            var sub = count == 0 ? "No enabled modifier"
                : shares[k] <= 0f ? $"{Plural(count, "modifier")} · weight 0, never rolls"
                : $"{Plural(count, "modifier")} · {(shares[k] / count).ToString("0.0", CultureInfo.InvariantCulture)} % each";
            entries.Add(ModeAdminEntry.Stepper("w:" + key, ModifierRarity.Label(tier), Math.Round(live.Weights[k]), 0, MaxWeight) with
            {
                Subtitle = sub,
                Rarity = ModifierLook.Rarity(tier),
                Share = (int)MathF.Round(shares[k]),
                ShareText = count == 0 ? "–" : shares[k].ToString("0.0", CultureInfo.InvariantCulture) + "%",
                BigStep = 5,
                Wide = true,
                Off = !live.Rarity,
                Dirty = dirty.Contains("w:" + key),
            });
        }

        var movedCount = live.Tiers.Count(kv => kv.Value != ModifierRarity.BuiltInTier(kv.Key));
        entries.Add(ModeAdminEntry.Button("tierreset", "Reset tiers to default", "Put every modifier back in its built-in tier?") with
        {
            Hint = movedCount == 0 ? "all on default tier" : $"{movedCount} moved by hand",
        });
        return entries;
    }

    private List<ModeAdminEntry> HudEntries(AdminSnapshot live, HashSet<string> dirty) =>
    [
        ModeAdminEntry.Switch("reveal", "Roll reveal", live.Reveal) with { Subtitle = "Case reel at round start", Dirty = dirty.Contains("reveal") },
        ModeAdminEntry.Switch("spin", "Centre-text spin", live.Spin) with { Subtitle = "Classic text spin as fallback", Dirty = dirty.Contains("spin") },
        ModeAdminEntry.Switch("spec", "Spectator panel", live.SpectatorPanel) with { Subtitle = "Dead players see who they watch", Dirty = dirty.Contains("spec") },
        ModeAdminEntry.Stepper("listy", "List position", Math.Clamp(live.ListOffset, 0, ListOffsets - 1), 0, ListOffsets - 1) with
        {
            Subtitle = "Move the list down if the radar overlaps", Dirty = dirty.Contains("listy"),
        },
        new ModeAdminEntry("header", ModeAdminKind.Info, "Header text") { Subtitle = "Shown above the reel", Hint = "Random Perk · Round {round}", Wide = true },
        new ModeAdminEntry("cmds", ModeAdminKind.Info, "Player commands")
        {
            Group = "Players",
            Subtitle = "!rolllist · !rollactive · !rollhelp in chat; active modifiers also under !arc → Info → This match",
            Wide = true,
        },
    ];

    private List<ModeAdminEntry> ConfigEntries(List<AdminChange> changes)
    {
        var entries = new List<ModeAdminEntry>();
        if (changes.Count == 0)
        {
            entries.Add(new ModeAdminEntry("chg", ModeAdminKind.Info, "Nothing changed since the last save") { Group = "Unsaved", Column = 1, Off = true });
        }

        for (var i = 0; i < Math.Min(changes.Count, ConfigLines); i++)
        {
            var text = changes.Count > ConfigLines && i == ConfigLines - 1 ? $"+ {changes.Count - i} more" : changes[i].Text;
            entries.Add(new ModeAdminEntry("chg" + i, ModeAdminKind.Info, text) { Group = "Unsaved", Column = 1, Dirty = true });
        }

        var path = Host.ConfigPath.Replace('\\', '/');
        var addons = path.IndexOf("addons/", StringComparison.OrdinalIgnoreCase);
        var configs = path.IndexOf("configs/", StringComparison.OrdinalIgnoreCase);
        var shown = configs >= 0 ? path[configs..] : addons >= 0 ? path[addons..] : path;
        var holders = core.PlayerManager.GetAllValidPlayers().Count(p => Runtime.GetModifiersForSlot(p.Slot).Count > 0);
        entries.Add(new ModeAdminEntry("file", ModeAdminKind.Info, "File " + shown) { Group = "Server", Column = 2 });
        entries.Add(new ModeAdminEntry("live", ModeAdminKind.Info,
            $"Live: round {Round()} · {Plural(holders, "player")} · {Plural(Runtime.ActiveModifiers.Count, "modifier")} active") { Group = "Server", Column = 2 });
        entries.Add(new ModeAdminEntry("ver", ModeAdminKind.Info, $"Plugin CSRoll v{Host.Version} · Arcfrag Random Perk") { Group = "Server", Column = 2 });
        if (Host.ConVarNote is { } note)
        {
            entries.Add(new ModeAdminEntry("cvar", ModeAdminKind.Info, note) { Group = "Server", Column = 2 });
        }

        entries.Add(ModeAdminEntry.Button("reload", "Reload from disk", "Drop every unsaved change and read config.jsonc again?") with
        {
            Group = "Server", Column = 2, Danger = true, Hint = "drops unsaved changes",
        });
        return entries;
    }

    public ModeAdminStatus? GetStatus()
    {
        var (_, changes) = Changes();
        return changes.Count == 0
            ? new ModeAdminStatus("Config in sync with disk")
            : new ModeAdminStatus($"{Plural(changes.Count, "unsaved change")} · live until the map changes", changes.Count);
    }

    // ---------------------------------------------------------------- changes

    public ModeAdminResult Toggle(string id, bool value, ModeAdminCaller caller)
    {
        var config = Config;
        switch (id)
        {
            case "rr":
                if (value == Runtime.RandomRoundsEnabled)
                {
                    return ModeAdminResult.Success($"Random rounds already {OnOff(value)}");
                }

                if (value && Runtime.RegisteredModifiers.Count == 0)
                {
                    return ModeAdminResult.Failure("No modifiers are enabled - enable some first");
                }

                Runtime.ToggleRandomRounds();
                Host.SyncConVar("csr_randomrounds");
                return ModeAdminResult.Success($"Random rounds {OnOff(value)}");
            case "rep":
                config.CanRepeat = value;
                return ModeAdminResult.Success($"Repeat last set {OnOff(value)}");
            case "warm":
                config.DisableRandomRoundsInWarmup = !value;
                return ModeAdminResult.Success($"Roll in warmup {OnOff(value)}");
            case "reveal":
                config.ShowCentreMsg = value;
                return ModeAdminResult.Success($"Roll reveal {OnOff(value)}");
            case "spin":
                config.SpinReveal.Enabled = value;
                return ModeAdminResult.Success($"Centre-text spin {OnOff(value)}");
            case "spec":
                config.SpectatorHud.Enabled = value;
                return ModeAdminResult.Success($"Spectator panel {OnOff(value)}");
            case "rar":
                config.Rarity.Enabled = value;
                Host.SyncConVar("csr_rarity");
                return ModeAdminResult.Success($"Rarity tiers {OnOff(value)}");
        }

        if (id.StartsWith("m:", StringComparison.Ordinal))
        {
            var name = id[2..];
            var display = DisplayName(name);
            var registered = Runtime.IsModifierRegisteredByName(name);
            if (value == registered)
            {
                return ModeAdminResult.Success($"{display} already {OnOff(value)}");
            }

            if (!value)
            {
                var wasActive = Runtime.IsModifierActive(Runtime.GetRegisteredModifierByName(name));
                return Runtime.DisableModifierByName(name, out var message)
                    ? ModeAdminResult.Success(wasActive ? $"{display} off and removed from everyone holding it" : $"{display} off")
                    : ModeAdminResult.Failure(message);
            }

            return Runtime.EnableModifierByName(name, out var error) ? ModeAdminResult.Success($"{display} can roll again") : ModeAdminResult.Failure(error);
        }

        return ModeAdminResult.Unsupported(id);
    }

    public ModeAdminResult Step(string id, double value, ModeAdminCaller caller)
    {
        var config = Config;
        var n = (int)Math.Round(value);
        switch (id)
        {
            case "min":
                Runtime.MinRandomRounds = Math.Clamp(n, 0, Runtime.MaxRandomRounds);
                Host.SyncConVar("csr_minrounds");
                return ModeAdminResult.Success($"Min modifiers: {Runtime.MinRandomRounds}");
            case "max":
                Runtime.MaxRandomRounds = Math.Clamp(n, Runtime.MinRandomRounds, Math.Max(MaxRounds, Runtime.MaxRandomRounds));
                Host.SyncConVar("csr_maxrounds");
                return ModeAdminResult.Success($"Max modifiers: {Runtime.MaxRandomRounds}");
            case "cd":
                config.PerPlayerRepeatCooldownRounds = Math.Clamp(n, 0, Math.Max(MaxCooldown, config.PerPlayerRepeatCooldownRounds));
                return ModeAdminResult.Success($"Cooldown: {config.PerPlayerRepeatCooldownRounds}");
            case "listy":
                config.CustomHud.ListOffset = Math.Clamp(n, 0, ListOffsets - 1);
                return ModeAdminResult.Success($"List position: {config.CustomHud.ListOffset}");
        }

        if (id.StartsWith("w:", StringComparison.Ordinal))
        {
            foreach (var tier in ModifierRarity.Tiers)
            {
                if (ModifierRarity.CssKey(tier) == id[2..])
                {
                    var weight = Math.Clamp((float)Math.Round(value), 0f, MaxWeight);
                    AdminSnapshot.SetWeight(tier, config.Rarity, weight);
                    return ModeAdminResult.Success($"{ModifierRarity.Label(tier)} weight: {AdminSnapshot.FormatWeight(weight)}");
                }
            }
        }

        return ModeAdminResult.Unsupported(id);
    }

    public ModeAdminResult Choose(string id, int option, ModeAdminCaller caller)
    {
        if (id != "mode" || option is < 0 or > 2)
        {
            return ModeAdminResult.Unsupported(id);
        }

        var mode = (ModifierRollMode)option;
        if (mode == Runtime.RollMode)
        {
            return ModeAdminResult.Success($"Roll mode already {mode}");
        }

        Config.RollMode = mode.ToString();
        Config.RandomizePlayers = null;
        Host.AnnounceRollMode(mode);
        Host.SyncConVar("csr_rollmode");
        return ModeAdminResult.Success($"Roll mode: {mode}");
    }

    /// <summary>A click on a modifier's tier: one tier up, Gold wrapping to Mil-Spec; back at the built-in tier the override goes.</summary>
    public ModeAdminResult Cycle(string id, ModeAdminCaller caller)
    {
        if (!id.StartsWith("m:", StringComparison.Ordinal))
        {
            return ModeAdminResult.Unsupported(id);
        }

        var name = id[2..];
        var rarity = Config.Rarity;
        var next = (ModifierTier)(((int)ModifierRarity.Resolve(name, rarity) + 1) % ModifierRarity.Tiers.Length);
        rarity.Overrides ??= new Dictionary<string, string>();
        foreach (var key in rarity.Overrides.Keys.Where(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            rarity.Overrides.Remove(key);
        }

        if (next != ModifierRarity.BuiltInTier(name))
        {
            rarity.Overrides[name] = next.ToString();
        }

        // The tier cache is keyed on the config object, which this edit doesn't replace.
        ModifierRarity.Invalidate();
        return ModeAdminResult.Success($"{DisplayName(name)} moved to {ModifierRarity.Label(next)}");
    }

    public ModeAdminResult Action(string id, ModeAdminCaller caller)
    {
        switch (id)
        {
            case "save":
            {
                var (live, changes) = Changes();
                if (changes.Count == 0)
                {
                    return ModeAdminResult.Success("Nothing to save - config.jsonc already matches");
                }

                return Host.Save(live, out var error)
                    ? ModeAdminResult.Success($"Saved {Plural(changes.Count, "change")} to config.jsonc")
                    : ModeAdminResult.Failure($"Couldn't save: {error} - changes are still live");
            }
            case "reload":
                Host.ReloadFromDisk();
                return ModeAdminResult.Success("Reloaded config.jsonc from disk");
            case "reroll":
                if (!Runtime.RandomRoundsEnabled)
                {
                    return ModeAdminResult.Failure("Random rounds are off - switch them on to re-roll");
                }

                if (Runtime.RegisteredModifiers.Count == 0)
                {
                    return ModeAdminResult.Failure("No modifiers are enabled - enable some first");
                }

                // Exactly !rollmenu's Re-roll: the roll is stashed and committed when the reveal lands.
                Runtime.RemoveAllModifiers();
                Runtime.ApplyRandomRoundsForRound(showBanner: false);
                Runtime.PlaySpinThenRevealActiveModifiersBanner();
                return ModeAdminResult.Success("Rerolled · everyone gets new modifiers");
            case "clear":
                Runtime.RemoveAllModifiers();
                return ModeAdminResult.Success("Removed every active modifier");
            case "allon":
            case "alloff":
            {
                var enable = id == "allon";
                var changed = 0;
                foreach (var modifier in Catalog())
                {
                    var registered = Runtime.IsModifierRegisteredByName(modifier.Name);
                    if (enable && !registered && Runtime.EnableModifierByName(modifier.Name, out _))
                    {
                        changed++;
                    }
                    else if (!enable && registered && Runtime.DisableModifierByName(modifier.Name, out _))
                    {
                        changed++;
                    }
                }

                return !enable && Runtime.RegisteredModifiers.Count == 0
                    ? ModeAdminResult.Success("Every modifier is off - random rounds have nothing to roll")
                    : ModeAdminResult.Success(changed == 0 ? $"All modifiers were already {(enable ? "on" : "off")}" : $"{(enable ? "Enabled" : "Disabled")} {Plural(changed, "modifier")}");
            }
            case "tierreset":
                Config.Rarity.Overrides = new Dictionary<string, string>();
                ModifierRarity.Invalidate();
                return ModeAdminResult.Success("Every modifier is back in its built-in tier");
        }

        return ModeAdminResult.Unsupported(id);
    }

    // ---------------------------------------------------------------- !arc → Info → This match

    /// <summary>
    /// What <c>!rollactive</c> shows, as text: the round and roll settings, the viewer's modifiers, everyone else's (or one
    /// line in Game mode), and where the full list is.
    /// </summary>
    public IReadOnlyList<string> GetMatchInfo(int slot)
    {
        var lines = new List<string>();
        var maxRounds = 0;
        try
        {
            maxRounds = core.ConVar.Find<int>("mp_maxrounds")?.Value ?? 0;
        }
        catch (Exception)
        {
            // No such convar: "Round 7" without "of".
        }

        var min = Runtime.MinRandomRounds;
        var max = Runtime.MaxRandomRounds;
        var perPlayer = min == max ? $"{Plural(max, "modifier")} per player" : $"{min} – {max} modifiers per player";
        lines.Add($"Round {Round()}{(maxRounds > 0 ? $" of {maxRounds}" : "")} · Random Perk · {perPlayer}");
        if (!Runtime.RandomRoundsEnabled)
        {
            lines.Add("Random rounds are off right now");
        }

        string Mods(IReadOnlyList<GameModifierBase> modifiers)
            => string.Join(" · ", modifiers.Select(m => Config.Rarity.Enabled
                ? $"{DisplayName(m.Name)} ({ModifierRarity.Label(ModifierRarity.Resolve(m, Config.Rarity))})"
                : DisplayName(m.Name)));

        var players = core.PlayerManager.GetAllValidPlayers().ToList();
        if (Runtime.RollMode == ModifierRollMode.Game)
        {
            var shared = Runtime.ActiveModifiers;
            lines.Add(shared.Count == 0 ? "Nothing rolled this round" : "Everyone: " + Mods(shared));
        }
        else
        {
            var own = Runtime.GetModifiersForSlot(slot);
            lines.Add(own.Count == 0 ? "You: nothing this round" : "You: " + Mods(own));
            foreach (var player in players.Where(p => p.Slot != slot).Take(12))
            {
                var theirs = Runtime.GetModifiersForSlot(player.Slot);
                if (theirs.Count > 0 && player.Controller is { IsValid: true } controller)
                {
                    lines.Add($"{CSRollUtils.PlainTextFromChatColors(controller.PlayerName)}: {Mods(theirs)}");
                }
            }
        }

        lines.Add("Next roll at round start · !rolllist shows every modifier");
        return lines;
    }

    // ---------------------------------------------------------------- data

    private (AdminSnapshot Live, List<AdminChange> Changes) Changes()
    {
        var live = AdminSnapshot.Capture(Runtime, Runtime.RandomRoundsEnabled,
            Runtime.KnownModifierNames.Where(name => !Runtime.IsModifierRegisteredByName(name)).ToList());
        return (live, live.Diff(Host.Baseline ?? live, DisplayName));
    }

    /// <summary>Every known modifier, sorted by display name, as its live instance where it's registered.</summary>
    private List<GameModifierBase> Catalog()
    {
        _catalog ??= [.. Runtime.GetAllKnownModifiers().OrderBy(m => CSRollUtils.GetModifierDisplayName(core, m), StringComparer.OrdinalIgnoreCase)];
        return _catalog.Select(m => Runtime.GetRegisteredModifierByName(m.Name) ?? m).ToList();
    }

    /// <summary>The runtime was rebuilt (plugin Load): the catalog is read again.</summary>
    public void Reset() => _catalog = null;

    private string DisplayName(string name)
        => Runtime.GetRegisteredModifierByName(name) is { } registered ? CSRollUtils.GetModifierDisplayName(core, registered)
            : _catalog?.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } known ? CSRollUtils.GetModifierDisplayName(core, known)
            : name;

    private string Short(GameModifierBase modifier)
    {
        try
        {
            return CSRollUtils.PlainTextFromChatColors(CSRollUtils.GetModifierShortDescription(core, modifier));
        }
        catch (Exception)
        {
            return Runtime.IsModifierRegistered(modifier) ? "" : "Switched off";
        }
    }

    private int Round() => core.EntitySystem.GetGameRules() is { } rules ? rules.TotalRoundsPlayed + 1 : 0;

    /// <summary>Whole percentages that add up to exactly 100 (or all 0): floors, then the largest remainders get the rest.</summary>
    internal static int[] LargestRemainder(float[] shares)
    {
        var floors = shares.Select(v => (int)MathF.Floor(Math.Max(0f, v))).ToArray();
        if (shares.Sum() <= 0f)
        {
            return floors;
        }

        var left = 100 - floors.Sum();
        foreach (var i in Enumerable.Range(0, shares.Length).OrderByDescending(i => shares[i] - floors[i]).Take(Math.Max(0, left)))
        {
            floors[i]++;
        }

        return floors;
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string OnOff(bool on) => on ? "on" : "off";
}
