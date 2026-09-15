using Vintagestory.API.Client;

namespace Prosequor.Client;

/// <summary>Persistent HUD host for the unspent unlock-points reminder icon.</summary>
public sealed class HudElementSkillWaiting : HudElement
{
    readonly SkillWaitingHudController controller;

    public HudElementSkillWaiting(ICoreClientAPI capi, SkillWaitingHudController controller)
        : base(capi)
    {
        this.controller = controller;
        TryOpen();
    }

    /// <summary>After the hotbar (default 0.1) so the icon is not covered by its frame.</summary>
    public override double DrawOrder => 0.15;

    public override bool Focusable => false;

    public override bool ShouldReceiveMouseEvents() => false;

    public override bool ShouldReceiveKeyboardEvents() => false;

    public override void OnRenderGUI(float deltaTime)
    {
        controller.Tick(deltaTime);
        base.OnRenderGUI(deltaTime);

        if (!controller.ShouldDraw)
        {
            return;
        }

        controller.Draw(deltaTime);
    }

    public override void Dispose()
    {
        controller.Dispose();
        base.Dispose();
    }
}
