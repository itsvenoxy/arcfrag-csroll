using Arcfrag.Core.Contract;

using CSRoll.Core;

namespace CSRoll.Tests;

public class GameLogTests
{
    private sealed class FakeConsole : IPlatformConsole
    {
        public List<(string Plugin, string Level, string Text)> Lines { get; } = [];

        public bool Write(string plugin, string level, string text)
        {
            Lines.Add((plugin, level, text));
            return true;
        }
    }

    [Fact]
    public void Lines_go_to_the_core_console_as_csroll()
    {
        var console = new FakeConsole();
        var log = new GameLog();
        log.Attach(console.Write);
        log.Info("loaded");
        log.Warn("no modifiers");
        log.Error("config invalid");
        Assert.Equal([("csroll", "info", "loaded"), ("csroll", "warn", "no modifiers"), ("csroll", "error", "config invalid")], console.Lines);
    }

    [Fact]
    public void Lines_before_the_interface_wait_and_come_in_order()
    {
        var console = new FakeConsole();
        var log = new GameLog();
        log.Info("loaded");
        log.Error("reload failed");
        Assert.Empty(console.Lines);
        log.Attach(console.Write);
        Assert.Equal(["loaded", "reload failed"], console.Lines.Select(l => l.Text));
        log.Attach(console.Write);
        Assert.Equal(2, console.Lines.Count);
    }

    [Fact]
    public void Only_the_newest_lines_wait()
    {
        var console = new FakeConsole();
        var log = new GameLog();
        for (var i = 0; i < GameLog.MaxPending + 10; i++) log.Info("line " + i);
        log.Attach(console.Write);
        Assert.Equal(GameLog.MaxPending, console.Lines.Count);
        Assert.Equal("line 10", console.Lines[0].Text);
        Assert.Equal("line " + (GameLog.MaxPending + 9), console.Lines[^1].Text);
    }

    [Fact]
    public void Without_core_lines_wait_again()
    {
        var console = new FakeConsole();
        var log = new GameLog();
        log.Attach(console.Write);
        log.Attach(null);
        log.Info("while core reloads");
        Assert.Empty(console.Lines);
        log.Attach(console.Write);
        Assert.Equal("while core reloads", Assert.Single(console.Lines).Text);
    }

    [Fact]
    public void A_failing_console_never_throws()
    {
        var log = new GameLog();
        log.Attach((_, _, _) => throw new InvalidOperationException("boom"));
        log.Error("still fine");
    }

    [Fact]
    public void Contract_has_the_console_key()
        => Assert.Equal("arcfrag:core_console", PlatformCoreCapabilities.PlatformConsoleName);
}
