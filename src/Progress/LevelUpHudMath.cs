using Prosequor.Data;
using Vintagestory.API.MathTools;

namespace Prosequor.Progress;

public static class LevelUpHudMath
{
    public static float PlayerBarFill(float lifetimeXp, int level, int maxPlayerLevel = XpCurves.PlayerMaxLevel)
    {
        int need = XpCurves.XpToNextPlayerLevel(level, maxPlayerLevel);
        if (need <= 0)
        {
            return 1f;
        }

        float into = XpCurves.InLevelPlayerXp(lifetimeXp, level, maxPlayerLevel);
        return GameMath.Clamp(into / need, 0f, 1f);
    }
}
