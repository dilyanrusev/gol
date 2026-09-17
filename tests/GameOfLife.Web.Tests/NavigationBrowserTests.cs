using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GameOfLife.Web.Tests;

/// <summary>The navigation pad: over the canvas, in its corner, with directions where they belong.</summary>
[Collection(WebCollection.Name)]
public sealed class NavigationBrowserTests(WebAppFixture app) : IAsyncLifetime
{
    private readonly List<IPage> _pages = [];

    public Task InitializeAsync() => app.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var page in _pages) await page.Context.CloseAsync();
    }

    private async Task<IPage> OpenAsync(ViewportSize? viewport = null)
    {
        var page = await app.NewPageAsync(viewport);
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        return page;
    }

    private static async Task<(float X, float Y)> CentreAsync(ILocator locator)
    {
        var box = (await locator.BoundingBoxAsync())!;
        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    [Fact]
    public async Task Pan_sits_bottom_left_and_zoom_bottom_right_with_directions_pointing_the_right_way()
    {
        var page = await OpenAsync();
        var canvas = (await page.Locator("#universe").BoundingBoxAsync())!;
        var pad = (await page.Locator(".viewer-nav-pan").BoundingBoxAsync())!;
        var zoom = (await page.Locator(".viewer-nav-zoom").BoundingBoxAsync())!;

        foreach (var (name, box) in new[] { ("pan cluster", pad), ("zoom cluster", zoom) })
        {
            Assert.True(box.X >= canvas.X && box.X + box.Width <= canvas.X + canvas.Width + 1, $"the {name} is not inside the canvas horizontally");
            Assert.True(box.Y >= canvas.Y && box.Y + box.Height <= canvas.Y + canvas.Height + 1, $"the {name} is not inside the canvas vertically");
            Assert.True(box.Y + box.Height > canvas.Y + canvas.Height * 0.75, $"the {name} is not at the bottom");
            Assert.True(box.X >= canvas.X + 12 && box.X + box.Width <= canvas.X + canvas.Width - 12, $"the {name} touches the edge-swipe zone");
        }
        Assert.True(pad.X < canvas.X + canvas.Width * 0.25, "the pan cluster is not on the left");
        Assert.True(zoom.X + zoom.Width > canvas.X + canvas.Width * 0.75, "the zoom cluster is not on the right");
        Assert.True(Math.Abs((pad.Y + pad.Height) - (zoom.Y + zoom.Height)) < 2, "the clusters do not share a baseline");

        var centre = await CentreAsync(page.Locator("#btn-recentre"));
        var up = await CentreAsync(page.GetByRole(AriaRole.Button, new() { Name = "Pan up" }));
        var down = await CentreAsync(page.GetByRole(AriaRole.Button, new() { Name = "Pan down" }));
        var left = await CentreAsync(page.GetByRole(AriaRole.Button, new() { Name = "Pan left" }));
        var right = await CentreAsync(page.GetByRole(AriaRole.Button, new() { Name = "Pan right" }));
        var zoomIn = await CentreAsync(page.Locator("#btn-zoom-in"));
        var zoomOut = await CentreAsync(page.Locator("#btn-zoom-out"));

        Assert.True(up.Y < centre.Y && Math.Abs(up.X - centre.X) < 2, "up is not above the centre");
        Assert.True(down.Y > centre.Y && Math.Abs(down.X - centre.X) < 2, "down is not below the centre");
        Assert.True(left.X < centre.X && Math.Abs(left.Y - centre.Y) < 2, "left is not left of the centre");
        Assert.True(right.X > centre.X && Math.Abs(right.Y - centre.Y) < 2, "right is not right of the centre");
        var across = await CentreAsync(page.Locator("#cells-across"));
        Assert.True(zoomOut.X < across.X && across.X < zoomIn.X && Math.Abs(zoomIn.Y - zoomOut.Y) < 2, "the zoom stepper is not [out] [cells] [in] in a row");
    }

    [Fact]
    public async Task The_pad_does_not_take_toolbar_space_and_only_one_strip_remains()
    {
        var page = await OpenAsync();

        await Expect(page.Locator(".viewer-toolbar")).ToHaveCountAsync(1);
        await Expect(page.Locator("#toolbar-top #btn-seed")).ToBeVisibleAsync();
        await Expect(page.Locator(".viewer-nav-zoom #cells-across")).ToBeVisibleAsync();
        await Expect(page.Locator(".viewer-canvas-wrap .viewer-nav")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task The_pad_pans_the_view_in_the_direction_pressed()
    {
        var page = await OpenAsync();
        await page.Locator("#btn-zoom-fit").ClickAsync();
        await Expect(page.Locator("#status-viewport")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^12 × \d+$"));
        await Expect(page.Locator("#canvas-tiny")).ToHaveCountAsync(0);

        // A press moves a tenth of the view, at least one cell; twelve presses push the centred
        // blinker out above a view at most 12 cells tall.
        var down = page.GetByRole(AriaRole.Button, new() { Name = "Pan down" });
        for (var i = 0; i < 12; i++) await down.ClickAsync();
        await Expect(page.Locator("#canvas-offscreen")).ToBeVisibleAsync();

        var up = page.GetByRole(AriaRole.Button, new() { Name = "Pan up" });
        for (var i = 0; i < 12; i++) await up.ClickAsync();
        await Expect(page.Locator("#canvas-offscreen")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_pad_button_stays_legible_while_hovered_and_focused_after_a_click()
    {
        var page = await OpenAsync();
        var button = page.Locator("#btn-zoom-in");
        static Task<string> Contrast(ILocator b) =>
            b.EvaluateAsync<string>("el => { const cs = getComputedStyle(el); return cs.color === cs.backgroundColor ? 'same' : 'different'; }");
        Assert.Equal("different", await Contrast(button));

        await button.HoverAsync();
        Assert.Equal("different", await Contrast(button));

        await button.ClickAsync();
        await Expect(page.Locator("#status-viewport")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^80 × \d+$"));
        await Expect(button).ToBeFocusedAsync();
        Assert.Equal("different", await Contrast(button));
    }

    [Fact]
    public async Task Touch_devices_get_bigger_pad_buttons()
    {
        var context = await app.Browser.NewContextAsync(new()
        {
            BaseURL = app.BaseAddress.ToString(),
            HasTouch = true,
            IsMobile = true,
            ViewportSize = new() { Width = 412, Height = 915 },
        });
        var page = await context.NewPageAsync();
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");

        var button = (await page.GetByRole(AriaRole.Button, new() { Name = "Pan up" }).BoundingBoxAsync())!;
        Assert.True(button.Width >= 44 && button.Height >= 44, $"touch target is {button.Width} x {button.Height}");
    }
}
