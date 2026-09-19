using GameOfLife.Web.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GameOfLife.Web.Tests;

/// <summary>
/// A background tab costs the server nothing: the page reports the Page Visibility state and the
/// hub stops sending frames to it until it is shown again. Headless Chromium cannot actually hide
/// a tab, so the tests replace <c>document.hidden</c> / <c>visibilityState</c> and raise
/// <c>visibilitychange</c>, which is all the page listens to.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class VisibilityBrowserTests(WebAppFixture app) : IAsyncLifetime
{
    private readonly List<IPage> _pages = [];

    public Task InitializeAsync() => app.ResetAsync();

    public async Task DisposeAsync()
    {
        await app.Loop.PauseAsync();
        foreach (var page in _pages) await page.Context.CloseAsync();
    }

    private async Task<IPage> OpenAsync()
    {
        var page = await app.NewPageAsync();
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connected");
        await SettledAsync(page);
        return page;
    }

    /// <summary>
    /// Shortly after the first frame the page asks for a view height matching the canvas, and the
    /// answer to that (like any hub call the page makes itself) carries the current frame. Wait for
    /// it, so that a frozen HUD in these tests can only mean the broadcast stopped.
    /// </summary>
    private static Task SettledAsync(IPage page) =>
        Expect(page.Locator("#status-viewport")).Not.ToHaveTextAsync("100 × 100");

    private static Task SetHiddenAsync(IPage page, bool hidden) => page.EvaluateAsync(
        """
        (hidden) => {
          Object.defineProperty(document, "hidden", { value: hidden, configurable: true });
          Object.defineProperty(document, "visibilityState", { value: hidden ? "hidden" : "visible", configurable: true });
          document.dispatchEvent(new Event("visibilitychange"));
        }
        """, hidden);

    private static async Task<ulong> GenerationAsync(IPage page) =>
        ulong.Parse(await page.Locator("#status-generation").InnerTextAsync());

    /// <summary>The HUD generation once two readings 300 ms apart agree.</summary>
    private static async Task<ulong> FrozenGenerationAsync(IPage page)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var last = await GenerationAsync(page);
        while (true)
        {
            await Task.Delay(300, cts.Token);
            var now = await GenerationAsync(page);
            if (now == last) return now;
            last = now;
        }
    }

    [Fact]
    public async Task A_hidden_tab_stops_receiving_frames_and_catches_up_when_shown()
    {
        var page = await OpenAsync();
        await page.ClickAsync("#btn-run");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("running");
        await Expect(page.Locator("#status-generation")).Not.ToHaveTextAsync("0");

        await SetHiddenAsync(page, true);
        // Frames in flight when the tab went hidden may still land (the first ones of a run are slow
        // while the server warms up); wait until the counter has stopped moving, then hold it there.
        var frozen = await FrozenGenerationAsync(page);
        await Task.Delay(700);
        Assert.Equal(frozen, await GenerationAsync(page));
        Assert.True(app.Loop.Current.Generation > frozen, "the simulation should have run on without the tab");

        await SetHiddenAsync(page, false);
        await Expect(page.Locator("#status-generation")).Not.ToHaveTextAsync(frozen.ToString());
        var shown = await GenerationAsync(page);
        Assert.True(shown > frozen);
        // And it keeps moving.
        await Expect(page.Locator("#status-generation")).Not.ToHaveTextAsync(shown.ToString());
    }

    [Fact]
    public async Task Other_tabs_keep_updating_while_one_is_hidden()
    {
        var hidden = await OpenAsync();
        var visible = await OpenAsync();
        await SetHiddenAsync(hidden, true);
        await Task.Delay(200);

        await visible.ClickAsync("#btn-run");
        await Expect(visible.Locator("#status-generation")).Not.ToHaveTextAsync("0");
        var before = await GenerationAsync(visible);
        await Expect(visible.Locator("#status-generation")).Not.ToHaveTextAsync(before.ToString());

        // The hidden tab never learned that the simulation started.
        await Expect(hidden.Locator("#status-running")).ToHaveTextAsync("paused");
        await Expect(hidden.Locator("#status-generation")).ToHaveTextAsync("0");
    }

    [Fact]
    public async Task A_tab_opened_in_the_background_receives_nothing_until_shown()
    {
        var page = await app.NewPageAsync();
        _pages.Add(page);
        // Hidden before the page's script runs, so the hub is told right after the connection is initialised.
        await page.AddInitScriptAsync(
            """
            Object.defineProperty(document, "hidden", { value: true, configurable: true });
            Object.defineProperty(document, "visibilityState", { value: "hidden", configurable: true });
            """);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connected");
        await SettledAsync(page);
        await Expect(page.Locator("#status-generation")).ToHaveTextAsync("0");
        // "connected" shows as soon as the handshake is done, which is before the hub has registered
        // the connection, let alone been told it is hidden; wait for both.
        var viewports = app.Services.GetRequiredService<ClientViewports>();
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            while (viewports.Count != 1 || viewports.VisibleCount != 0) await Task.Delay(50, cts.Token);

        await app.Loop.StartAsync();
        await Task.Delay(700);
        await Expect(page.Locator("#status-generation")).ToHaveTextAsync("0");
        Assert.True(app.Loop.Current.Generation > 5);

        await SetHiddenAsync(page, false);
        await Expect(page.Locator("#status-generation")).Not.ToHaveTextAsync("0");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("running");
    }
}
