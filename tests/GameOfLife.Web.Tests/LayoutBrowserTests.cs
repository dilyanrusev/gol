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

    private static Task<bool> MainScrollsAsync(IPage page) =>
        page.EvaluateAsync<bool>("() => { const m = document.querySelector('main'); return m.scrollHeight > m.clientHeight + 1; }");

    /// <summary>Heights of the page's vertical building blocks, for the failure message.</summary>
    private static Task<string> LayoutReportAsync(IPage page) => page.EvaluateAsync<string>(@"() => {
        const main = document.querySelector('main');
        const lines = [`main client=${main.clientHeight} scroll=${main.scrollHeight}`];
        for (const sel of ['header', '#edit-banner', '.viewer-toolbar', '.viewer-canvas-wrap', '.viewer-main > .form-text', 'main > .alert'])
            for (const el of document.querySelectorAll(sel)) {
                const r = el.getBoundingClientRect();
                lines.push(`${sel}: top=${r.top.toFixed(0)} height=${r.height.toFixed(0)}`);
            }
        for (const el of document.querySelectorAll('#toolbar-top > *')) {
            const r = el.getBoundingClientRect();
            const cs = getComputedStyle(el);
            lines.push(`  strip child ${el.className}: top=${r.top.toFixed(0)} width=${r.width.toFixed(0)} height=${r.height.toFixed(0)} computedWidth=${cs.width} flex=${cs.flex} wrap=${cs.flexWrap}`);
        }
        return lines.join(' | ');
    }");

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
        Assert.False(await MainScrollsAsync(page), "main scrolls: " + await LayoutReportAsync(page));

        foreach (var id in new[] { "#universe", "#btn-recentre", "#cells-across", "#gesture-hint", "#edit-banner" })
        {
            var box = (await page.Locator(id).BoundingBoxAsync())!;
            Assert.True(box.Y >= 0 && box.Y + box.Height <= height, $"{id} is outside the viewport: y={box.Y} height={box.Height}");
        }
        var canvas = (await page.Locator("#universe").BoundingBoxAsync())!;
        Assert.True(canvas.Height >= 200, $"the canvas is squashed to {canvas.Height}px");
    }

    [Theory]
    [InlineData(1280, 500)] // short
    [InlineData(600, 900)]  // phone-ish: one strip that wraps, still no page scroll
    public async Task The_page_still_fits_on_short_and_narrow_windows(int width, int height)
    {
        var page = await app.NewPageAsync(new ViewportSize { Width = width, Height = height });
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");

        Assert.False(await MainScrollsAsync(page), "main scrolls: " + await LayoutReportAsync(page));
        var canvas = (await page.Locator("#universe").BoundingBoxAsync())!;
        Assert.True(canvas.Y >= 0 && canvas.Y + canvas.Height <= height, "the canvas does not fit");
        Assert.True(canvas.Height >= 200, $"the canvas is squashed to {canvas.Height}px");

        // The head-up display and the canvas prompt share the top of the canvas; they must not overlap.
        await Expect(page.Locator("#canvas-tiny")).ToBeVisibleAsync();
        var hud = (await page.Locator(".viewer-hud").BoundingBoxAsync())!;
        var prompt = (await page.Locator("#canvas-tiny").BoundingBoxAsync())!;
        var overlap = hud.X < prompt.X + prompt.Width && prompt.X < hud.X + hud.Width
                   && hud.Y < prompt.Y + prompt.Height && prompt.Y < hud.Y + hud.Height;
        Assert.False(overlap, $"the prompt overlaps the HUD at {width}x{height}");
    }
}
