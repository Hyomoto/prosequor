using Prosequor.Data;
using Vintagestory.API.MathTools;

namespace Prosequor.Progress;

public static class LevelUpHudMath
{
    public static float PlayerBarFill(float lifetimeXp, int level, int maxLevel = XpCurves.PlayerMaxLevel)
    {
        int need = XpCurves.XpToNextPlayerLevel(level, maxLevel);
        if (need <= 0)
        {
            return 1f;
        }

        float into = XpCurves.InLevelPlayerXp(lifetimeXp, level, maxLevel);
        return GameMath.Clamp(into / need, 0f, 1f);
    }
}
