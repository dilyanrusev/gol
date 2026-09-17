using System.Diagnostics;

namespace GameOfLife.Core;

/// <summary>
/// An exclusive hold on the universe for hand editing. While one exists, nothing but the owner's
/// edits may change the universe. It expires <see cref="Timeout"/> after the owner's last edit.
/// </summary>
/// <param name="Owner">Opaque identity of the editing client (a connection id, never shown to other clients).</param>
/// <param name="DeadlineTimestamp">A <see cref="Stopwatch"/> timestamp at which the session expires.</param>
/// <param name="Timeout">The full idle timeout, so a client can show how much of it is left.</param>
public sealed record EditSession(string Owner, long DeadlineTimestamp, TimeSpan Timeout)
{
    /// <summary>Time left before the session expires, never negative.</summary>
    public TimeSpan Remaining
    {
        get
        {
            var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), DeadlineTimestamp);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public bool Expired => Stopwatch.GetTimestamp() >= DeadlineTimestamp;

    public static long DeadlineAfter(TimeSpan timeout) =>
        Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
}

/// <summary>Thrown when a change is refused because a client holds an <see cref="EditSession"/>.</summary>
public sealed class EditInProgressException(string message) : InvalidOperationException(message);
