namespace CSRoll.Core;

/// <summary>
/// The log lines admins should see in the web admin (Servers → Console), handed to ArcfragCore's IPlatformConsole
/// (contract 0.11.0, ArcfragCore 0.18.0) next to the server log. Lines written before ArcfragCore's interface is there
/// or while it is missing wait here, the newest <see cref="MaxPending"/>.
/// </summary>
/// <remarks>
/// Knows only a delegate, not the contract type: with an older ArcfragCore (or none) IPlatformConsole does not exist
/// and CSRoll must still load. Safe from any thread; never throws.
/// No player IPs in the text; ArcfragCore removes IPs and secrets again before it keeps a line.
/// </remarks>
public sealed class GameLog
{
    /// <summary>The plugin slug in the web admin - the internal mode slug, kept as "csroll" for the stats.</summary>
    public const string Plugin = "csroll";

    public const int MaxPending = 50;

    private readonly object _gate = new();
    private readonly Queue<(string Level, string Text)> _pending = new();
    private Func<string, string, string, bool>? _write;

    /// <summary>Sets (or with null drops) ArcfragCore's Write(plugin, level, text) and hands it the waiting lines.</summary>
    public void Attach(Func<string, string, string, bool>? write)
    {
        lock (_gate)
        {
            _write = write;
            if (write is null) return;
            while (_pending.TryDequeue(out var line)) Send(write, line.Level, line.Text);
        }
    }

    public void Info(string text) => Write("info", text);

    public void Warn(string text) => Write("warn", text);

    public void Error(string text) => Write("error", text);

    private void Write(string level, string text)
    {
        lock (_gate)
        {
            if (_write is { } write)
            {
                Send(write, level, text);
                return;
            }
            if (_pending.Count == MaxPending) _pending.Dequeue();
            _pending.Enqueue((level, text));
        }
    }

    private static void Send(Func<string, string, string, bool> write, string level, string text)
    {
        // The game log is extra; it must never take a round, a modifier or a command down.
        try { write(Plugin, level, text); }
        catch (Exception) { }
    }
}
