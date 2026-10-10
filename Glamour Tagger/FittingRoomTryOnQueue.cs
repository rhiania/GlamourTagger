using System.Collections.Generic;

namespace GlamourTagger;

/// <summary>
/// Fitting Room try-ons waiting to be sent to the game, oldest first.
/// The game does not reliably apply several try-ons that arrive in the same frame (the brush and the
/// Instant Hover Over Dye Preview try on every dye target at once), so Plugin sends them one at a time.
/// A new request for an item that is still waiting replaces the old one and moves to the end: the
/// newest dyes win, and fast hovering never builds up a backlog.
/// </summary>
public sealed class FittingRoomTryOnQueue
{
    public readonly record struct Request(uint ItemId, byte? Dye1, byte? Dye2);

    private readonly List<Request> pending = new();

    public int Count => pending.Count;

    public void Enqueue(uint itemId, byte? dye1, byte? dye2)
    {
        pending.RemoveAll(request => request.ItemId == itemId);
        pending.Add(new Request(itemId, dye1, dye2));
    }

    public bool TryDequeue(out Request request)
    {
        if (pending.Count == 0)
        {
            request = default;
            return false;
        }

        request = pending[0];
        pending.RemoveAt(0);
        return true;
    }
}