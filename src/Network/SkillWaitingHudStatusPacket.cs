using ProtoBuf;

namespace Prosequor.Network;

/// <summary>
/// Client → server skill-waiting HUD diagnostics (real clients). Server may echo for sniffing.
/// Atlas headless players have no client process — use <c>/prosequor skillhint</c> in-game.
/// </summary>
[ProtoContract]
public class SkillWaitingHudStatusPacket
{
    [ProtoMember(1)]
    public bool Armed { get; set; }

    [ProtoMember(2)]
    public bool ShouldDraw { get; set; }

    [ProtoMember(3)]
    public bool HasSyncedMirror { get; set; }

    [ProtoMember(4)]
    public int UnlockPoints { get; set; }

    [ProtoMember(5)]
    public bool MenuOpen { get; set; }

    [ProtoMember(6)]
    public bool BgTextureOk { get; set; }

    [ProtoMember(7)]
    public bool EmblemTextureOk { get; set; }

    [ProtoMember(8)]
    public float DrawX { get; set; }

    [ProtoMember(9)]
    public float DrawY { get; set; }

    [ProtoMember(10)]
    public float IconSize { get; set; }

    [ProtoMember(11)]
    public int FrameWidth { get; set; }

    [ProtoMember(12)]
    public int FrameHeight { get; set; }

    [ProtoMember(13)]
    public bool HudStarted { get; set; }

    [ProtoMember(14)]
    public bool HudElementOpen { get; set; }
}
