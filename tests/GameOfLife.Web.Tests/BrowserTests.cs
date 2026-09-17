using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GameOfLife.Web.Tests;

/// <summary>
/// What the page adds on top of the hub: initial state on connect, shared controls between
/// browsers, per-browser viewports. Logic lives in the Core tests; these stay few and slow.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class BrowserTests(WebAppFixture app) : IAsyncLifetime
{
    private readonly List<IPage> _pages = [];

    public Task InitializeAsync() => app.ResetAsync();

    public async Task DisposeAsync()
    {
        await app.Loop.PauseAsync();
        foreach (var page in _pages) await page.Context.CloseAsync();
    }

    /// <summary>A page in its own browser context (own connection, own viewport), on the universe page.</summary>
    private async Task<IPage> OpenAsync()
    {
        var page = await app.NewPageAsync();
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connected");
        return page;
    }

    [Fact]
    public async Task A_new_page_shows_the_paused_world_it_connected_to()
    {
        var page = await OpenAsync();

        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        await Expect(page.Locator("#btn-run")).ToBeEnabledAsync();
        await Expect(page.Locator("#btn-run")).ToHaveAttributeAsync("aria-label", "Start");
        await Expect(page.Locator("#status-generation")).ToHaveTextAsync("0");
        await Expect(page.Locator("#status-population")).ToHaveTextAsync("3");
        // The view is 100 cells across; its height follows the canvas's shape.
        await Expect(page.Locator("#status-viewport")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^100 × \d+$"));
    }

    [Fact]
    public async Task A_page_opened_while_running_shows_running()
    {
        await app.Loop.StartAsync();

        var page = await OpenAsync();

        await Expect(page.Locator("#status-running")).ToHaveTextAsync("running");
        await Expect(page.Locator("#btn-run")).ToBeEnabledAsync();
        await Expect(page.Locator("#btn-run")).ToHaveAttributeAsync("aria-label", "Pause");
        await Expect(page.Locator("#status-generation")).Not.ToHaveTextAsync("0");
    }

    [Fact]
    public async Task Start_in_one_browser_is_seen_in_another()
    {
        var first = await OpenAsync();
        var second = await OpenAsync();

        await first.Locator("#btn-run").ClickAsync();
        await Expect(second.Locator("#status-running")).ToHaveTextAsync("running");
        await Expect(second.Locator("#btn-run")).ToHaveAttributeAsync("aria-label", "Pause");

        await second.Locator("#btn-run").ClickAsync();
        await Expect(first.Locator("#status-running")).ToHaveTextAsync("paused");
        await Expect(first.Locator("#btn-run")).ToHaveAttributeAsync("aria-label", "Start");
    }

    [Fact]
    public async Task Zoom_changes_only_this_browsers_viewport()
    {
        var first = await OpenAsync();
        var second = await OpenAsync();

        await first.Locator("#btn-zoom-in").ClickAsync();

        await Expect(first.Locator("#status-viewport")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^80 × \d+$"));
        await Expect(first.Locator("#cells-across")).ToHaveValueAsync("80");
        await Expect(second.Locator("#status-viewport")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^100 × \d+$"));
    }

    [Fact]
    public async Task Controls_are_disabled_until_the_state_arrives()
    {
        var page = await app.NewPageAsync();
        _pages.Add(page);
        await page.RouteAsync("**/hubs/life/negotiate*", route => route.AbortAsync());

        await page.GotoAsync("/");

        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connection failed");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("–");
        await Expect(page.Locator("#btn-run")).ToBeDisabledAsync();
        await Expect(page.Locator("#speed")).ToBeDisabledAsync();
    }
}
