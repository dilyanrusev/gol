using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GameOfLife.Web.Tests;

/// <summary>The universe page fits the viewport: the canvas column never needs vertical scrolling at desktop sizes.</summary>
[Collection(WebCollection.Name)]
public sealed class LayoutBrowserTests(WebAppFixture app) : IAsyncLifetime
{
    private readonly List<IPage> _pages = [];

    public Task InitializeAsync() => app.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var page in _pages) await page.Context.CloseAsync();
        await app.ResetAsync();
    }

    [Theory]
    [InlineData(1280, 720)] // Playwright's default
    [InlineData(992, 600)]  // the narrowest lg window, and short
    public async Task The_canvas_column_fits_without_scrolling_even_with_the_banner(int width, int height)
    {
        var page = await app.NewPageAsync(new ViewportSize { Width = width, Height = height });
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        await page.Locator("#btn-edit").ClickAsync();
        await Expect(page.Locator("#edit-banner")).ToBeVisibleAsync();

        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollHeight > window.innerHeight + 1"), "the document scrolls");
        Assert.False(await page.EvaluateAsync<bool>("() => { const m = document.querySelector('main'); return m.scrollHeight > m.clientHeight + 1; }"), "main scrolls");

        foreach (var id in new[] { "#universe", "#btn-recentre", "#btn-resize", "#edit-hint", "#edit-banner" })
        {
            var box = (await page.Locator(id).BoundingBoxAsync())!;
            Assert.True(box.Y >= 0 && box.Y + box.Height <= height, $"{id} is outside the viewport: y={box.Y} height={box.Height}");
        }
        var canvas = (await page.Locator("#universe").BoundingBoxAsync())!;
        Assert.True(canvas.Height >= 200, $"the canvas is squashed to {canvas.Height}px");
    }

    [Fact]
    public async Task A_tall_sidebar_scrolls_by_itself_not_the_page()
    {
        var page = await app.NewPageAsync(new ViewportSize { Width = 1280, Height = 500 });
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");

        Assert.False(await page.EvaluateAsync<bool>("() => { const m = document.querySelector('main'); return m.scrollHeight > m.clientHeight + 1; }"), "main scrolls");
        var sidebarScrolls = await page.EvaluateAsync<bool>("() => { const s = document.querySelector('.viewer-sidebar'); return s.scrollHeight > s.clientHeight + 1; }");
        Assert.True(sidebarScrolls, "expected the sidebar to be the scroll container on a short window");
        var canvas = (await page.Locator("#universe").BoundingBoxAsync())!;
        Assert.True(canvas.Y + canvas.Height <= 500, "the canvas does not fit");
    }
}
