using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GameOfLife.Web.Tests;

/// <summary>The one-time tip: dismissible, remembered per browser, and recoverable from the Help panel.</summary>
[Collection(WebCollection.Name)]
public sealed class HintBrowserTests(WebAppFixture app) : IAsyncLifetime
{
    private readonly List<IPage> _pages = [];

    public Task InitializeAsync() => app.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var page in _pages) await page.Context.CloseAsync();
    }

    private async Task<IPage> OpenAsync()
    {
        // Every context starts with empty storage, so this is a first visit.
        var page = await app.NewPageAsync();
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connected");
        return page;
    }

    [Fact]
    public async Task The_hint_shows_on_a_first_visit_and_stays_dismissed_across_reloads()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#gesture-hint")).ToContainTextAsync("Drag to pan");
        await Expect(page.Locator("#gesture-hint")).ToContainTextAsync("Edit cells");

        await page.Locator("#gesture-hint-close").ClickAsync();
        await Expect(page.Locator("#gesture-hint")).ToHaveCountAsync(0);
        Assert.Equal("1", await page.EvaluateAsync<string?>("() => localStorage.getItem('gol.hint.gestures.v2')"));

        await page.ReloadAsync();
        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connected");
        await Expect(page.Locator("#gesture-hint")).ToHaveCountAsync(0);
        // The knowledge is still reachable: the enabled Edit button explains itself on hover.
        await page.Locator("#btn-edit-wrap").HoverAsync();
        // By role and name: another tooltip may still be fading out from wherever the pointer was.
        await Expect(page.GetByRole(AriaRole.Tooltip, new() { Name = "Flip cells by clicking them" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Help_is_a_reference_panel_that_can_also_bring_the_tip_back()
    {
        var page = await OpenAsync();
        await page.Locator("#gesture-hint-close").ClickAsync();
        await Expect(page.Locator("#gesture-hint")).ToHaveCountAsync(0);

        await page.Locator("#btn-help").ClickAsync();
        await Expect(page.Locator("#help-panel")).ToBeVisibleAsync();
        await Expect(page.Locator("#help-panel")).ToContainTextAsync("Keyboard");
        await Expect(page.Locator("#help-panel")).ToContainTextAsync("Editing cells");
        await Expect(page.Locator("#help-panel")).ToContainTextAsync("Load pattern");

        await page.Locator("#btn-show-tip").ClickAsync();
        await Expect(page.Locator("#help-panel")).ToBeHiddenAsync();
        await Expect(page.Locator("#gesture-hint")).ToBeVisibleAsync();
        Assert.Null(await page.EvaluateAsync<string?>("() => localStorage.getItem('gol.hint.gestures.v2')"));
        await page.ReloadAsync();
        await Expect(page.Locator("#gesture-hint")).ToBeVisibleAsync();
    }
}
