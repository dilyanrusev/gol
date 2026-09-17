using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace GameOfLife.Web.Tests;

/// <summary>Hand editing from the page: the banner, the countdown, clicking cells, and what other browsers see.</summary>
[Collection(WebCollection.Name)]
public sealed class EditingBrowserTests(WebAppFixture app) : IAsyncLifetime
{
    private readonly List<IPage> _pages = [];

    public Task InitializeAsync() => app.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var page in _pages) await page.Context.CloseAsync();
        await app.Loop.PauseAsync();
    }

    private async Task<IPage> OpenAsync()
    {
        var page = await app.NewPageAsync();
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-connection")).ToHaveTextAsync("connected");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        return page;
    }

    [Fact]
    public async Task The_editor_and_an_observer_see_matching_banners_until_Done()
    {
        var editor = await OpenAsync();
        var observer = await OpenAsync();

        await editor.Locator("#btn-edit").ClickAsync();

        await Expect(editor.Locator("#edit-banner")).ToBeVisibleAsync();
        await Expect(editor.Locator("#edit-banner-title")).ToHaveTextAsync("You are editing the universe.");
        await Expect(editor.Locator("#edit-countdown")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d+:\d\d$"));
        await Expect(editor.Locator("#edit-progress-bar")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bbg-success\b"));
        await Expect(editor.Locator("#btn-edit-done")).ToBeVisibleAsync();
        await Expect(editor.Locator("#btn-run")).ToBeDisabledAsync();
        await Expect(editor.Locator("#btn-edit")).ToHaveTextAsync("Editing…");

        await Expect(observer.Locator("#edit-banner")).ToBeVisibleAsync();
        await Expect(observer.Locator("#edit-banner-title")).ToHaveTextAsync("Another client is editing the universe.");
        await Expect(observer.Locator("#edit-banner-text")).ToContainTextAsync("disabled for everyone");
        await Expect(observer.Locator("#btn-edit-done")).ToBeHiddenAsync();
        await Expect(observer.Locator("#btn-run")).ToBeDisabledAsync();
        await Expect(observer.Locator("#btn-step")).ToBeDisabledAsync();
        await Expect(observer.Locator("#btn-reset")).ToBeDisabledAsync();
        await Expect(observer.Locator("#btn-edit")).ToBeDisabledAsync();
        // The mode instruction is on the editor's canvas only.
        await Expect(editor.Locator("#canvas-editing")).ToContainTextAsync("Click a cell to flip it");
        await Expect(observer.Locator("#canvas-editing")).ToHaveCountAsync(0);

        await editor.Locator("#btn-edit-done").ClickAsync();

        // Done resumes the simulation for everyone.
        await Expect(editor.Locator("#edit-banner")).ToBeHiddenAsync();
        await Expect(observer.Locator("#edit-banner")).ToBeHiddenAsync();
        await Expect(editor.Locator("#status-running")).ToHaveTextAsync("running");
        await Expect(observer.Locator("#status-running")).ToHaveTextAsync("running");
        await Expect(observer.Locator("#btn-run")).ToHaveAttributeAsync("aria-label", "Pause");
        await Expect(observer.Locator("#btn-edit")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task The_disabled_edit_button_explains_why_on_hover()
    {
        await app.Loop.StartAsync();
        var page = await app.NewPageAsync();
        _pages.Add(page);
        await page.GotoAsync("/");
        // The tooltip is enabled by the first frame; hovering before that would show nothing.
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("running");
        await Expect(page.Locator("#btn-edit")).ToBeDisabledAsync();

        await page.Locator("#btn-edit-wrap").HoverAsync();
        await Expect(page.Locator(".tooltip")).ToContainTextAsync("Pause the simulation first");

        await page.Locator("#btn-run").ClickAsync();
        await Expect(page.Locator("#btn-edit")).ToBeEnabledAsync();
        // The run button's own tooltip is still fading out after the click; let it go first.
        await page.Mouse.MoveAsync(0, 0);
        await Expect(page.Locator(".tooltip")).ToHaveCountAsync(0);
        // Once enabled, the button explains what it does instead of why it cannot.
        await page.Locator("#btn-edit-wrap").HoverAsync();
        await Expect(page.Locator(".tooltip")).ToContainTextAsync("Flip cells by clicking them");
    }

    [Fact]
    public async Task Observers_get_a_tooltip_while_someone_else_edits()
    {
        var editor = await OpenAsync();
        var observer = await OpenAsync();
        await editor.Locator("#btn-edit").ClickAsync();
        await Expect(observer.Locator("#btn-edit")).ToBeDisabledAsync();

        await observer.Locator("#btn-edit-wrap").HoverAsync();

        await Expect(observer.Locator(".tooltip")).ToContainTextAsync("Another client is editing");
    }

    [Fact]
    public async Task Clicking_a_cell_flips_it_and_Cancel_puts_it_back()
    {
        var page = await OpenAsync();
        await page.Locator("#cells-across").FillAsync("10");
        await page.Locator("#cells-across").PressAsync("Enter");
        await Expect(page.Locator("#status-viewport")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^10 × \d+$"));
        await page.Locator("#btn-edit").ClickAsync();
        await Expect(page.Locator("#edit-banner")).ToBeVisibleAsync();

        // The blinker's middle cell sits at the view's centre cell. Cell positions are computed from
        // the view's size and the canvas box, the same way the page lays the grid out.
        var text = await page.Locator("#status-viewport").TextContentAsync();
        var dims = text!.Split('×').Select(t => int.Parse(t.Trim())).ToArray();
        var (w, h) = (dims[0], dims[1]);
        var canvas = page.Locator("#universe");
        var box = (await canvas.BoundingBoxAsync())!;
        var cellPx = Math.Min(box.Width / w, box.Height / h);
        var (ox, oy) = ((box.Width - cellPx * w) / 2, (box.Height - cellPx * h) / 2);
        Position CellAt(int x, int y) => new() { X = (float)(ox + (x + 0.5) * cellPx), Y = (float)(oy + (y + 0.5) * cellPx) };
        var middle = CellAt(w / 2, h / 2);
        var corner = CellAt(0, 0);

        await canvas.ClickAsync(new() { Position = middle });
        await Expect(page.Locator("#status-population")).ToHaveTextAsync("2");

        await canvas.ClickAsync(new() { Position = corner });
        await Expect(page.Locator("#status-population")).ToHaveTextAsync("3");

        await page.Locator("#btn-edit-cancel").ClickAsync();
        await Expect(page.Locator("#edit-banner")).ToBeHiddenAsync();
        await Expect(page.Locator("#canvas-editing")).ToHaveCountAsync(0);
        await Expect(page.Locator("#status-population")).ToHaveTextAsync("3");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        Assert.Equal(WebAppFixture.Blinker.Cells.Count, app.Loop.Current.Population);
    }

    [Fact]
    public async Task Clicks_from_a_non_editor_do_nothing()
    {
        var page = await OpenAsync();
        var canvas = page.Locator("#universe");
        var box = (await canvas.BoundingBoxAsync())!;

        await canvas.ClickAsync(new() { Position = new() { X = box.Width / 2, Y = box.Height / 2 } });

        await page.WaitForTimeoutAsync(300);
        await Expect(page.Locator("#status-population")).ToHaveTextAsync("3");
        Assert.Null(app.Loop.Current.Edit);
    }

    [Fact]
    public async Task The_countdown_drains_through_warning_and_danger_then_unlocks()
    {
        var page = await OpenAsync();
        await page.Locator("#btn-edit").ClickAsync();
        var bar = page.Locator("#edit-progress-bar");
        var banner = page.Locator("#edit-banner");
        var half = (float)WebAppFixture.EditTimeout.TotalMilliseconds;

        await Expect(bar).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bbg-success\b"));
        await Expect(banner).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\balert-success\b"));
        await Expect(bar).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bbg-warning\b"), new() { Timeout = half });
        await Expect(banner).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\balert-warning\b"));
        await Expect(bar).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bbg-danger\b"), new() { Timeout = half });
        await Expect(banner).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\balert-danger\b"));
        await Expect(banner).ToBeHiddenAsync(new() { Timeout = half });
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        await Expect(page.Locator("#btn-run")).ToBeEnabledAsync();
        await Expect(page.Locator("#btn-run")).ToHaveAttributeAsync("aria-label", "Start");
        Assert.Null(app.Loop.Current.Edit);
    }

    [Fact]
    public async Task The_edit_button_waits_for_a_pause()
    {
        await app.Loop.StartAsync();
        var page = await app.NewPageAsync();
        _pages.Add(page);
        await page.GotoAsync("/");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("running");

        await Expect(page.Locator("#btn-edit")).ToBeDisabledAsync();

        await page.Locator("#btn-run").ClickAsync();
        await Expect(page.Locator("#btn-edit")).ToBeEnabledAsync();
    }
}
