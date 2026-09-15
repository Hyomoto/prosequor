using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Prosequor.Progress;

public enum ProgressTrack
{
    Player,
    Skill
}

/// <summary>Published after XP has been applied. It never produces chat by itself.</summary>
public sealed record ExperienceGainedEvent(
    IServerPlayer Player,
    ProgressTrack Track,
    string? SkillId,
    float Amount,
    float TotalXp,
    int Level
);

/// <summary>
/// Published after one mutation raises a level. Subscribers may replace the default chat fallback.
/// </summary>
public sealed class LevelUpEvent
{
    public required IServerPlayer Player { get; init; }
    public required ProgressTrack Track { get; init; }
    public string? SkillId { get; init; }
    public required int PreviousLevel { get; init; }
    public required int NewLevel { get; init; }

    /// <summary>
    /// A server-side UI integration may set this while handling the event, then send its own packet.
    /// All subscribers run before Prosequor decides whether to send the normal chat fallback.
    /// </summary>
    public bool SuppressDefaultNotification { get; set; }
}

public interface IProgressEventBus
{
    event Action<ExperienceGainedEvent>? ExperienceGained;
    event Action<LevelUpEvent>? LevelUp;
}

public sealed class ProgressEventBus : IProgressEventBus
{
    ILogger? logger;

    public event Action<ExperienceGainedEvent>? ExperienceGained;
    public event Action<LevelUpEvent>? LevelUp;

    internal void SetLogger(ILogger value) => logger = value;

    internal void Publish(ExperienceGainedEvent evt) =>
        PublishEach(ExperienceGained, evt, nameof(ExperienceGained));

    internal void Publish(LevelUpEvent evt) =>
        PublishEach(LevelUp, evt, nameof(LevelUp));

    void PublishEach<T>(Action<T>? handlers, T evt, string eventName)
    {
        if (handlers == null)
        {
            return;
        }

        foreach (Action<T> handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(evt);
            }
            catch (Exception ex)
            {
                logger?.Error(
                    "[prosequor] Subscriber failed while handling {0}: {1}",
                    eventName,
                    ex);
            }
        }
    }
}
