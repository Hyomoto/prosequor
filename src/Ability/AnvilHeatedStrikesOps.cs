using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Heated Strikes: shrink passive cooling debt by advancing
/// <c>temperatureLastUpdate</c> (no heat added, no rate change).
/// </summary>
public static class AnvilHeatedStrikesOps
{
    public const string TemperatureAttr = "temperature";
    public const string LastUpdateKey = "temperatureLastUpdate";

    /// <summary>
    /// <c>lastUpdate += pct * (now - lastUpdate)</c>, clamped to <paramref name="nowHours"/>.
    /// Returns true when the tree was mutated.
    /// </summary>
    public static bool TryShrinkDebt(ITreeAttribute? temp, double nowHours, float pct)
    {
        if (temp == null || pct <= 0f || !temp.HasAttribute(LastUpdateKey))
        {
            return false;
        }

        double lastUpdate = temp.GetDouble(LastUpdateKey);
        double debt = nowHours - lastUpdate;
        if (debt <= 0)
        {
            return false;
        }

        double next = lastUpdate + pct * debt;
        if (next > nowHours)
        {
            next = nowHours;
        }

        if (Math.Abs(next - lastUpdate) < 1e-12)
        {
            return false;
        }

        temp.SetDouble(LastUpdateKey, next);
        return true;
    }

    public static void OnStrike(BlockEntityAnvil anvil, IPlayer player)
    {
        if (anvil?.Api?.Side != EnumAppSide.Server || player == null)
        {
            return;
        }

        ItemStack? work = anvil.WorkItemStack;
        if (work?.Attributes == null)
        {
            return;
        }

        float pct = AnvilWorkStation.ResolveDecayShrink(player);
        if (pct <= 0f)
        {
            return;
        }

        if (work.Attributes[TemperatureAttr] is not ITreeAttribute temp)
        {
            return;
        }

        double now = anvil.Api.World.Calendar.TotalHours;
        if (!TryShrinkDebt(temp, now, pct))
        {
            return;
        }

        anvil.MarkDirty(redrawOnClient: true);
    }
}
