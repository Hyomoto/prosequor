using System.Collections.Concurrent;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Xp.Activity;

/// <summary>
/// Watcher poll: return null when inactive; otherwise tokens + roles for <see cref="Effort.Emit"/>.
/// </summary>
public delegate EffortPollResult? EffortPoll(IPlayer player, EntityAgent entity);

/// <summary>One active effort sample from a registered poll (feeds the stamp store).</summary>
public readonly record struct EffortPollResult(
    IReadOnlyList<string> Tokens,
    string? Target = null,
    string? Mount = null,
    string? Ground = null,
    string? Channel = null);

/// <summary>
/// Push-based effort stamps. Natural sites call <see cref="Emit"/>; continuous states without a
/// game pulse use <see cref="RegisterPoll"/>. The watcher runs polls, then materializes fresh
/// stamps into <c>prosequor:effort</c> facts.
/// </summary>
public static class Effort
{
    public const string Activity = "prosequor:effort";

    public const string PollIdMount = "prosequor:mount";
    public const string PollIdFishing = "prosequor:fishing";
    public const string PollIdFoot = "prosequor:foot";
    public const string PollIdTemporalDrain = "prosequor:temporal-drain";

    /// <summary>How long a stamp stays fresh (game seconds).</summary>
    public const double FreshWindowGameSeconds = 1.0;

    static readonly EffortStampStore store = new();
    static readonly EffortPollRegistry polls = new();

    public static EffortStampStore Store => store;

    public static IReadOnlyList<KeyValuePair<string, EffortPoll>> Polls => polls.All;

    public static void Emit(
        IPlayer player,
        EffortToken token,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? channel = null) =>
        Emit(player, [token.ToTag()], target, mount, ground, channel ?? token.ToTag());

    public static void Emit(
        IPlayer player,
        IReadOnlyList<EffortToken> tokens,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? channel = null)
    {
        if (tokens == null || tokens.Count == 0)
        {
            return;
        }

        string[] tags = new string[tokens.Count];
        for (int i = 0; i < tokens.Count; i++)
        {
            tags[i] = tokens[i].ToTag();
        }

        Emit(player, tags, target, mount, ground, channel ?? tags[0]);
    }

    public static void Emit(
        IPlayer player,
        IReadOnlyList<string> tokens,
        string? target = null,
        string? mount = null,
        string? ground = null,
        string? channel = null)
    {
        if (player?.PlayerUID == null || tokens == null || tokens.Count == 0)
        {
            return;
        }

        double hours = player.Entity?.World?.Calendar?.TotalHours ?? 0.0;
        string channelId = string.IsNullOrWhiteSpace(channel) ? tokens[0] : channel.Trim();
        store.Put(
            player.PlayerUID,
            channelId,
            tokens,
            target,
            mount,
            ground,
            hours);
    }

    public static void Emit(IPlayer player, EffortPollResult sample) =>
        Emit(player, sample.Tokens, sample.Target, sample.Mount, sample.Ground, sample.Channel);

    /// <summary>
    /// Register a watcher poll for continuous effort without a natural emit site.
    /// Same id last-wins. Prefer <see cref="Emit"/> when the game already pulses.
    /// </summary>
    public static void RegisterPoll(string id, EffortPoll poll) => polls.Register(id, poll);

    public static void UnregisterPoll(string id) => polls.Unregister(id);

    public static void Forget(string playerUid) => store.Forget(playerUid);

    /// <summary>Run all polls for this player and Emit any non-null samples.</summary>
    public static void RunPolls(IPlayer player, EntityAgent entity)
    {
        if (player == null || entity == null)
        {
            return;
        }

        foreach (KeyValuePair<string, EffortPoll> kv in polls.All)
        {
            EffortPollResult? sample = kv.Value(player, entity);
            if (sample == null || sample.Value.Tokens == null || sample.Value.Tokens.Count == 0)
            {
                continue;
            }

            Emit(player, sample.Value);
        }
    }
}

/// <summary>Last-win registry of effort poll callbacks.</summary>
public sealed class EffortPollRegistry
{
    readonly List<KeyValuePair<string, EffortPoll>> ordered = new();
    readonly Dictionary<string, EffortPoll> byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<KeyValuePair<string, EffortPoll>> All => ordered;

    public void Register(string id, EffortPoll poll)
    {
        if (poll == null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        string key = id.Trim();
        if (byId.ContainsKey(key))
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                if (string.Equals(ordered[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    ordered.RemoveAt(i);
                    break;
                }
            }
        }

        byId[key] = poll;
        ordered.Add(new KeyValuePair<string, EffortPoll>(key, poll));
    }

    public void Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        string key = id.Trim();
        if (!byId.Remove(key))
        {
            return;
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            if (string.Equals(ordered[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                ordered.RemoveAt(i);
                return;
            }
        }
    }
}

/// <summary>Per-player effort stamp channels.</summary>
public sealed class EffortStampStore
{
    readonly ConcurrentDictionary<string, ConcurrentDictionary<string, EffortStamp>> byPlayer =
        new(StringComparer.Ordinal);

    public void Put(
        string playerUid,
        string channel,
        IReadOnlyList<string> tokens,
        string? target,
        string? mount,
        string? ground,
        double totalHours)
    {
        if (string.IsNullOrWhiteSpace(playerUid) || string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        HashSet<string> tokenSet = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? raw in tokens)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                tokenSet.Add(raw.Trim());
            }
        }

        if (tokenSet.Count == 0)
        {
            return;
        }

        ConcurrentDictionary<string, EffortStamp> channels = byPlayer.GetOrAdd(
            playerUid,
            _ => new ConcurrentDictionary<string, EffortStamp>(StringComparer.OrdinalIgnoreCase));
        channels[channel.Trim()] = new EffortStamp(
            channel.Trim(),
            tokenSet,
            string.IsNullOrWhiteSpace(target) ? null : target.Trim(),
            string.IsNullOrWhiteSpace(mount) ? null : mount.Trim(),
            string.IsNullOrWhiteSpace(ground) ? null : ground.Trim(),
            totalHours);
    }

    public IReadOnlyList<EffortStamp> GetFresh(string playerUid, double nowTotalHours)
    {
        if (!byPlayer.TryGetValue(playerUid, out ConcurrentDictionary<string, EffortStamp>? channels))
        {
            return Array.Empty<EffortStamp>();
        }

        List<EffortStamp> fresh = new();
        foreach (KeyValuePair<string, EffortStamp> kv in channels)
        {
            double ageSeconds =
                (nowTotalHours - kv.Value.TotalHours) * XpBucketFormulas.GameSecondsPerHour;
            if (ageSeconds < 0 || ageSeconds > Effort.FreshWindowGameSeconds)
            {
                continue;
            }

            fresh.Add(kv.Value);
        }

        return fresh;
    }

    public void Forget(string playerUid)
    {
        if (!string.IsNullOrEmpty(playerUid))
        {
            byPlayer.TryRemove(playerUid, out _);
        }
    }
}

/// <summary>One effort stamp channel.</summary>
public readonly record struct EffortStamp(
    string Channel,
    IReadOnlySet<string> Tokens,
    string? Target,
    string? Mount,
    string? Ground,
    double TotalHours);
