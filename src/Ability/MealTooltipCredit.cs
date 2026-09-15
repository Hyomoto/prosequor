using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Prosequor.Ability;

/// <summary>
/// Puts meal cook credit under the serving line, before Nutrition Facts, so it reads as
/// part of the meal. Vessel Created By stays the tooltip footer.
/// </summary>
public static class MealTooltipCredit
{
    public static void InsertBeforeNutritionFacts(StringBuilder? dsc, IWorldAccessor? world, ItemStack? stack)
    {
        if (dsc == null
            || stack == null
            || !MealHostCredit.IsMealVessel(stack)
            || !ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob blob))
        {
            return;
        }

        InsertBeforeNutritionFacts(dsc, world, blob);
    }

    public static void InsertBeforeNutritionFacts(StringBuilder? dsc, IWorldAccessor? world, ProsequorBlob blob)
    {
        if (dsc == null || !OwnerCredit.TryFormatPreparedBy(world, blob, out string line))
        {
            return;
        }

        string text = dsc.ToString();
        if (text.IndexOf(line, StringComparison.Ordinal) >= 0)
        {
            return;
        }

        int at = IndexOfNutritionFactsLine(text);
        if (at < 0)
        {
            return;
        }

        dsc.Insert(at, line + "\n");
    }

    public static void InsertBeforeNutritionFacts(ref string info, IWorldAccessor? world, ProsequorBlob blob)
    {
        if (!OwnerCredit.TryFormatPreparedBy(world, blob, out string line))
        {
            return;
        }

        if (string.IsNullOrEmpty(info))
        {
            return;
        }

        if (info.IndexOf(line, StringComparison.Ordinal) >= 0)
        {
            return;
        }

        int at = IndexOfNutritionFactsLine(info);
        if (at < 0)
        {
            return;
        }

        info = info.Insert(at, line + "\n");
    }

    /// <summary>Start of the Nutrition Facts line, or -1.</summary>
    public static int IndexOfNutritionFactsLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return -1;
        }

        string marker = NutritionFactsMarker();
        int at = 0;
        while (at <= text.Length)
        {
            int found = text.IndexOf(marker, at, StringComparison.Ordinal);
            if (found < 0)
            {
                return -1;
            }

            if (found == 0 || text[found - 1] == '\n')
            {
                return found;
            }

            at = found + marker.Length;
        }

        return -1;
    }

    static string NutritionFactsMarker()
    {
        try
        {
            string localized = Lang.Get("Nutrition Facts");
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }
        }
        catch
        {
            // Pure fixtures may lack a loaded lang table.
        }

        return "Nutrition Facts";
    }
}
