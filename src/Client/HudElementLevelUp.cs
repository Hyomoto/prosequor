using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Prosequor.Client;

/// <summary>Fullscreen HUD host for the Skyrim-style level-up banner.</summary>
public sealed class HudElementLevelUp : HudElement
{
    public const double VerticalAnchor = 0.38;

    readonly LevelUpHudController controller;

    public HudElementLevelUp(ICoreClientAPI capi, LevelUpHudController controller) : base(capi)
    {
        this.controller = controller;
        TryOpen();
    }

    public override bool Focusable => false;

    public override bool ShouldReceiveMouseEvents() => false;

    public override bool ShouldReceiveKeyboardEvents() => false;

    public override void OnRenderGUI(float deltaTime)
    {
        controller.Tick(deltaTime);
        base.OnRenderGUI(deltaTime);

        if (!controller.HasActive)
        {
            return;
        }

        double centerX = capi.Render.FrameWidth / 2.0;
        double topY = capi.Render.FrameHeight * VerticalAnchor;
        controller.Draw(capi, centerX, topY, RuntimeEnv.GUIScale);
    }

    public override void Dispose()
    {
        controller.Dispose();
        base.Dispose();
    }
}
