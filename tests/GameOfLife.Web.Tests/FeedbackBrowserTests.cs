using GameOfLife.Core;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GameOfLife.Web.Tests;

/// <summary>Feedback the page gives about itself: theme, notices, explained-disabled controls, run-state styling, canvas overlays.</summary>
[Collection(WebCollection.Name)]
public sealed class FeedbackBrowserTests(WebAppFixture app) : IAsyncLifetime
{
    private readonly List<IPage> _pages = [];

    public Task InitializeAsync() => app.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var page in _pages) await page.Context.CloseAsync();
        await app.ResetAsync();
    }

    private async Task<IPage> OpenAsync(ColorScheme? scheme = null)
    {
        var context = await app.Browser.NewContextAsync(new()
        {
            BaseURL = app.BaseAddress.ToString(),
            ColorScheme = scheme,
        });
        var page = await context.NewPageAsync();
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connected");
        return page;
    }

    [Fact]
    public async Task The_page_follows_the_dark_colour_scheme_preference()
    {
        var page = await OpenAsync(ColorScheme.Dark);

        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "dark");
        var background = await page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");
        Assert.NotEqual("rgb(255, 255, 255)", background);

        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "light");
    }

    [Fact]
    public async Task The_theme_button_cycles_light_dark_and_system_and_remembers_the_choice()
    {
        var page = await OpenAsync(ColorScheme.Light);
        var html = page.Locator("html");
        var button = page.Locator("#btn-theme");
        await Expect(html).ToHaveAttributeAsync("data-bs-theme", "light");
        await Expect(button).ToHaveAttributeAsync("aria-label", "Theme: follow the system");

        // The tooltip follows the state while the pointer stays on the button.
        await button.HoverAsync();
        await Expect(page.GetByRole(AriaRole.Tooltip, new() { Name = "Click for light" })).ToBeVisibleAsync();
        await button.ClickAsync();
        await Expect(html).ToHaveAttributeAsync("data-bs-theme", "light");
        await Expect(button).ToHaveAttributeAsync("aria-label", "Theme: light");
        await Expect(page.GetByRole(AriaRole.Tooltip, new() { Name = "Click for dark" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Tooltip, new() { Name = "Click for light" })).ToHaveCountAsync(0);

        await button.ClickAsync();
        await Expect(html).ToHaveAttributeAsync("data-bs-theme", "dark");
        await Expect(button).ToHaveAttributeAsync("aria-label", "Theme: dark");
        await Expect(page.GetByRole(AriaRole.Tooltip, new() { Name = "Click for follow the system" })).ToBeVisibleAsync();

        // The choice survives a reload, on every page, and is applied before the bundles run.
        await page.GotoAsync("/Editor");
        await Expect(html).ToHaveAttributeAsync("data-bs-theme", "dark");
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connected");
        await Expect(html).ToHaveAttributeAsync("data-bs-theme", "dark");

        // Back to following the system, which is light here.
        await page.Locator("#btn-theme").ClickAsync();
        await Expect(html).ToHaveAttributeAsync("data-bs-theme", "light");
        await Expect(page.Locator("#btn-theme")).ToHaveAttributeAsync("aria-label", "Theme: follow the system");
        Assert.Null(await page.EvaluateAsync<string?>("() => localStorage.getItem('gol.theme')"));
    }

    [Fact]
    public async Task An_out_of_range_view_size_is_clamped_and_the_user_is_told()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");

        await page.Locator("#cells-across").FillAsync("9999");
        await page.Locator("#cells-across").PressAsync("Enter");

        await Expect(page.Locator("#notice")).ToContainTextAsync("5–500");
        await Expect(page.Locator("#status-viewport")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^500 × \d+$"));
        await Expect(page.Locator("#cells-across")).ToHaveValueAsync("500");
        // The connection badge is not the error channel.
        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connected");
    }

    [Fact]
    public async Task Export_explains_itself_while_disabled_and_describes_itself_when_enabled()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");

        await page.Locator("#btn-export").HoverAsync();
        await Expect(page.GetByRole(AriaRole.Tooltip, new() { Name = "Save the current state" })).ToBeVisibleAsync();
        await page.Mouse.MoveAsync(0, 0);

        await page.Locator("#btn-edit").ClickAsync();
        await Expect(page.Locator("#edit-banner")).ToBeVisibleAsync();
        await Expect(page.Locator("#btn-export")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdisabled\b"));
        await page.Locator("#btn-export-wrap").HoverAsync();
        // By role and name: the Edit button's tooltip may still be fading out after the click.
        await Expect(page.GetByRole(AriaRole.Tooltip, new() { Name = "Available once editing is finished" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_status_figures_explain_what_they_measure()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");

        await page.Locator("#status-viewport").HoverAsync();
        await Expect(page.GetByRole(AriaRole.Tooltip, new() { Name = "across" })).ToContainTextAsync("height follows the shape of the canvas");

        await page.Locator("#status-population").HoverAsync();
        await Expect(page.GetByRole(AriaRole.Tooltip, new() { Name = "whole universe" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_readouts_are_a_head_up_display_and_the_connection_badge_hides_while_healthy()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");

        var canvas = (await page.Locator("#universe").BoundingBoxAsync())!;
        var hud = (await page.Locator(".viewer-hud").BoundingBoxAsync())!;
        Assert.True(hud.Y >= canvas.Y && hud.Y < canvas.Y + 40, "the HUD is not at the top of the canvas");
        Assert.True(Math.Abs((hud.X + hud.Width / 2) - (canvas.X + canvas.Width / 2)) < 2, "the HUD is not centred");

        await Expect(page.Locator("#status-connection")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bvisually-hidden\b"));
        await Expect(page.Locator("#status-running")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bvisually-hidden\b"));
        await Expect(page.Locator("#status-running")).ToHaveAttributeAsync("aria-live", "polite");

        // The strip is grouped: two rules separate transport+edit, patterns, and help.
        await Expect(page.Locator("#toolbar-top .vr")).ToHaveCountAsync(2);
        await Expect(page.Locator("#toolbar-top .toolbar-group")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task One_button_toggles_between_start_and_pause_and_never_looks_pressed()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        var run = page.Locator("#btn-run");
        await Expect(run).ToHaveAttributeAsync("aria-label", "Start");
        await Expect(run).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bactive\b"));

        await run.ClickAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("running");
        await Expect(run).ToHaveAttributeAsync("aria-label", "Pause");
        await Expect(run).ToBeEnabledAsync();
        await Expect(run).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bactive\b"));

        await run.ClickAsync();
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        await Expect(run).ToHaveAttributeAsync("aria-label", "Start");
    }

    [Fact]
    public async Task An_empty_universe_shows_an_empty_state_with_both_ways_out()
    {
        await app.Loop.LoadAsync(Pattern.Empty);
        var page = await OpenAsync();
        await Expect(page.Locator("#status-population")).ToHaveTextAsync("0");

        await Expect(page.Locator("#canvas-empty")).ToContainTextAsync("The universe is empty");
        await page.Locator("#btn-empty-seed").ClickAsync();
        await Expect(page.Locator("#seed-panel")).ToBeVisibleAsync();
        await page.Locator("#seed-panel .btn-close").ClickAsync();
        await Expect(page.Locator("#seed-panel")).ToBeHiddenAsync();

        await page.Locator("#btn-empty-edit").ClickAsync();
        await Expect(page.Locator("#edit-banner")).ToBeVisibleAsync();
        await Expect(page.Locator("#canvas-empty")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_tiny_pattern_offers_to_zoom_in()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-viewport")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^100 × \d+$"));

        await Expect(page.Locator("#canvas-tiny")).ToContainTextAsync("tiny at this zoom");
        await page.Locator("#btn-zoom-fit").ClickAsync();

        await Expect(page.Locator("#status-viewport")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^12 × \d+$"));
        await Expect(page.Locator("#canvas-tiny")).ToHaveCountAsync(0);
        // The pattern stayed in view: otherwise the off-screen overlay would have taken over.
        await Expect(page.Locator("#canvas-offscreen")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_pattern_outside_the_view_offers_to_recentre()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#status-viewport")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^100 × \d+$"));
        var right = page.GetByRole(AriaRole.Button, new() { Name = "Pan right" });
        for (var i = 0; i < 15; i++) await right.ClickAsync();

        await Expect(page.Locator("#canvas-offscreen")).ToContainTextAsync("outside your view");
        await page.Locator("#btn-overlay-recentre").ClickAsync();

        await Expect(page.Locator("#canvas-offscreen")).ToHaveCountAsync(0);
        await Expect(page.Locator("#canvas-tiny")).ToBeVisibleAsync();
    }
}
