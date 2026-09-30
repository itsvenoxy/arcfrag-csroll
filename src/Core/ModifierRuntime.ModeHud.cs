using Arcfrag.Core.Contract;

using Microsoft.Extensions.Logging;

using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

using CSRoll.Modifiers;

namespace CSRoll.Core;

/// <summary>
/// The Arcfrag UI HUD route. When ArcfragCore offers <see cref="IPlatformModeHud"/> (shared interface
/// <c>arcfrag:core_hud</c>) and it is <see cref="IPlatformModeHud.Available"/>, the roll reveal, the late-modifier card, the
/// spectator HUD and the per-modifier HUD go to its Panorama panels instead of center HTML: modifiers publish a structured
/// status next to their HTML block (<see cref="SetStatusSection"/>), and this composes one modifier bar per player.
/// Without it everything stays exactly as before (center HTML).
/// </summary>
public sealed partial class ModifierRuntime
{
    private IPlatformModeHud? _modeHud;

    /// <summary>Set from CSRoll.UseSharedInterface; null when ArcfragCore is not loaded.</summary>
    public IPlatformModeHud? ModeHud
    {
        get => _modeHud;
        set
        {
            if (!ReferenceEquals(_modeHud, value))
            {
                _modeBarSent.Clear();
                _modeSpecShown.Clear();
                _lastModeSpecUpdateTime.Clear();
            }

            _modeHud = value;
        }
    }

    /// <summary>True when the Arcfrag UI HUD draws CSRoll's HUD on this server (checked live: the addon or HudModes can change on a reload).</summary>
    public bool UseModeHud
    {
        get
        {
            try
            {
                return _modeHud is { Available: true };
            }
            catch (Exception)
            {
                // ArcfragCore unloaded under us; its adapter is gone until UseSharedInterface runs again.
                return false;
            }
        }
    }

    /// <summary>How often the modifier bars are recomposed - ArcfragCore only sends what changed, so this is cheap.</summary>
    private const float ModeHudRefreshIntervalSeconds = 0.1f;

    /// <summary>When the reel of the Arcfrag roll reveal lands on the winner - activation is committed then, as it is when the center-HTML spin lands.</summary>
    private const float ModeRollLandSeconds = 1.55f;

    private sealed record StatusSection(ModeHudWidget Widget, int Priority, float ExpiresAt);

    /// <summary>Per-slot, per-modifier structured status - the Arcfrag HUD twin of _hudSections, same TTL semantics.</summary>
    private readonly Dictionary<int, Dictionary<GameModifierBase, StatusSection>> _statusSections = [];

    private readonly HashSet<int> _modeBarSent = [];
    private readonly HashSet<int> _modeSpecShown = [];
    private readonly Dictionary<int, float> _lastModeSpecUpdateTime = [];
    private float _lastModeHudRefreshTime = float.NegativeInfinity;

    /// <summary>Publishes (or replaces) one modifier's widget for one player. Cheap enough to call every tick; expires like an HTML block.</summary>
    public void SetStatusSection(GameModifierBase owner, int slot, ModeHudWidget widget, int priority = 0)
    {
        if (!_statusSections.TryGetValue(slot, out var sections))
        {
            sections = [];
            _statusSections[slot] = sections;
        }

        sections[owner] = new StatusSection(widget, priority, _core.Engine.GlobalVars.CurrentTime + HudSectionTtlSeconds);
    }

    private void ClearStatusSection(GameModifierBase owner, int slot)
    {
        if (_statusSections.TryGetValue(slot, out var sections) && sections.Remove(owner) && sections.Count == 0)
        {
            _statusSections.Remove(slot);
        }
    }

    private void ForgetModeHudSlot(int slot)
    {
        _statusSections.Remove(slot);
        _modeBarSent.Remove(slot);
        _modeSpecShown.Remove(slot);
        _lastModeSpecUpdateTime.Remove(slot);
    }

    /// <summary>A one-off announcement as a toast in the Arcfrag HUD's toast stack. False when the HUD is not there - the caller keeps its chat line then.</summary>
    public bool TryToast(int slot, string title, string message, ModeHudTone tone) => TryToast(slot, title, message, tone.ToString());

    public bool TryToast(int slot, string title, string message, string tone)
    {
        if (!UseModeHud)
        {
            return false;
        }

        try
        {
            _modeHud!.Toast(slot, title, message, tone);
            return true;
        }
        catch (Exception error)
        {
            _core.Logger.LogWarning(error, "[CSRoll] Arcfrag HUD toast failed");
            return false;
        }
    }

    /// <summary>The roll reveal on the Arcfrag HUD for one player. <paramref name="late"/>: the gold "new modifier" card without the reel.</summary>
    private void ShowModeRoll(int slot, IReadOnlyCollection<GameModifierBase> modifiers, bool late)
    {
        if (modifiers.Count == 0)
        {
            return;
        }

        try
        {
            var cards = modifiers.Take(3).Select(m => ModifierLook.ToHud(_core, m)).ToList();
            var reel = _registeredModifiers.Select(m => CSRollUtils.PlainTextFromChatColors(CSRollUtils.GetModifierDisplayName(_core, m))).ToList();
            var round = _core.EntitySystem.GetGameRules() is { } rules ? rules.TotalRoundsPlayed + 1 : 0;
            _modeHud!.ShowRoll(slot, new ModeHudRoll(round, cards, late, reel));
        }
        catch (Exception error)
        {
            _core.Logger.LogWarning(error, "[CSRoll] Arcfrag HUD roll failed for slot {Slot}", slot);
        }
    }

    /// <summary>
    /// Per-player roll on the Arcfrag HUD: the reel plays client-side-paced by ArcfragCore, the modifiers are committed when
    /// it lands (same moment as the center-HTML spin's landing), so the effect and the reveal still arrive together.
    /// </summary>
    private void PlayModeRoll(int slot, IReadOnlyCollection<GameModifierBase> modifiers, Action onRevealed)
    {
        ShowModeRoll(slot, modifiers, late: false);
        _core.Scheduler.DelayBySeconds(ModeRollLandSeconds, onRevealed);
    }

    /// <summary>Broadcast twin of <see cref="PlayModeRoll"/> for the global (non-RandomizePlayers) roll.</summary>
    private void PlayModeRollAll(IReadOnlyCollection<GameModifierBase> modifiers, Action onRevealed)
    {
        foreach (var player in _core.PlayerManager.GetAllValidPlayers())
        {
            ShowModeRoll(player.Slot, modifiers, late: false);
        }

        _core.Scheduler.DelayBySeconds(ModeRollLandSeconds, onRevealed);
    }

    /// <summary>
    /// One modifier bar per player: the chip lists the modifiers that apply to them, the widgets are the live status
    /// sections (highest priority first, at most 3). Dead or spectating players get no bar - the spectator panel takes
    /// the spot. Only players who were sent a bar get the null that hides it.
    /// </summary>
    private void RefreshModeHudBars()
    {
        var now = _core.Engine.GlobalVars.CurrentTime;

        // Map change: CurrentTime restarted near zero (see ResetMapRelativeTimeState).
        if (now < _lastModeHudRefreshTime)
        {
            _lastModeHudRefreshTime = float.NegativeInfinity;
        }

        if (now - _lastModeHudRefreshTime < ModeHudRefreshIntervalSeconds)
        {
            return;
        }

        _lastModeHudRefreshTime = now;

        foreach (var (slot, sections) in _statusSections.ToList())
        {
            foreach (var (owner, section) in sections.ToList())
            {
                if (now >= section.ExpiresAt || now + HudSectionTtlSeconds < section.ExpiresAt)
                {
                    sections.Remove(owner);
                }
            }

            if (sections.Count == 0)
            {
                _statusSections.Remove(slot);
            }
        }

        foreach (var player in _core.PlayerManager.GetAllValidPlayers())
        {
            var slot = player.Slot;
            var bar = BuildModeBar(player);
            if (bar is null && !_modeBarSent.Contains(slot))
            {
                continue;
            }

            try
            {
                _modeHud!.SetModBar(slot, bar);
            }
            catch (Exception error)
            {
                _core.Logger.LogWarning(error, "[CSRoll] Arcfrag HUD modifier bar failed for slot {Slot}", slot);
                continue;
            }

            if (bar is null)
            {
                _modeBarSent.Remove(slot);
            }
            else
            {
                _modeBarSent.Add(slot);
            }
        }
    }

    private ModeHudBar? BuildModeBar(IPlayer player)
    {
        if (player.Controller is not { IsValid: true, PawnIsAlive: true })
        {
            return null;
        }

        var modifiers = GetModifiersForSlot(player.Slot);
        var widgets = _statusSections.TryGetValue(player.Slot, out var sections)
            ? sections
                .OrderByDescending(entry => entry.Value.Priority)
                .ThenBy(entry => entry.Key.Name, StringComparer.Ordinal)
                .Take(3)
                .Select(entry => entry.Value.Widget)
                .ToList()
            : [];

        if (modifiers.Count == 0 && widgets.Count == 0)
        {
            return null;
        }

        // The chip needs no descriptions; skipping them keeps the 10 Hz pass free of translation lookups.
        return new ModeHudBar(modifiers.Select(m => ModifierLook.ToHud(_core, m, withDescription: false)).ToList(), widgets);
    }

    /// <summary>
    /// The spectator panel on the Arcfrag HUD: who a dead/spectating player watches and that player's modifiers. Same
    /// throttle as the center-HTML spectator HUD; ArcfragCore animates a target switch.
    /// </summary>
    private void RefreshModeHudSpectators()
    {
        var enabled = Config.SpectatorHud.Enabled && _activeModifiers.Count > 0;
        var now = _core.Engine.GlobalVars.CurrentTime;

        foreach (var player in _core.PlayerManager.GetAllValidPlayers())
        {
            var slot = player.Slot;
            var target = enabled ? GetSpectatorTarget(player) : null;

            if (target is null)
            {
                if (_modeSpecShown.Remove(slot))
                {
                    _lastModeSpecUpdateTime.Remove(slot);
                    TrySetSpectator(slot, null);
                }

                continue;
            }

            if (_lastModeSpecUpdateTime.TryGetValue(slot, out var lastUpdate) && now >= lastUpdate &&
                now - lastUpdate < Config.SpectatorHud.RefreshIntervalSeconds)
            {
                continue;
            }

            _lastModeSpecUpdateTime[slot] = now;

            var (targetPlayer, controller) = target.Value;
            var modifiers = GetModifiersForSlot(targetPlayer.Slot).Take(3).Select(m => ModifierLook.ToHud(_core, m)).ToList();
            var team = controller.Team switch
            {
                SwiftlyS2.Shared.Players.Team.T => "T",
                SwiftlyS2.Shared.Players.Team.CT => "CT",
                _ => "",
            };
            var health = targetPlayer.PlayerPawn is { IsValid: true } pawn ? pawn.Health : 0;
            var meta = string.Join(" · ", new[] { team, $"{Math.Max(0, health)} HP" }.Where(part => part.Length > 0));

            if (TrySetSpectator(slot, new ModeHudSpectator(controller.PlayerName, meta, modifiers)))
            {
                _modeSpecShown.Add(slot);
            }
        }
    }

    /// <summary>The player a dead/spectating player currently observes (see RefreshSpectatorHud for why IPlayer.Pawn, not PlayerPawn).</summary>
    private (IPlayer Player, CCSPlayerController Controller)? GetSpectatorTarget(IPlayer player)
    {
        if (player.Pawn?.ObserverServices?.ObserverTarget.Value is not { IsValid: true } targetEntity)
        {
            return null;
        }

        var targetPlayer = _core.PlayerManager.GetPlayerFromPawn(targetEntity.As<CBasePlayerPawn>());
        if (targetPlayer is not { IsValid: true, Controller: { IsValid: true } controller } || targetPlayer.Slot == player.Slot)
        {
            return null;
        }

        return (targetPlayer, controller);
    }

    private bool TrySetSpectator(int slot, ModeHudSpectator? spectator)
    {
        try
        {
            _modeHud!.SetSpectator(slot, spectator);
            return true;
        }
        catch (Exception error)
        {
            _core.Logger.LogWarning(error, "[CSRoll] Arcfrag HUD spectator panel failed for slot {Slot}", slot);
            return false;
        }
    }
}
