using System.Threading.Channels;
using GameOfLife.Core;
using GameOfLife.Core.Rle;

namespace GameOfLife.Core.Tests;

/// <summary>Exclusive hand editing on the simulation loop: who may edit, what is refused meanwhile, and how it ends.</summary>
public class EditingTests : IAsyncLifetime
{
    private static readonly TimeSpan EditTimeout = TimeSpan.FromMilliseconds(500);

    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));
    private readonly Channel<UniverseSnapshot> _published = Channel.CreateUnbounded<UniverseSnapshot>();
    private SimulationLoop _loop = null!;
    private Task _run = null!;

    /// <summary>Absolute cells of the blinker as the loop places it (centred on the universe centre).</summary>
    private HashSet<Cell> _blinker = null!;

    /// <summary>A dead cell next to the blinker, used for edits.</summary>
    private static readonly Cell Spare = Cell.Centre.Offset(0, 3);

    public async Task InitializeAsync()
    {
        _loop = new SimulationLoop((s, ct) => _published.Writer.WriteAsync(s, ct).AsTask(), EditTimeout);
        _run = _loop.RunAsync(_cts.Token);
        await _loop.LoadAsync(RleParser.Parse(KnownPatterns.Blinker));
        _blinker = (await NextSnapshotAsync()).Cells.ToHashSet();
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        await _run;
    }

    private async Task<UniverseSnapshot> NextSnapshotAsync() => await _published.Reader.ReadAsync(_cts.Token);

    private async Task<UniverseSnapshot> NextSnapshotAsync(Func<UniverseSnapshot, bool> until)
    {
        while (true)
        {
            var s = await NextSnapshotAsync();
            if (until(s)) return s;
        }
    }

    [Fact]
    public void Edit_timeout_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationLoop(editTimeout: TimeSpan.Zero));
    }

    [Fact]
    public async Task Begin_requires_a_paused_simulation()
    {
        await _loop.StartAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _loop.BeginEditAsync("a"));

        Assert.IsNotType<EditInProgressException>(ex);
        Assert.Null(_loop.Current.Edit);
    }

    [Fact]
    public async Task Begin_gives_one_owner_the_session_and_refuses_everyone_else()
    {
        await _loop.BeginEditAsync("a");

        await Assert.ThrowsAsync<EditInProgressException>(() => _loop.BeginEditAsync("b"));

        var edit = Assert.IsType<EditSession>(_loop.Current.Edit);
        Assert.Equal("a", edit.Owner);
        Assert.Equal(EditTimeout, edit.Timeout);
        Assert.InRange(edit.Remaining, EditTimeout / 2, EditTimeout);
    }

    [Fact]
    public async Task Command_results_are_visible_in_Current_as_soon_as_they_complete()
    {
        await _loop.BeginEditAsync("a");
        Assert.NotNull(_loop.Current.Edit);

        await _loop.ToggleCellAsync("a", Spare);
        Assert.Contains(Spare, _loop.Current.Cells);

        await _loop.EndEditAsync("a", resume: false);
        Assert.Null(_loop.Current.Edit);
    }

    [Fact]
    public async Task Start_step_reset_and_load_are_refused_while_someone_edits()
    {
        await _loop.BeginEditAsync("a");

        await Assert.ThrowsAsync<EditInProgressException>(() => _loop.StartAsync());
        await Assert.ThrowsAsync<EditInProgressException>(() => _loop.StepAsync());
        await Assert.ThrowsAsync<EditInProgressException>(() => _loop.ResetAsync());
        await Assert.ThrowsAsync<EditInProgressException>(() => _loop.LoadAsync(RleParser.Parse(KnownPatterns.Glider)));

        var s = _loop.Current;
        Assert.False(s.Running);
        Assert.Equal(0UL, s.Generation);
        Assert.Equal(_blinker, s.Cells.ToHashSet());
    }

    [Fact]
    public async Task Pause_and_speed_changes_stay_allowed_while_someone_edits()
    {
        await _loop.BeginEditAsync("a");

        await _loop.PauseAsync();
        await _loop.SetSpeedAsync(SimulationLoop.MaxGenerationsPerSecond);

        Assert.Equal(SimulationLoop.MaxGenerationsPerSecond, _loop.Current.GenerationsPerSecond);
        Assert.NotNull(_loop.Current.Edit);
    }

    [Fact]
    public async Task Owner_toggles_are_applied_and_published()
    {
        await _loop.BeginEditAsync("a");
        await NextSnapshotAsync();

        await _loop.ToggleCellAsync("a", Spare);
        var born = await NextSnapshotAsync();
        Assert.Contains(Spare, born.Cells);
        Assert.Equal(4, born.Population);

        await _loop.ToggleCellAsync("a", Spare);
        var died = await NextSnapshotAsync();
        Assert.DoesNotContain(Spare, died.Cells);
        Assert.Equal(3, died.Population);
        Assert.Equal(0UL, died.Generation);
    }

    [Fact]
    public async Task Only_the_owner_may_toggle_end_or_cancel()
    {
        await _loop.BeginEditAsync("a");

        await Assert.ThrowsAsync<EditInProgressException>(() => _loop.ToggleCellAsync("b", Spare));
        await Assert.ThrowsAsync<EditInProgressException>(() => _loop.EndEditAsync("b", resume: false));
        await Assert.ThrowsAsync<EditInProgressException>(() => _loop.CancelEditAsync("b"));

        Assert.Equal("a", _loop.Current.Edit?.Owner);
        Assert.Equal(3, _loop.Current.Population);
    }

    [Fact]
    public async Task Editing_without_a_session_is_refused()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _loop.ToggleCellAsync("a", Spare));
        Assert.IsNotType<EditInProgressException>(ex);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _loop.EndEditAsync("a", resume: false));
    }

    [Fact]
    public async Task End_keeps_the_edits_and_lets_the_simulation_run_again()
    {
        await _loop.BeginEditAsync("a");
        await _loop.ToggleCellAsync("a", Spare);

        await _loop.EndEditAsync("a", resume: false);

        Assert.Null(_loop.Current.Edit);
        Assert.Contains(Spare, _loop.Current.Cells);
        Assert.False(_loop.Current.Running);
        await _loop.StartAsync();
        Assert.True(_loop.Current.Running);
    }

    [Fact]
    public async Task End_with_resume_starts_the_simulation_from_the_edited_state()
    {
        await _loop.BeginEditAsync("a");
        await _loop.ToggleCellAsync("a", Spare);
        await NextSnapshotAsync(s => s.Population == 4);

        await _loop.EndEditAsync("a", resume: true);

        var s = _loop.Current;
        Assert.Null(s.Edit);
        Assert.True(s.Running);
        Assert.True(s.Generation >= 1); // resuming runs a generation straight away
        var stepped = await NextSnapshotAsync(s => s.Generation > 1);
        Assert.True(stepped.Running);

        // The lone spare cell dies at once, but the edit was made at generation 0, so the seed has it.
        await _loop.PauseAsync();
        await _loop.ResetAsync();
        Assert.Contains(Spare, _loop.Current.Cells);
    }

    [Fact]
    public async Task Expiry_and_release_leave_the_simulation_paused()
    {
        await _loop.BeginEditAsync("a");
        Assert.True(await _loop.ReleaseEditAsync("a"));
        Assert.False(_loop.Current.Running);

        await _loop.BeginEditAsync("a");
        await NextSnapshotAsync(s => s.Edit is not null);
        var expired = await NextSnapshotAsync(s => s.Edit is null);
        Assert.False(expired.Running);
    }

    [Fact]
    public async Task Cancel_restores_the_universe_from_when_editing_began()
    {
        await _loop.StepAsync();
        var before = _loop.Current.Cells.ToHashSet();
        await _loop.BeginEditAsync("a");
        await _loop.ToggleCellAsync("a", Spare);
        await _loop.ToggleCellAsync("a", before.First());

        await _loop.CancelEditAsync("a");

        var s = _loop.Current;
        Assert.Null(s.Edit);
        Assert.Equal(before, s.Cells.ToHashSet());
        Assert.Equal(1UL, s.Generation);
    }

    [Fact]
    public async Task Beginning_again_as_the_owner_keeps_the_original_backup()
    {
        await _loop.BeginEditAsync("a");
        await _loop.ToggleCellAsync("a", Spare);
        await _loop.BeginEditAsync("a");

        await _loop.CancelEditAsync("a");

        Assert.Equal(_blinker, _loop.Current.Cells.ToHashSet());
    }

    [Fact]
    public async Task Edits_at_generation_zero_become_the_seed()
    {
        await _loop.BeginEditAsync("a");
        await _loop.ToggleCellAsync("a", Spare);
        await _loop.EndEditAsync("a", resume: false);
        var edited = _loop.Current.Cells.ToHashSet();

        await _loop.StepAsync();
        await _loop.ResetAsync();

        var s = _loop.Current;
        Assert.Equal(0UL, s.Generation);
        Assert.Equal(edited, s.Cells.ToHashSet());
        // The seed centre follows the new bounding box (3 x 4 cells: blinker row plus the spare cell below).
        Assert.Equal(Cell.Centre.Offset(-1, 0).Offset(1, 2), s.SeedCentre);
    }

    [Fact]
    public async Task Edits_after_generation_zero_leave_the_seed_alone()
    {
        await _loop.StepAsync();
        await _loop.BeginEditAsync("a");
        await _loop.ToggleCellAsync("a", Spare);
        await _loop.EndEditAsync("a", resume: false);

        await _loop.ResetAsync();

        var s = _loop.Current;
        Assert.Equal(0UL, s.Generation);
        Assert.Equal(_blinker, s.Cells.ToHashSet());
    }

    [Fact]
    public async Task Clearing_everything_at_generation_zero_makes_the_seed_empty()
    {
        await _loop.BeginEditAsync("a");
        foreach (var cell in _blinker) await _loop.ToggleCellAsync("a", cell);
        await _loop.EndEditAsync("a", resume: false);

        await _loop.ResetAsync();

        Assert.Equal(0, _loop.Current.Population);
    }

    [Fact]
    public async Task Release_ends_the_session_only_for_its_owner()
    {
        await _loop.BeginEditAsync("a");
        await _loop.ToggleCellAsync("a", Spare);

        Assert.False(await _loop.ReleaseEditAsync("b"));
        Assert.NotNull(_loop.Current.Edit);

        Assert.True(await _loop.ReleaseEditAsync("a"));
        Assert.Null(_loop.Current.Edit);
        Assert.Contains(Spare, _loop.Current.Cells);
        Assert.False(await _loop.ReleaseEditAsync("a"));
    }

    [Fact]
    public async Task An_idle_session_expires_and_the_expiry_is_published()
    {
        await _loop.BeginEditAsync("a");
        await _loop.ToggleCellAsync("a", Spare);
        await NextSnapshotAsync(s => s.Edit is not null && s.Population == 4);

        var released = await NextSnapshotAsync(s => s.Edit is null);

        Assert.Contains(Spare, released.Cells);
        await _loop.StartAsync();
        Assert.True(_loop.Current.Running);
    }

    [Fact]
    public async Task Each_edit_restarts_the_idle_timeout()
    {
        await _loop.BeginEditAsync("a");
        var firstDeadline = _loop.Current.Edit!.DeadlineTimestamp;

        await Task.Delay(EditTimeout * 0.6);
        await _loop.ToggleCellAsync("a", Spare);
        Assert.True(_loop.Current.Edit!.DeadlineTimestamp > firstDeadline);

        await Task.Delay(EditTimeout * 0.6);
        Assert.NotNull(_loop.Current.Edit); // 1.2 timeouts since Begin, 0.6 since the last edit

        await NextSnapshotAsync(s => s.Edit is null); // and it still ends once left alone
    }
}
