using System.Diagnostics;
using System.Threading.Channels;

namespace GameOfLife.Core;

/// <summary>
/// The single writer of the universe. All mutations are posted as commands and applied on the
/// loop's own task, between generations; after every change an immutable
/// <see cref="UniverseSnapshot"/> is published to the observer and exposed as <see cref="Current"/>.
/// </summary>
/// <remarks>
/// Hand editing is exclusive: <see cref="BeginEditAsync"/> gives one owner an <see cref="EditSession"/>
/// while the simulation is paused. Until the owner ends it, it expires, or it is released, every
/// other change to the universe (start, step, reset, load) is refused with
/// <see cref="EditInProgressException"/>. Edits made at generation 0 become the new seed. Ending a
/// session may resume the simulation; expiry and release leave it paused.
/// </remarks>
public sealed class SimulationLoop
{
    public const int MinGenerationsPerSecond = 1;
    public const int MaxGenerationsPerSecond = 60;
    public const int DefaultGenerationsPerSecond = 10;

    /// <summary>How long an edit session survives without an edit before it ends on its own.</summary>
    public static readonly TimeSpan DefaultEditTimeout = TimeSpan.FromMinutes(5);

    private readonly Universe _universe = new();
    private readonly Channel<Command> _commands = Channel.CreateUnbounded<Command>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Func<UniverseSnapshot, CancellationToken, Task> _observer;

    private bool _running;
    private int _generationsPerSecond = DefaultGenerationsPerSecond;
    private Pattern _seed = Pattern.Empty;
    private Cell _seedTopLeft = Cell.Centre;
    private Cell _seedCentre = Cell.Centre;
    private EditSession? _edit;
    private Cell[]? _editBackup;

    private volatile UniverseSnapshot _current;

    public SimulationLoop(Func<UniverseSnapshot, CancellationToken, Task>? observer = null, TimeSpan? editTimeout = null)
    {
        EditTimeout = editTimeout ?? DefaultEditTimeout;
        if (EditTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(editTimeout), "The edit timeout must be positive.");
        _observer = observer ?? ((_, _) => Task.CompletedTask);
        _current = BuildSnapshot();
    }

    /// <summary>The latest published snapshot. Never null; starts as an empty, paused universe.</summary>
    public UniverseSnapshot Current => _current;

    public TimeSpan EditTimeout { get; }

    public Task StartAsync() => PostAsync(() => { RequireNotEditing(); _running = true; });

    /// <summary>Pausing is always allowed; it never conflicts with an edit session.</summary>
    public Task PauseAsync() => PostAsync(() => _running = false);

    /// <summary>Advances one generation regardless of the running state.</summary>
    public Task StepAsync() => PostAsync(() => { RequireNotEditing(); _universe.Step(); });

    public Task SetSpeedAsync(int generationsPerSecond) =>
        PostAsync(() => _generationsPerSecond = Math.Clamp(generationsPerSecond, MinGenerationsPerSecond, MaxGenerationsPerSecond));

    /// <summary>
    /// Replaces the universe with <paramref name="pattern"/> and pauses. The pattern is placed at its
    /// own <see cref="Pattern.Origin"/> when it has one, otherwise centred on the universe centre.
    /// The pattern's centre becomes the seed centre that new viewports are anchored to.
    /// </summary>
    public Task LoadAsync(Pattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return PostAsync(() =>
        {
            RequireNotEditing();
            _seed = pattern;
            _seedTopLeft = pattern.Origin ?? pattern.TopLeftWhenCentredAt(Cell.Centre);
            _seedCentre = _seedTopLeft.Offset(pattern.Width / 2, pattern.Height / 2);
            _running = false;
            _universe.Load(pattern.ToUniverseCells(_seedTopLeft));
        });
    }

    /// <summary>Restores the most recently loaded seed at generation 0 and pauses.</summary>
    public Task ResetAsync() => PostAsync(() =>
    {
        RequireNotEditing();
        _running = false;
        _universe.Load(_seed.ToUniverseCells(_seedTopLeft));
    });

    // --- editing ---

    /// <summary>
    /// Gives <paramref name="owner"/> the exclusive right to edit. Requires a paused simulation and
    /// no other owner. Calling it again as the current owner just restarts the idle timeout.
    /// </summary>
    public Task BeginEditAsync(string owner)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        return PostAsync(() =>
        {
            if (_edit is not null && _edit.Owner != owner)
                throw new EditInProgressException("Another client is editing the universe.");
            if (_running)
                throw new InvalidOperationException("Pause the simulation before editing.");
            _editBackup ??= _universe.Snapshot();
            _edit = new EditSession(owner, EditSession.DeadlineAfter(EditTimeout), EditTimeout);
        });
    }

    /// <summary>Flips one cell as the session owner and restarts the idle timeout.</summary>
    public Task ToggleCellAsync(string owner, Cell cell) => PostAsync(() =>
    {
        RequireOwner(owner);
        _universe.Toggle(cell);
        _edit = _edit! with { DeadlineTimestamp = EditSession.DeadlineAfter(EditTimeout) };
    });

    /// <summary>
    /// Ends the owner's session, keeping the edits. With <paramref name="resume"/> the simulation
    /// starts running in the same step, so nobody else can slip a change in between.
    /// </summary>
    public Task EndEditAsync(string owner, bool resume) => PostAsync(() =>
    {
        RequireOwner(owner);
        CommitEdit();
        if (resume) _running = true;
    });

    /// <summary>Ends the owner's session and restores the universe as it was when editing began.</summary>
    public Task CancelEditAsync(string owner) => PostAsync(() =>
    {
        RequireOwner(owner);
        _universe.Replace(_editBackup!);
        _edit = null;
        _editBackup = null;
    });

    /// <summary>
    /// Ends the session if <paramref name="owner"/> holds it, keeping the edits; a no-op otherwise.
    /// For callers that cannot know whether the owner was editing, such as a disconnect handler.
    /// </summary>
    public Task<bool> ReleaseEditAsync(string owner) => PostAsync(() =>
    {
        if (_edit?.Owner != owner) return false;
        CommitEdit();
        return true;
    });

    private void RequireNotEditing()
    {
        if (_edit is not null)
            throw new EditInProgressException("The universe is being edited. Wait until the editor is done.");
    }

    private void RequireOwner(string owner)
    {
        if (_edit is null)
            throw new InvalidOperationException("Nobody is editing the universe.");
        if (_edit.Owner != owner)
            throw new EditInProgressException("Another client is editing the universe.");
    }

    private void CommitEdit()
    {
        if (_universe.Generation == 0)
            AdoptUniverseAsSeed();
        _edit = null;
        _editBackup = null;
    }

    /// <summary>Edits at generation 0 redefine the seed, so Reset returns to what was drawn.</summary>
    private void AdoptUniverseAsSeed()
    {
        var cells = _universe.LiveCells;
        if (cells.Count == 0)
        {
            _seed = Pattern.Empty with { Name = _seed.Name };
            return;
        }

        try
        {
            var pattern = Pattern.FromUniverseCells(cells, _seed.Name);
            _seed = pattern;
            _seedTopLeft = pattern.Origin!.Value;
            _seedCentre = _seedTopLeft.Offset(pattern.Width / 2, pattern.Height / 2);
        }
        catch (InvalidOperationException)
        {
            // Spread too far for a pattern (e.g. across the seam): keep the previous seed.
        }
    }

    /// <summary>Runs until cancelled. Call exactly once.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var reader = _commands.Reader;
        var executed = new List<Command>();
        long nextTick = Stopwatch.GetTimestamp();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool changed = false;

                if (!_running)
                {
                    if (_edit is { } edit)
                    {
                        // Wake for a command or for the session's expiry, whichever comes first.
                        if (!await WaitForCommandAsync(reader, edit.Remaining, cancellationToken).ConfigureAwait(false)
                            && reader.Completion.IsCompleted) break;
                    }
                    else
                    {
                        // Nothing to do until someone asks for something.
                        if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) break;
                    }
                    nextTick = Stopwatch.GetTimestamp();
                }
                else
                {
                    var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), nextTick);
                    if (wait > TimeSpan.Zero)
                        await WaitForCommandAsync(reader, wait, cancellationToken).ConfigureAwait(false);
                }

                // Commands run now but complete only after the snapshot is rebuilt, so a caller
                // that awaits one sees its effect in Current as soon as the await returns.
                while (reader.TryRead(out var command))
                {
                    command.Execute();
                    executed.Add(command);
                    changed = true;
                }

                if (_edit is { Expired: true })
                {
                    CommitEdit();
                    changed = true;
                }

                if (_running && Stopwatch.GetElapsedTime(nextTick) >= TimeSpan.Zero)
                {
                    _universe.Step();
                    changed = true;
                    long period = Stopwatch.Frequency / _generationsPerSecond;
                    nextTick += period;
                    // If we fell behind (slow observer, GC pause), don't try to catch up in a burst.
                    if (Stopwatch.GetTimestamp() - nextTick > period) nextTick = Stopwatch.GetTimestamp() + period;
                }

                if (changed) _current = BuildSnapshot();
                foreach (var command in executed) command.Complete();
                executed.Clear();
                if (changed) await _observer(_current, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _commands.Writer.TryComplete();
            var stopped = new OperationCanceledException("The simulation loop has stopped.");
            foreach (var command in executed) command.Fail(stopped);
            while (reader.TryRead(out var pending))
                pending.Fail(stopped);
        }
    }

    /// <summary>Waits until a command is available or <paramref name="timeout"/> passes; true if a command arrived.</summary>
    private static async Task<bool> WaitForCommandAsync(ChannelReader<Command> reader, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) return reader.TryPeek(out _);
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var commandArrived = reader.WaitToReadAsync(timer.Token).AsTask();
        var completed = await Task.WhenAny(commandArrived, Task.Delay(timeout, timer.Token)).ConfigureAwait(false);
        timer.Cancel(); // release whichever of the two is still pending
        cancellationToken.ThrowIfCancellationRequested();
        return completed == commandArrived && await commandArrived.ConfigureAwait(false);
    }

    private UniverseSnapshot BuildSnapshot() =>
        new(_universe.Generation, _universe.SnapshotIndexed(), _running, _generationsPerSecond, _seedCentre, _edit);

    private Task PostAsync(Action action) => PostAsync<object?>(() => { action(); return null; });

    private Task<T> PostAsync<T>(Func<T> action)
    {
        var command = new Command<T>(action);
        if (!_commands.Writer.TryWrite(command))
            throw new InvalidOperationException("The simulation loop has stopped.");
        return command.Completion;
    }

    private abstract class Command
    {
        /// <summary>Runs the action on the loop, capturing its outcome without completing the caller yet.</summary>
        public abstract void Execute();

        /// <summary>Hands the captured outcome to the caller.</summary>
        public abstract void Complete();

        public abstract void Fail(Exception ex);
    }

    private sealed class Command<T>(Func<T> action) : Command
    {
        private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private T? _result;
        private Exception? _error;

        public Task<T> Completion => _tcs.Task;

        public override void Execute()
        {
            try
            {
                _result = action();
            }
            catch (Exception ex)
            {
                _error = ex;
            }
        }

        public override void Complete()
        {
            if (_error is null) _tcs.TrySetResult(_result!);
            else _tcs.TrySetException(_error);
        }

        public override void Fail(Exception ex) => _tcs.TrySetException(ex);
    }
}
