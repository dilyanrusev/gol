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
        await Expect(editor.Locator("#btn-start")).ToBeDisabledAsync();
        await Expect(editor.Locator("#btn-edit")).ToHaveTextAsync("Editing…");

        await Expect(observer.Locator("#edit-banner")).ToBeVisibleAsync();
        await Expect(observer.Locator("#edit-banner-title")).ToHaveTextAsync("Another client is editing the universe.");
        await Expect(observer.Locator("#edit-banner-text")).ToContainTextAsync("disabled for everyone");
        await Expect(observer.Locator("#btn-edit-done")).ToBeHiddenAsync();
        await Expect(observer.Locator("#btn-start")).ToBeDisabledAsync();
        await Expect(observer.Locator("#btn-step")).ToBeDisabledAsync();
        await Expect(observer.Locator("#btn-reset")).ToBeDisabledAsync();
        await Expect(observer.Locator("#btn-edit")).ToBeDisabledAsync();
        await Expect(observer.Locator("#edit-hint")).ToContainTextAsync("Another client is editing");

        await editor.Locator("#btn-edit-done").ClickAsync();

        // Done resumes the simulation for everyone.
        await Expect(editor.Locator("#edit-banner")).ToBeHiddenAsync();
        await Expect(observer.Locator("#edit-banner")).ToBeHiddenAsync();
        await Expect(editor.Locator("#status-running")).ToHaveTextAsync("running");
        await Expect(observer.Locator("#status-running")).ToHaveTextAsync("running");
        await Expect(observer.Locator("#btn-pause")).ToBeEnabledAsync();
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

        await page.Locator("#btn-pause").ClickAsync();
        await Expect(page.Locator("#btn-edit")).ToBeEnabledAsync();
        await page.Mouse.MoveAsync(0, 0);
        await page.Locator("#btn-edit-wrap").HoverAsync();
        await page.WaitForTimeoutAsync(300);
        await Expect(page.Locator(".tooltip")).ToHaveCountAsync(0);
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
        await page.Locator("#grid-width").FillAsync("10");
        await page.Locator("#grid-height").FillAsync("10");
        await page.Locator("#btn-resize").ClickAsync();
        await Expect(page.Locator("#status-viewport")).ToHaveTextAsync("10 × 10");
        await page.Locator("#btn-edit").ClickAsync();
        await Expect(page.Locator("#edit-banner")).ToBeVisibleAsync();

        // The blinker's middle cell sits at viewport cell (5, 5). The square grid is centred in the
        // canvas, which may be wider than it is tall, so positions are taken from the grid's own box.
        var canvas = page.Locator("#universe");
        var box = (await canvas.BoundingBoxAsync())!;
        var side = Math.Min(box.Width, box.Height);
        var (ox, oy) = ((box.Width - side) / 2, (box.Height - side) / 2);
        var middle = new Position { X = (float)(ox + side * 0.55), Y = (float)(oy + side * 0.55) };
        var corner = new Position { X = (float)(ox + side * 0.05), Y = (float)(oy + side * 0.05) };

        await canvas.ClickAsync(new() { Position = middle });
        await Expect(page.Locator("#status-population")).ToHaveTextAsync("2");

        await canvas.ClickAsync(new() { Position = corner });
        await Expect(page.Locator("#status-population")).ToHaveTextAsync("3");

        await page.Locator("#btn-edit-cancel").ClickAsync();
        await Expect(page.Locator("#edit-banner")).ToBeHiddenAsync();
        await Expect(page.Locator("#status-population")).ToHaveTextAsync("3");
        await Expect(page.Locator("#status-running")).ToHaveTextAsync("paused");
        Assert.Equal(WebAppFixture.Blinker.Cells.Count, app.Loop.Current.Population);
        Assert.DoesNotContain(app.Loop.Current.Cells, c => c == app.Loop.Current.SeedCentre.Offset(-5, -5));
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
        await Expect(page.Locator("#btn-start")).ToBeEnabledAsync();
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
        await Expect(page.Locator("#edit-hint")).ToHaveTextAsync("Pause the simulation to edit cells.");

        await page.Locator("#btn-pause").ClickAsync();
        await Expect(page.Locator("#btn-edit")).ToBeEnabledAsync();
    }
}
