using System.Diagnostics;
using System.Threading.Channels;

namespace GameOfLife.Core;

/// <summary>
/// The single writer of the universe. All mutations are posted as commands and applied on the
/// loop's own task, between generations; after every change an immutable
/// <see cref="UniverseSnapshot"/> is published to the observer and exposed as <see cref="Current"/>.
/// </summary>
public sealed class SimulationLoop
{
    public const int MinGenerationsPerSecond = 1;
    public const int MaxGenerationsPerSecond = 60;
    public const int DefaultGenerationsPerSecond = 10;

    private readonly Universe _universe = new();
    private readonly Channel<Command> _commands = Channel.CreateUnbounded<Command>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Func<UniverseSnapshot, CancellationToken, Task> _observer;

    private bool _running;
    private int _generationsPerSecond = DefaultGenerationsPerSecond;
    private Pattern _seed = Pattern.Empty;
    private Cell _seedTopLeft = Cell.Centre;
    private Cell _seedCentre = Cell.Centre;

    private volatile UniverseSnapshot _current;

    public SimulationLoop(Func<UniverseSnapshot, CancellationToken, Task>? observer = null)
    {
        _observer = observer ?? ((_, _) => Task.CompletedTask);
        _current = BuildSnapshot();
    }

    /// <summary>The latest published snapshot. Never null; starts as an empty, paused universe.</summary>
    public UniverseSnapshot Current => _current;

    public Task StartAsync() => PostAsync(() => _running = true);

    public Task PauseAsync() => PostAsync(() => _running = false);

    /// <summary>Advances one generation regardless of the running state.</summary>
    public Task StepAsync() => PostAsync(() => _universe.Step());

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
        _running = false;
        _universe.Load(_seed.ToUniverseCells(_seedTopLeft));
    });

    /// <summary>Runs until cancelled. Call exactly once.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var reader = _commands.Reader;
        long nextTick = Stopwatch.GetTimestamp();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool changed = false;

                if (!_running)
                {
                    // Nothing to do until someone asks for something.
                    if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) break;
                    nextTick = Stopwatch.GetTimestamp();
                }
                else
                {
                    var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), nextTick);
                    if (wait > TimeSpan.Zero)
                    {
                        var commandArrived = reader.WaitToReadAsync(cancellationToken).AsTask();
                        await Task.WhenAny(commandArrived, Task.Delay(wait, cancellationToken)).ConfigureAwait(false);
                    }
                }

                while (reader.TryRead(out var command))
                {
                    command.Execute();
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

                if (changed)
                {
                    _current = BuildSnapshot();
                    await _observer(_current, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _commands.Writer.TryComplete();
            while (reader.TryRead(out var pending))
                pending.Fail(new OperationCanceledException("The simulation loop has stopped."));
        }
    }

    private UniverseSnapshot BuildSnapshot() =>
        new(_universe.Generation, _universe.Snapshot(), _running, _generationsPerSecond, _seedCentre);

    private Task PostAsync(Action action)
    {
        var command = new Command(action);
        if (!_commands.Writer.TryWrite(command))
            throw new InvalidOperationException("The simulation loop has stopped.");
        return command.Completion;
    }

    private sealed class Command(Action action)
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _tcs.Task;

        public void Execute()
        {
            try
            {
                action();
                _tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
        }

        public void Fail(Exception ex) => _tcs.TrySetException(ex);
    }
}
