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
    private const string PluginVersion = "1.37.4";

    private IServiceProvider _serviceProvider = null!;
    private ICvarRollbackService _cvarService = null!;
    private bool _isLoaded;
    private IDisposable? _configChangeSubscription;

    public CSRollConfig Config { get; private set; } = new();

    /// <summary>Lines for the web admin (ArcfragCore's IPlatformConsole); kept across Load() so waiting lines survive a reload.</summary>
    public GameLog GameLog { get; } = new();
    public ModifierRuntime Runtime { get; private set; } = null!;

    public CSRoll(ISwiftlyCore core) : base(core)
    {
    }

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
    {
    }

    /// <summary>
    /// Picks up ArcfragCore's IPlatformConsole ("arcfrag:core_console", ArcfragCore 0.18.0). SwiftlyS2 runs this again
    /// whenever the plugin set changes, so the value is always replaced, never kept. Without it CSRoll runs as before,
    /// only the server log gets the lines.
    /// </summary>
    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        Func<string, string, string, bool>? console = null;
        try { console = PlatformConsole(interfaceManager); }
        catch (Exception error) when (error is FileNotFoundException or FileLoadException or TypeLoadException) { /* no ArcfragCore, or before 0.18.0 */ }
        GameLog.Attach(console);
        Core.Logger.LogInformation("[CSRoll] Web admin game log (arcfrag:core_console): {State}", console is null ? "off" : "on");
    }

    /// <summary>The only method naming IPlatformConsole (contract 0.11.0); not inlined, so a missing or older contract fails here and nowhere else.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Func<string, string, string, bool>? PlatformConsole(IInterfaceManager interfaceManager)
        => interfaceManager.TryGetSharedInterface<Arcfrag.Core.Contract.IPlatformConsole>(
               Arcfrag.Core.Contract.PlatformCoreCapabilities.PlatformConsoleName, out var console)
           && console is not null ? console.Write : null;

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
            GameLog.Warn("Load() called while already loaded - previous registrations unloaded first");
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

        Runtime = new ModifierRuntime(Core, Config, _cvarService, GameLog);
        Runtime.Initialise(BuildModifierFactories());

        InitializeCommands();
        InitializeGameEvents();

        Core.Logger.LogInformation("[CSRoll] Successfully loaded! Version {Version} ({Count} modifiers registered)", PluginVersion, Runtime.RegisteredModifiers.Count);
        GameLog.Info($"Random Perk (CSRoll {PluginVersion}) loaded, {Runtime.RegisteredModifiers.Count} modifiers registered, "
            + $"random rounds {(Runtime.RandomRoundsEnabled ? "on" : "off")}");
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
        GameLog.Info("Random Perk (CSRoll) unloaded");
    }

    private void InitializeConfig()
    {
        Core.Configuration.InitializeJsonWithModel<CSRollConfig>("config.jsonc", "Main")
            .Configure(builder => builder.AddJsonFile("config.jsonc", optional: false, reloadOnChange: true));

        ReloadConfigFromManager();
        _configChangeSubscription = ChangeToken.OnChange(() => Core.Configuration.Manager.GetReloadToken(), OnConfigFileChanged);
    }

    /// <summary>A config.jsonc edit on a running server: a value that does not bind keeps the old config instead of failing silently.</summary>
    private void OnConfigFileChanged()
    {
        try
        {
            ReloadConfigFromManager();
        }
        catch (Exception error)
        {
            Core.Logger.LogError(error, "[CSRoll] config.jsonc reload failed - keeping the previous config");
            GameLog.Error($"config.jsonc reload failed, keeping the previous config: {error.Message}");
        }
    }

    private void ReloadConfigFromManager()
    {
        var newConfig = Core.Configuration.Manager.GetSection("Main").Get<CSRollConfig>() ?? new CSRollConfig();
        var disabledModifiersChanged = Runtime is not null &&
            !Config.DisabledModifiers.SequenceEqual(newConfig.DisabledModifiers, StringComparer.OrdinalIgnoreCase);

        if (Runtime is not null)
        {
            // Only reloads of a running plugin; the first read in Load() is covered by the "loaded" line.
            GameLog.Info($"Config reloaded: {newConfig.MinRandomRounds}-{newConfig.MaxRandomRounds} modifiers per round, "
                + $"{newConfig.DisabledModifiers.Length} disabled");
        }

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
            GameLog.Warn("DisabledModifiers changed - takes effect on the next full plugin reload (map change/restart)");
        }
    }
}
