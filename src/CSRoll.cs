using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared;

using CSRoll.Config;
using CSRoll.Core;
using CSRoll.Services.Impl;
using CSRoll.Services.Interfaces;

namespace CSRoll;

[PluginMetadata(Id = "CSRoll", Version = CSRoll.PluginVersion, Name = "CSRoll", Author = "lafkis", Description = "Apply game modifiers dynamically based on pre-defined classes or config files.")]
public partial class CSRoll : BasePlugin
{
    // Single source of truth for the version - also referenced in the PluginMetadata attribute
    // above and logged on every load, so the running build is always identifiable in the console.
    private const string PluginVersion = "1.37.3-arcfrag.1";

    private IServiceProvider _serviceProvider = null!;
    private ICvarRollbackService _cvarService = null!;
    private bool _isLoaded;
    private IDisposable? _configChangeSubscription;

    public CSRollConfig Config { get; private set; } = new();
    public ModifierRuntime Runtime { get; private set; } = null!;

    public CSRoll(ISwiftlyCore core) : base(core)
    {
    }

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
    {
    }

    /// <summary>ArcfragCore's mode HUD (Arcfrag UI addon), or null - kept here because Load() can rebuild Runtime.</summary>
    private Arcfrag.Core.Contract.IPlatformModeHud? _modeHud;

    /// <summary>
    /// Picks up ArcfragCore's IPlatformModeHud ("arcfrag:core_hud"). SwiftlyS2 runs this again whenever the plugin set
    /// changes (either plugin reloaded, ArcfragCore unloaded), so the value is always replaced, never kept. Without it (or
    /// while it reports Available = false) CSRoll draws its own center-HTML HUD exactly as before.
    /// </summary>
    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        try
        {
            _modeHud = TryGetModeHud(interfaceManager);
        }
        catch (Exception error) when (error is FileNotFoundException or FileLoadException or TypeLoadException)
        {
            // ArcfragCore.Contract is not loadable, or an older copy (before ArcfragCore 0.8.0, no IPlatformModeHud) won
            // SwiftlyS2's one-copy-per-name export load. Center-HTML fallback - but the modifiers' Arcfrag widgets need the
            // new contract too, so this has to be fixed by updating ArcfragCore.
            _modeHud = null;
            Core.Logger.LogError(error, "[CSRoll] ArcfragCore.Contract on this server has no IPlatformModeHud - update ArcfragCore to 0.8.0 or later and restart the server");
        }

        if (Runtime is not null)
        {
            Runtime.ModeHud = _modeHud;
        }

        Core.Logger.LogInformation(_modeHud is null
            ? "[CSRoll] ArcfragCore mode HUD not found - using center HTML"
            : "[CSRoll] ArcfragCore mode HUD found (arcfrag:core_hud) - Arcfrag UI panels when available, center HTML otherwise");
    }

    /// <summary>The only method naming the contract's interface in this class; not inlined, so a missing assembly fails here.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Arcfrag.Core.Contract.IPlatformModeHud? TryGetModeHud(IInterfaceManager interfaceManager)
        => interfaceManager.TryGetSharedInterface<Arcfrag.Core.Contract.IPlatformModeHud>(
            Arcfrag.Core.Contract.PlatformCoreCapabilities.PlatformModeHudName, out var hud) ? hud : null;

    public override void Load(bool hotReload)
    {
        // Bug fix: every command and game-event hook in this plugin doubled up on every single
        // action ("Enabled" immediately followed by "Disabled", etc.), even after switching off
        // attribute-based auto-registration and after a genuine full server restart. That rules
        // out stale state surviving a reload - the only way every single registration doubles is
        // if Load() itself runs twice for one logical plugin load (a file-system-watcher firing
        // two change events for a single file write is a common cause of exactly this). Rather
        // than depend on pinning down why the framework might call Load() twice, make Load()
        // idempotent: if it's ever invoked while already loaded, tear down first so there is
        // never more than one live registration for anything, regardless of the cause.
        if (_isLoaded)
        {
            Core.Logger.LogWarning("[CSRoll] Load() called while already loaded - unloading previous registrations first.");
            Unload();
        }

        _isLoaded = true;

        InitializeConfig();

        var services = new ServiceCollection();
        services.AddSwiftly(Core);
        services.AddSingleton(Config);
        services.AddSingleton<ICvarRollbackService, CvarRollbackService>();
        _serviceProvider = services.BuildServiceProvider();

        _cvarService = _serviceProvider.GetRequiredService<ICvarRollbackService>();
        _cvarService.Install();

        Runtime = new ModifierRuntime(Core, Config, _cvarService);
        Runtime.ModeHud = _modeHud;
        Runtime.Initialise(BuildModifierFactories());

        InitializeCommands();
        InitializeGameEvents();

        Core.Logger.LogInformation("[CSRoll] Successfully loaded! Version {Version} ({Count} modifiers registered)", PluginVersion, Runtime.RegisteredModifiers.Count);
    }

    public override void Unload()
    {
        _isLoaded = false;
        UninitializeCommands();
        UninitializeGameEvents();
        Runtime?.Unregister();
        _cvarService?.Uninstall();
        CSRollUtils.ClearXrayVision();

        // Bug fix: ChangeToken.OnChange's returned IDisposable used to be discarded - Load() is
        // documented to sometimes run twice (see its own comment above) and self-heals by calling
        // Unload() then re-running InitializeConfig(), so each occurrence installed one more
        // permanent config-reload subscription that was never removed. Disposing it here means a
        // fresh Load() always starts from zero subscriptions instead of accumulating one per reload.
        _configChangeSubscription?.Dispose();
        _configChangeSubscription = null;
    }

    private void InitializeConfig()
    {
        Core.Configuration.InitializeJsonWithModel<CSRollConfig>("config.jsonc", "Main")
            .Configure(builder => builder.AddJsonFile("config.jsonc", optional: false, reloadOnChange: true));

        ReloadConfigFromManager();
        _configChangeSubscription = ChangeToken.OnChange(() => Core.Configuration.Manager.GetReloadToken(), ReloadConfigFromManager);
    }

    private void ReloadConfigFromManager()
    {
        var newConfig = Core.Configuration.Manager.GetSection("Main").Get<CSRollConfig>() ?? new CSRollConfig();
        var disabledModifiersChanged = Runtime is not null &&
            !Config.DisabledModifiers.SequenceEqual(newConfig.DisabledModifiers, StringComparer.OrdinalIgnoreCase);

        Config = newConfig;
        CSRollUtils.SetTitlePrefix(newConfig.BannerText);
        if (Runtime is not null)
        {
            Runtime.Config = newConfig;

            // Bug fix: MinRandomRounds/MaxRandomRounds are separate mutable properties on
            // ModifierRuntime (not read live off Runtime.Config), previously kept in sync only by the
            // now-removed !minrandomrounds/!maxrandomrounds commands writing straight to them.
            // Now that config.jsonc is the sole source for these, a reload (hot-reload or !rollreload)
            // has to re-sync them explicitly here or an edited config.jsonc value would never actually
            // take effect on a running server, permanently stuck at whatever was live on last Load().
            Runtime.MinRandomRounds = newConfig.MinRandomRounds;
            Runtime.MaxRandomRounds = newConfig.MaxRandomRounds;
        }

        if (disabledModifiersChanged)
        {
            // Bug fix: this used to point admins at !reloadmodifiers, which has been removed (it only
            // rebuilt the registered-modifier list/pool - not something day-to-day admin usage needs,
            // since disabling a modifier for the rest of the session already happens live via
            // !disablemodifier). A DisabledModifiers edit in config.jsonc now requires a full plugin
            // reload (map change or server restart) to take effect - flagging that plainly instead of
            // pointing at a command that no longer exists.
            Core.Logger.LogWarning("[CSRoll] DisabledModifiers changed - this only takes effect on the next full plugin reload (map change/restart), not from config reload alone.");
        }
    }
}
