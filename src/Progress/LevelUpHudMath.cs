using Prosequor.Data;
using Vintagestory.API.MathTools;

namespace Prosequor.Progress;

public static class LevelUpHudMath
{
    public static float PlayerBarFill(float lifetimeXp, int level)
    {
        int need = XpCurves.XpToNextPlayerLevel(level);
        if (need <= 0)
        {
            return 1f;
        }

        float into = XpCurves.InLevelPlayerXp(lifetimeXp, level);
        return GameMath.Clamp(into / need, 0f, 1f);
    }
}
