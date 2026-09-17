using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GameOfLife.Web.Tests;

/// <summary>The seed editor page: presets, painting, keyboard, RLE round trip, and seeding the universe.</summary>
[Collection(WebCollection.Name)]
public sealed class EditorBrowserTests(WebAppFixture app) : IAsyncLifetime
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
        await page.GotoAsync("/Editor");
        await Expect(page.Locator("#editor-grid [role=gridcell]")).ToHaveCountAsync(100 * 100);
        return page;
    }

    [Fact]
    public async Task Presets_clicks_and_keys_keep_the_grid_and_the_rle_in_sync()
    {
        var page = await OpenAsync();
        await Expect(page.Locator("#editor-count")).ToHaveTextAsync("0");

        await page.Locator("[data-preset=glider]").ClickAsync();
        await Expect(page.Locator("#editor-count")).ToHaveTextAsync("5");
        await Expect(page.Locator("#editor-rle")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"x = 3, y = 3.*bo\$2bo\$3o!", System.Text.RegularExpressions.RegexOptions.Singleline));

        var corner = page.Locator("#editor-grid [data-i='0']");
        await corner.ClickAsync();
        await Expect(corner).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator("#editor-count")).ToHaveTextAsync("6");

        // Arrow keys move the focus (roving tabindex); Space toggles the focused cell.
        await page.Keyboard.PressAsync("ArrowRight");
        await Expect(page.Locator("#editor-grid [data-i='1']")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Space");
        await Expect(page.Locator("#editor-grid [data-i='1']")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator("#editor-count")).ToHaveTextAsync("7");
        // The header is the bounding box of the live cells: from the corner to the centred glider.
        await Expect(page.Locator("#editor-rle")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"^x = 51, y = 51"));

        await page.Locator("#btn-clear").ClickAsync();
        await Expect(page.Locator("#editor-count")).ToHaveTextAsync("0");
    }

    [Fact]
    public async Task Pasted_rle_is_applied_and_bad_rle_is_explained_inline()
    {
        var page = await OpenAsync();

        await page.Locator("#editor-rle").FillAsync("x = 3, y = 1\n3o!");
        await page.Locator("#btn-apply-rle").ClickAsync();
        await Expect(page.Locator("#editor-count")).ToHaveTextAsync("3");
        await Expect(page.Locator("#editor-error")).ToHaveCountAsync(0);

        await page.Locator("#editor-rle").FillAsync("this is not rle");
        await page.Locator("#btn-apply-rle").ClickAsync();
        await Expect(page.Locator("#editor-error")).ToContainTextAsync("header");
        await Expect(page.Locator("#editor-count")).ToHaveTextAsync("3");
    }

    [Fact]
    public async Task Seeding_posts_the_pattern_to_the_server()
    {
        var page = await OpenAsync();
        await page.Locator("[data-preset=rpentomino]").ClickAsync();
        await Expect(page.Locator("#editor-count")).ToHaveTextAsync("5");
        await page.Locator("#Name").FillAsync("My R");

        await page.GetByRole(AriaRole.Button, new() { Name = "Seed the universe" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/$"));
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Seeded the universe with 5 cells");
        await Expect(page.Locator("#status-population")).ToHaveTextAsync("5");
        Assert.Equal(5, app.Loop.Current.Population);
    }
}
