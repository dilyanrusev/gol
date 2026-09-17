using System.Threading.Channels;
using GameOfLife.Core;
using GameOfLife.Web.Simulation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace GameOfLife.Web.Tests;

/// <summary>The exclusive edit session as seen through the hub by two SignalR clients.</summary>
[Collection(WebCollection.Name)]
public sealed class EditingHubTests(WebAppFixture app) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly List<HubConnection> _connections = [];

    public Task InitializeAsync() => app.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections) await connection.DisposeAsync();
        await app.Loop.PauseAsync();
    }

    private async Task<(HubConnection Connection, ChannelReader<Frame> Frames)> ConnectAsync()
    {
        var connection = new HubConnectionBuilder().WithUrl(app.HubUrl).Build();
        _connections.Add(connection);
        var frames = Channel.CreateUnbounded<Frame>();
        connection.On<Frame>(nameof(Hubs.ILifeClient.ReceiveFrame), f => frames.Writer.TryWrite(f));
        await connection.StartAsync();
        return (connection, frames.Reader);
    }

    private static async Task<Frame> NextAsync(ChannelReader<Frame> frames, Func<Frame, bool> until)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (true)
        {
            var frame = await frames.ReadAsync(cts.Token);
            if (until(frame)) return frame;
        }
    }

    private static Task<Frame> Invoke(HubConnection c, string method, params object[] args) => c.InvokeCoreAsync<Frame>(method, args);

    private static int[] Cells(Frame frame) => CellsCodec.Decode(frame.Cells, frame.Width, frame.Height);

    [Fact]
    public async Task Only_one_client_can_edit_and_every_frame_says_who()
    {
        var (first, _) = await ConnectAsync();
        var (second, secondFrames) = await ConnectAsync();

        var mine = await Invoke(first, nameof(Hubs.ILifeHub.BeginEdit));
        Assert.True(mine.Editing);
        Assert.True(mine.EditingByMe);
        Assert.InRange(mine.EditRemainingMs, 1, mine.EditTimeoutMs);
        Assert.Equal((int)WebAppFixture.EditTimeout.TotalMilliseconds, mine.EditTimeoutMs);

        var ex = await Assert.ThrowsAsync<HubException>(() => Invoke(second, nameof(Hubs.ILifeHub.BeginEdit)));
        Assert.Contains("Another client is editing", ex.Message);

        var theirs = await NextAsync(secondFrames, f => f.Editing);
        Assert.False(theirs.EditingByMe);
        Assert.InRange(theirs.EditRemainingMs, 1, theirs.EditTimeoutMs);
    }

    [Fact]
    public async Task Start_step_and_reset_are_refused_for_everyone_with_a_reason()
    {
        var (editor, _) = await ConnectAsync();
        var (other, _) = await ConnectAsync();
        await Invoke(editor, nameof(Hubs.ILifeHub.BeginEdit));

        foreach (var method in new[] { nameof(Hubs.ILifeHub.Start), nameof(Hubs.ILifeHub.Step), nameof(Hubs.ILifeHub.Reset) })
        {
            var ex = await Assert.ThrowsAsync<HubException>(() => other.InvokeAsync(method));
            Assert.Contains("being edited", ex.Message);
        }

        var own = await Assert.ThrowsAsync<HubException>(() => editor.InvokeAsync(nameof(Hubs.ILifeHub.Start)));
        Assert.Contains("Press Done or Cancel", own.Message);
        Assert.False(app.Loop.Current.Running);
    }

    [Fact]
    public async Task Editing_requires_a_paused_simulation()
    {
        var (client, _) = await ConnectAsync();
        await app.Loop.StartAsync();

        var ex = await Assert.ThrowsAsync<HubException>(() => Invoke(client, nameof(Hubs.ILifeHub.BeginEdit)));

        Assert.Contains("Pause the simulation", ex.Message);
    }

    [Fact]
    public async Task Toggle_maps_through_this_clients_viewport()
    {
        var (client, _) = await ConnectAsync();
        // A 10 x 10 viewport centred on the blinker puts its middle cell at (5, 5) and the row at y = 5.
        var before = await Invoke(client, nameof(Hubs.ILifeHub.Resize), 10, 10);
        Assert.Equal(new[] { 5 * 10 + 4, 5 * 10 + 5, 5 * 10 + 6 }, Cells(before));
        await Invoke(client, nameof(Hubs.ILifeHub.BeginEdit));

        var middleOff = await Invoke(client, nameof(Hubs.ILifeHub.ToggleCell), 5, 5);
        Assert.Equal(new[] { 54, 56 }, Cells(middleOff));
        Assert.Equal(2, middleOff.Population);

        var cornerOn = await Invoke(client, nameof(Hubs.ILifeHub.ToggleCell), 0, 0);
        Assert.Equal(new[] { 0, 54, 56 }, Cells(cornerOn));
        Assert.Equal(0UL, cornerOn.Generation);
    }

    [Fact]
    public async Task Toggle_outside_the_viewport_or_by_a_non_editor_is_refused()
    {
        var (editor, _) = await ConnectAsync();
        var (other, _) = await ConnectAsync();
        await Invoke(editor, nameof(Hubs.ILifeHub.Resize), 10, 10);
        await Invoke(editor, nameof(Hubs.ILifeHub.BeginEdit));

        var outside = await Assert.ThrowsAsync<HubException>(() => Invoke(editor, nameof(Hubs.ILifeHub.ToggleCell), 10, 0));
        Assert.Contains("outside", outside.Message);

        var notMine = await Assert.ThrowsAsync<HubException>(() => Invoke(other, nameof(Hubs.ILifeHub.ToggleCell), 0, 0));
        Assert.Contains("Another client is editing", notMine.Message);
        Assert.Equal(3, app.Loop.Current.Population);
    }

    [Fact]
    public async Task Done_keeps_the_edits_and_resumes_the_simulation_for_everyone()
    {
        var (editor, _) = await ConnectAsync();
        var (_, otherFrames) = await ConnectAsync();
        await Invoke(editor, nameof(Hubs.ILifeHub.BeginEdit));
        await Invoke(editor, nameof(Hubs.ILifeHub.ToggleCell), 0, 0);

        var done = await Invoke(editor, nameof(Hubs.ILifeHub.EndEdit));
        Assert.False(done.Editing);
        Assert.True(done.Running);

        var running = await NextAsync(otherFrames, f => !f.Editing && f.Running && f.Generation > 0);
        Assert.False(running.EditingByMe);

        // The lone cell dies in the first generation, but the edit was made at generation 0, so it is the seed now.
        await app.Loop.PauseAsync();
        await app.Loop.ResetAsync();
        Assert.Equal(4, app.Loop.Current.Population);
    }

    [Fact]
    public async Task Cancel_restores_the_universe()
    {
        var (editor, _) = await ConnectAsync();
        await Invoke(editor, nameof(Hubs.ILifeHub.BeginEdit));
        await Invoke(editor, nameof(Hubs.ILifeHub.ToggleCell), 0, 0);
        await Invoke(editor, nameof(Hubs.ILifeHub.ToggleCell), 1, 1);

        var cancelled = await Invoke(editor, nameof(Hubs.ILifeHub.CancelEdit));

        Assert.False(cancelled.Editing);
        Assert.False(cancelled.Running);
        Assert.Equal(3, cancelled.Population);
    }

    [Fact]
    public async Task Disconnecting_releases_the_session_and_keeps_the_edits()
    {
        var (editor, _) = await ConnectAsync();
        var (other, otherFrames) = await ConnectAsync();
        await Invoke(editor, nameof(Hubs.ILifeHub.BeginEdit));
        await Invoke(editor, nameof(Hubs.ILifeHub.ToggleCell), 0, 0);
        await NextAsync(otherFrames, f => f.Editing && f.Population == 4);

        await editor.DisposeAsync();

        var released = await NextAsync(otherFrames, f => !f.Editing);
        Assert.Equal(4, released.Population);
        Assert.False(released.Running);
        var taken = await Invoke(other, nameof(Hubs.ILifeHub.BeginEdit));
        Assert.True(taken.EditingByMe);
    }

    [Fact]
    public async Task Seed_upload_is_refused_while_someone_edits()
    {
        var (editor, _) = await ConnectAsync();
        await Invoke(editor, nameof(Hubs.ILifeHub.BeginEdit));
        using var client = CreateClient();
        var form = new MultipartFormDataContent { { new StringContent("x = 1, y = 1\no!"), "file", "dot.rle" } };
        var page = await client.GetStringAsync("/");
        // The upload form is rendered by React; Razor hands it the token through the mount element.
        var token = System.Text.RegularExpressions.Regex.Match(page, "data-antiforgery-token=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        form.Add(new StringContent(token), "__RequestVerificationToken");

        var response = await client.PostAsync("/?handler=Upload", form);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("being edited", body);
        Assert.Equal(3, app.Loop.Current.Population);
    }

    private HttpClient CreateClient() => new(new HttpClientHandler { UseCookies = true }) { BaseAddress = app.BaseAddress };
}
