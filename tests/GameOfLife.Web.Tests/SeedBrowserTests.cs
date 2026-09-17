using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GameOfLife.Web.Tests;

/// <summary>The pattern-loading tools live behind a toolbar button; their availability follows the shared state, their visibility the user's intent.</summary>
[Collection(WebCollection.Name)]
public sealed class SeedBrowserTests(WebAppFixture app) : IAsyncLifetime
{
    private readonly List<IPage> _pages = [];

    public Task InitializeAsync() => app.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var page in _pages) await page.Context.CloseAsync();
        await app.ResetAsync();
    }

    private async Task<IPage> OpenAsync()
    {
        var page = await app.NewPageAsync();
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connected");
        return page;
    }

    [Fact]
    public async Task The_seed_panel_opens_from_the_toolbar_and_closes_on_request()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        await Expect(page.Locator("#seed-panel")).ToHaveCountAsync(0);

        await page.Locator("#btn-seed").ClickAsync();

        await Expect(page.Locator("#seed-panel")).ToBeVisibleAsync();
        await Expect(page.Locator("#file")).ToBeEnabledAsync();
        await Expect(page.Locator("#link-editor")).ToHaveAttributeAsync("href", "/Editor");
        await Expect(page.Locator("#seed-blocked")).ToHaveCountAsync(0);

        await page.Locator("#seed-panel .btn-close").ClickAsync();
        await Expect(page.Locator("#seed-panel")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Seeding_waits_for_a_pause_and_says_so()
    {
        await app.Loop.StartAsync();
        var page = await OpenAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("running");

        await Expect(page.Locator("#btn-seed")).ToBeDisabledAsync();
        await page.Locator("#btn-seed-wrap").HoverAsync();
        await Expect(page.Locator(".tooltip")).ToContainTextAsync("Pause the simulation to load a pattern");

        await page.Locator("#btn-run").ClickAsync();
        await Expect(page.Locator("#btn-seed")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task An_open_panel_stays_open_but_disables_its_form_when_someone_starts()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        await page.Locator("#btn-seed").ClickAsync();
        await Expect(page.Locator("#file")).ToBeEnabledAsync();

        await app.Loop.StartAsync();

        await Expect(page.Locator("#seed-panel")).ToBeVisibleAsync();
        await Expect(page.Locator("#seed-blocked")).ToContainTextAsync("Pause the simulation");
        await Expect(page.Locator("#file")).ToBeDisabledAsync();
        await Expect(page.Locator("#btn-upload")).ToBeDisabledAsync();

        await app.Loop.PauseAsync();
        await Expect(page.Locator("#file")).ToBeEnabledAsync();
        await Expect(page.Locator("#seed-blocked")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Export_is_a_toolbar_link_that_is_blocked_only_while_editing()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        var export = page.Locator("#btn-export");
        await Expect(export).ToHaveAttributeAsync("href", "/?handler=Export");
        await Expect(export).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdisabled\b"));

        await page.Locator("#btn-edit").ClickAsync();
        await Expect(page.Locator("#edit-banner")).ToBeVisibleAsync();
        await Expect(export).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdisabled\b"));
        await Expect(export).ToHaveAttributeAsync("aria-disabled", "true");

        await page.Locator("#btn-edit-cancel").ClickAsync();
        await Expect(export).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdisabled\b"));
    }

    [Fact]
    public async Task Only_the_canvas_and_its_toolbars_are_visible_under_normal_conditions()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");

        await Expect(page.Locator(".card")).ToHaveCountAsync(0);
        await Expect(page.Locator("#seed-panel")).ToHaveCountAsync(0);
        await Expect(page.Locator("#edit-banner")).ToHaveCountAsync(0);
        await Expect(page.Locator("#file")).ToHaveCountAsync(0);
        await Expect(page.Locator(".viewer-toolbar")).ToHaveCountAsync(1);
        await Expect(page.Locator(".viewer-nav-pan")).ToBeVisibleAsync();
        await Expect(page.Locator(".viewer-nav-zoom")).ToBeVisibleAsync();
        await Expect(page.Locator("#universe")).ToBeVisibleAsync();
    }
}
