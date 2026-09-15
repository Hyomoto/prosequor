using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Shared craft-quality grade stamp (<c>Quality:</c> footer). Rank lives on
/// <c>prosequor:quality-rank</c>; attribute/affix stamps live on <c>prosequor:quality</c>.
/// Items in collection <c>distilled</c> never get the grade — mash rank is spent
/// as a quality-base addend, then stripped from the spirit.
/// </summary>
public static class QualityGrade
{
    public const string DistilledCollectionId = "distilled";

    public static void TryStamp(
        AffixListRegistry affixLists,
        CraftMutateOutputContext context,
        ItemStack stack,
        float mean)
    {
        if (mean <= QualityMath.RawMin)
        {
            return;
        }

        if (mean < context.BestQualityMean)
        {
            return;
        }

        if (IsDistilled(context, stack))
        {
            context.BestQualityMean = mean;
            return;
        }

        if (!affixLists.TryGet("prosequor:quality", out AffixListDef gradeList)
            || gradeList.Entries.Count == 0)
        {
            return;
        }

        int idx = QualityMath.GradeIndex(gradeList.Entries.Count, mean);
        if (idx < 0 || idx >= gradeList.Entries.Count)
        {
            return;
        }

        AffixListEntryDef entry = gradeList.Entries[idx];
        if (ItemAffixes.SetFront(stack, ItemAffixes.QualityCode, entry.Lang, entry.Color))
        {
            context.BestQualityMean = mean;
        }
    }

    static bool IsDistilled(CraftMutateOutputContext context, ItemStack stack)
    {
        CollectionIndex? index = context.Collections;
        if (index == null && context.Player?.Entity?.Api != null)
        {
            index = ProsequorModSystem.For(context.Player.Entity.Api)?.Collections?.Index;
        }

        return index != null && index.StackMatches(DistilledCollectionId, stack);
    }
}
