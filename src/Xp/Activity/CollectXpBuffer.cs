using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;

namespace Prosequor.Xp.Activity;

/// <summary>
/// Per-player pending collect-XP bag: dumb <c>code → quantity</c>, flushed on the
/// activity-watch bucket cadence. Logout / forget discards without FatherXp.
/// </summary>
public sealed class CollectXpBuffer
{
    readonly Dictionary<string, Dictionary<string, int>> byPlayer =
        new(StringComparer.Ordinal);

    /// <summary>Add <paramref name="qty"/> of <paramref name="code"/> for the player.</summary>
    public void Enqueue(string? playerUid, string? code, int qty)
    {
        if (string.IsNullOrWhiteSpace(playerUid)
            || string.IsNullOrWhiteSpace(code)
            || qty <= 0)
        {
            return;
        }

        string uid = playerUid.Trim();
        string key = code.Trim();
        if (!byPlayer.TryGetValue(uid, out Dictionary<string, int>? bag))
        {
            bag = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            byPlayer[uid] = bag;
        }

        bag.TryGetValue(key, out int current);
        bag[key] = current + qty;
    }

    /// <summary>True when the player has any pending collect entries.</summary>
    public bool HasPending(string? playerUid) =>
        !string.IsNullOrWhiteSpace(playerUid)
        && byPlayer.TryGetValue(playerUid.Trim(), out Dictionary<string, int>? bag)
        && bag.Count > 0;

    /// <summary>
    /// Snapshot and clear the player's bag, then emit one collected deed per code
    /// with <c>pay: quantity</c> semantics (quantityUnits).
    /// </summary>
    public void Flush(ICoreAPI? api, string? playerUid)
    {
        if (api == null || string.IsNullOrWhiteSpace(playerUid))
        {
            return;
        }

        string uid = playerUid.Trim();
        if (!byPlayer.Remove(uid, out Dictionary<string, int>? bag) || bag.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<string, int> pair in bag)
        {
            if (pair.Value <= 0 || string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            Deed.Emit(
                api,
                uid,
                tokens: Array.Empty<string>(),
                caller: CallerIdentities.Hand,
                target: pair.Key,
                outputs: [new Deed.QuantityUnit(pair.Key, pair.Value)],
                activity: CollectXpItem.Activity);
        }
    }

    /// <summary>Drop pending entries for <paramref name="playerUid"/> (logout / stale).</summary>
    public void Discard(string? playerUid)
    {
        if (string.IsNullOrWhiteSpace(playerUid))
        {
            return;
        }

        byPlayer.Remove(playerUid.Trim());
    }

    /// <summary>Test helper: pending qty for a code, or 0.</summary>
    public int Peek(string? playerUid, string? code)
    {
        if (string.IsNullOrWhiteSpace(playerUid)
            || string.IsNullOrWhiteSpace(code)
            || !byPlayer.TryGetValue(playerUid.Trim(), out Dictionary<string, int>? bag))
        {
            return 0;
        }

        return bag.TryGetValue(code.Trim(), out int qty) ? qty : 0;
    }
}
