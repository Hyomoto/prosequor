using Prosequor.Data;
using Prosequor.Network;
using Prosequor.Player;
using Prosequor.Progress;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Prosequor.Network;

/// <summary>
/// Unlock purchase, HUD, fingerprint, and owner-only progress snapshot/delta channel.
/// </summary>
public class ProgressNetwork
{
    public const string ChannelName = "prosequor";
    const int FingerprintTickMs = 250;

    ICoreServerAPI? sapi;
    ICoreClientAPI? capi;
    IServerNetworkChannel? serverChannel;
    IClientNetworkChannel? clientChannel;
    ContentFingerprint? fingerprint;
    long fingerprintListenerId;

    public event Action<UnlockNodeResultPacket>? UnlockResultReceived;
    public event Action<LevelUpHudPacket>? LevelUpHudReceived;
    public event Action? SkillWaitingDumpRequested;
    public event Action<ContentFingerprintMismatchPacket>? ContentMismatchReceived;
    public event Action<ProgressSnapshotPacket>? ProgressSnapshotReceived;
    public event Action<ProgressDeltaPacket>? ProgressDeltaReceived;
    public event Action? ProgressionProfileReceived;

    public void StartServer(ICoreServerAPI api)
    {
        sapi = api;
        serverChannel = api.Network
            .RegisterChannel(ChannelName)
            .RegisterMessageType<UnlockNodeRequestPacket>()
            .RegisterMessageType<UnlockNodeResultPacket>()
            .RegisterMessageType<LevelUpHudPacket>()
            .RegisterMessageType<SkillWaitingHudStatusPacket>()
            .RegisterMessageType<SkillWaitingHudDumpRequestPacket>()
            .RegisterMessageType<ContentFingerprintPacket>()
            .RegisterMessageType<ContentFingerprintMismatchPacket>()
            .RegisterMessageType<ProgressSnapshotPacket>()
            .RegisterMessageType<ProgressDeltaPacket>()
            .RegisterMessageType<ProgressResyncRequestPacket>()
            .RegisterMessageType<ProgressionProfilePacket>()
            .SetMessageHandler<UnlockNodeRequestPacket>(OnUnlockRequest)
            .SetMessageHandler<SkillWaitingHudStatusPacket>(OnSkillWaitingStatusFromClient)
            .SetMessageHandler<ContentFingerprintPacket>(OnFingerprintFromClient)
            .SetMessageHandler<ProgressResyncRequestPacket>(OnResyncRequest);
    }

    public void StartClient(ICoreClientAPI api)
    {
        capi = api;
        clientChannel = api.Network
            .RegisterChannel(ChannelName)
            .RegisterMessageType<UnlockNodeRequestPacket>()
            .RegisterMessageType<UnlockNodeResultPacket>()
            .RegisterMessageType<LevelUpHudPacket>()
            .RegisterMessageType<SkillWaitingHudStatusPacket>()
            .RegisterMessageType<SkillWaitingHudDumpRequestPacket>()
            .RegisterMessageType<ContentFingerprintPacket>()
            .RegisterMessageType<ContentFingerprintMismatchPacket>()
            .RegisterMessageType<ProgressSnapshotPacket>()
            .RegisterMessageType<ProgressDeltaPacket>()
            .RegisterMessageType<ProgressResyncRequestPacket>()
            .RegisterMessageType<ProgressionProfilePacket>()
            .SetMessageHandler<ProgressionProfilePacket>(OnProgressionProfile)
            .SetMessageHandler<UnlockNodeResultPacket>(OnUnlockResult)
            .SetMessageHandler<LevelUpHudPacket>(OnLevelUpHud)
            .SetMessageHandler<SkillWaitingHudDumpRequestPacket>(_ => SkillWaitingDumpRequested?.Invoke())
            .SetMessageHandler<ContentFingerprintMismatchPacket>(OnContentMismatch)
            .SetMessageHandler<ProgressSnapshotPacket>(OnProgressSnapshot)
            .SetMessageHandler<ProgressDeltaPacket>(OnProgressDelta);
        fingerprintListenerId = api.Event.RegisterGameTickListener(OnFingerprintTick, FingerprintTickMs);
    }

    /// <summary>Compiled-content fingerprint, set after <c>AssetsFinalize</c>.</summary>
    public void SetFingerprint(ContentFingerprint value) => fingerprint = value;

    public void Stop()
    {
        UnregisterFingerprintTick();
        capi = null;
        sapi = null;
    }

    public void SendLevelUpHud(IServerPlayer player, LevelUpHudPacket packet)
    {
        serverChannel?.SendPacket(packet, player);
    }

    public void SendProgressSnapshot(IServerPlayer player, ProgressSnapshotPacket packet)
    {
        serverChannel?.SendPacket(packet, player);
    }

    public void SendProgressionProfile(IServerPlayer player, ProgressionProfile profile)
    {
        serverChannel?.SendPacket(new ProgressionProfilePacket
        {
            MaxPlayerLevel = profile.MaxPlayerLevel,
            SkillPointsPerPlayerLevel = ToRunDtos(profile.SkillPointsPerPlayerLevel),
            SkillPointsPerSkillLevel = ToRunDtos(profile.SkillPointsPerSkillLevel),
            SpecializationLevels = ToRunDtos(profile.SpecializationLevels)
        }, player);
    }

    public void SendProgressDelta(IServerPlayer player, ProgressDeltaPacket packet)
    {
        serverChannel?.SendPacket(packet, player);
    }

    /// <summary>Asks the player's client to print skill-waiting HUD status to chat.</summary>
    public void RequestSkillWaitingDump(IServerPlayer player)
    {
        serverChannel?.SendPacket(new SkillWaitingHudDumpRequestPacket(), player);
    }

    public void RequestProgressResync(int lastSeq)
    {
        if (clientChannel is not { Connected: true })
        {
            return;
        }

        clientChannel.SendPacket(new ProgressResyncRequestPacket { LastSeq = lastSeq });
    }

    /// <summary>
    /// Client diagnostic heartbeat for the skill-waiting HUD (real clients only).
    /// Atlas headless players have no client process, so this never runs there —
    /// use <c>/prosequor skillhint</c> in-game instead. Server still echoes for MP sniffing.
    /// </summary>
    public void SendSkillWaitingStatus(SkillWaitingHudStatusPacket packet)
    {
        // Channel is registered in StartClientSide but not Connected until join finishes.
        if (clientChannel is not { Connected: true })
        {
            return;
        }

        clientChannel.SendPacket(packet);
    }

    void OnSkillWaitingStatusFromClient(IServerPlayer fromPlayer, SkillWaitingHudStatusPacket packet)
    {
        serverChannel?.SendPacket(packet, fromPlayer);
    }

    void OnFingerprintTick(float dt)
    {
        if (fingerprint == null || clientChannel is not { Connected: true })
        {
            return;
        }

        ContentFingerprint local = fingerprint;
        UnregisterFingerprintTick();
        clientChannel.SendPacket(new ContentFingerprintPacket
        {
            Schema = local.Version,
            Hash = local.Hash
        });
    }

    void OnFingerprintFromClient(IServerPlayer fromPlayer, ContentFingerprintPacket packet)
    {
        if (fingerprint == null)
        {
            return;
        }

        ContentFingerprintReport report = ContentFingerprint.Compare(
            fingerprint,
            packet.Schema,
            packet.Hash);
        if (report.Match)
        {
            return;
        }

        sapi?.Logger.Warning(
            "[prosequor] Content fingerprint mismatch for {0} ({1}): client v{2} {3} server v{4} {5}",
            fromPlayer.PlayerName,
            fromPlayer.PlayerUID,
            report.RemoteSchema,
            report.RemoteHash,
            report.LocalSchema,
            report.LocalHash);

        serverChannel?.SendPacket(new ContentFingerprintMismatchPacket
        {
            ServerSchema = report.LocalSchema,
            ServerHash = report.LocalHash,
            ClientSchema = report.RemoteSchema,
            ClientHash = report.RemoteHash
        }, fromPlayer);
    }

    void OnContentMismatch(ContentFingerprintMismatchPacket packet)
    {
        ContentMismatchReceived?.Invoke(packet);
    }

    void OnResyncRequest(IServerPlayer fromPlayer, ProgressResyncRequestPacket packet)
    {
        EntityBehaviorProgress? progress = ProsequorModSystem.TryGetLiveProgress(fromPlayer);
        progress?.SendOwnerSnapshot();
    }

    void UnregisterFingerprintTick()
    {
        if (capi == null || fingerprintListenerId == 0)
        {
            return;
        }

        capi.Event.UnregisterGameTickListener(fingerprintListenerId);
        fingerprintListenerId = 0;
    }

    public void RequestUnlock(string skillId, string nodeId)
    {
        if (clientChannel is not { Connected: true })
        {
            return;
        }

        clientChannel.SendPacket(new UnlockNodeRequestPacket
        {
            SkillId = skillId ?? "",
            NodeId = nodeId ?? ""
        });
    }

    void OnUnlockRequest(IServerPlayer fromPlayer, UnlockNodeRequestPacket packet)
    {
        EntityBehaviorProgress? progress = ProsequorModSystem.TryGetLiveProgress(fromPlayer);
        UnlockPurchaseStatus status = progress == null
            ? UnlockPurchaseStatus.UnknownSkill
            : progress.TryPurchaseNode(packet.SkillId, packet.NodeId);

        serverChannel?.SendPacket(new UnlockNodeResultPacket
        {
            SkillId = packet.SkillId ?? "",
            NodeId = packet.NodeId ?? "",
            Status = status
        }, fromPlayer);
    }

    void OnUnlockResult(UnlockNodeResultPacket packet)
    {
        UnlockResultReceived?.Invoke(packet);
    }

    void OnLevelUpHud(LevelUpHudPacket packet)
    {
        LevelUpHudReceived?.Invoke(packet);
    }

    void OnProgressionProfile(ProgressionProfilePacket packet)
    {
        if (capi == null)
        {
            return;
        }

        ProsequorModSystem.For(capi)?.SetProgression(ProgressionProfile.FromRuns(
            packet.MaxPlayerLevel,
            FromRunDtos(packet.SkillPointsPerPlayerLevel),
            FromRunDtos(packet.SkillPointsPerSkillLevel),
            FromRunDtos(packet.SpecializationLevels)));
        ProgressionProfileReceived?.Invoke();
    }

    void OnProgressSnapshot(ProgressSnapshotPacket packet)
    {
        ApplySnapshotToLocalPlayer(packet);
        ProgressSnapshotReceived?.Invoke(packet);
    }

    void OnProgressDelta(ProgressDeltaPacket packet)
    {
        ApplyDeltaToLocalPlayer(packet);
        ProgressDeltaReceived?.Invoke(packet);
    }

    void ApplySnapshotToLocalPlayer(ProgressSnapshotPacket packet)
    {
        EntityBehaviorProgress? progress = TryLocalProgress();
        progress?.ApplyOwnerSnapshot(packet);
    }

    void ApplyDeltaToLocalPlayer(ProgressDeltaPacket packet)
    {
        EntityBehaviorProgress? progress = TryLocalProgress();
        progress?.ApplyOwnerDelta(packet);
    }

    static List<LevelRunDto> ToRunDtos(LevelSet levels)
    {
        LevelRun[] runs = levels.ToRuns();
        List<LevelRunDto> list = new(runs.Length);
        for (int i = 0; i < runs.Length; i++)
        {
            list.Add(new LevelRunDto
            {
                Start = runs[i].Start,
                End = runs[i].End,
                Step = runs[i].Step
            });
        }

        return list;
    }

    static List<LevelRun> FromRunDtos(List<LevelRunDto>? runs)
    {
        List<LevelRun> list = new();
        if (runs == null)
        {
            return list;
        }

        for (int i = 0; i < runs.Count; i++)
        {
            LevelRunDto dto = runs[i];
            list.Add(new LevelRun(dto.Start, dto.End, dto.Step));
        }

        return list;
    }

    EntityBehaviorProgress? TryLocalProgress()
    {
        IClientPlayer? player = capi?.World?.Player;
        return ProsequorModSystem.TryGetLiveProgress(player);
    }
}
