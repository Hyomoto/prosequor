namespace Prosequor.Xp.Activity;

/// <summary>
/// Prosequor standard deed tokens for <c>when.tags</c> / <see cref="Deed.Emit"/>.
/// Prefer this enum over magic strings; custom mod tokens may still use free-form strings.
/// </summary>
public enum DeedToken
{
    BlockBroken,
    Harvested,
    Crafted,
    /// <summary>Clay-form (and similar hand-shape) progress — not craft-grid <see cref="Crafted"/>.</summary>
    Crafting,
    FishingCatch,
    KilnFired,
    SaddleBreak,
    SaddleTame,
    /// <summary>A domesticated growable advanced a growth stage (crop, bush, sapling, fruit tree, …).</summary>
    Grown,
    /// <summary>Hoe converted soil into farmland (one emit per tilled tile).</summary>
    TillSoil,
    /// <summary>Knife / rip harvest of a dead animal (meat and fat quantity).</summary>
    Butchered,
    /// <summary>Animal killed by a player arrow or thrown spear.</summary>
    Hunted,
    /// <summary>Animal caught in a basket / crate trap.</summary>
    Trapped,
    /// <summary>Farmland slow-release fertilizer transferred into available nutrients.</summary>
    FertilizerAbsorbed,
    /// <summary>Firepit output that is a meal host (cooking pot), not a raw smelt or leftover pot.</summary>
    CookingPot,
    /// <summary>An animal ate from a trough, a crop, or a dropped stack.</summary>
    FedAnimal,
    /// <summary>Liquid metal in a tool/ingot mold hardened into a cast product.</summary>
    MoldCast,
    /// <summary>Finished bloom taken from a bloomery (break or Bloom Brigand extract).</summary>
    BloomeryHarvest,
    /// <summary>Cementation furnace finished carburizing iron into blister steel.</summary>
    CementationFired,

    /// <summary>Block reinforced with the plumb and square.</summary>
    Reinforced,

    /// <summary>Healing item successfully applied (bandage / poultice).</summary>
    Healed
}

/// <summary>String forms for <see cref="DeedToken"/> (JSON <c>when.tags</c> / fact tokens).</summary>
public static class DeedTokenTags
{
    public const string BlockBroken = "block-broken";
    public const string Harvested = "harvested";
    public const string Crafted = "crafted";
    public const string Crafting = "crafting";
    public const string FishingCatch = "fishing-catch";
    public const string KilnFired = "kiln-fired";
    public const string SaddleBreak = "saddle-break";
    public const string SaddleTame = "saddle-tame";
    public const string Grown = "grown";
    /// <summary>Legacy alias accepted in <see cref="TryParse"/>; prefer <see cref="Grown"/>.</summary>
    public const string CropGrown = "crop-grown";

    /// <summary>An animal ate from a trough, a crop, or a dropped stack.</summary>
    public const string FedAnimal = "fed-animal";

    /// <summary>Legacy alias for <see cref="FedAnimal"/>. Compiles and emits as <c>fed-animal</c>.</summary>
    public const string TroughEaten = "trough-eaten";

    /// <summary>Successful milking of an animal.</summary>
    public const string Milked = "milked";

    /// <summary>Animal friendliness score was greater than 5 at emit time.</summary>
    public const string Friendly = "friendly";

    /// <summary>Juvenile animal became adult (<c>EntityBehaviorGrow.BecomeAdult</c>).</summary>
    public const string AgedUp = "aged-up";

    /// <summary>Animal completed a birth (<c>EntityBehaviorMultiply.GiveBirth</c>).</summary>
    public const string GaveBirth = "gave-birth";

    /// <summary>Skep honeycomb harvest (break or Apiary Master extract).</summary>
    public const string SkepHarvest = "skep-harvest";

    /// <summary>Populated hive swarmed into an empty skep (<c>TryPopCurrentSkep</c>).</summary>
    public const string SkepPropagate = "skep-propagate";

    /// <summary>Hoe converted soil into farmland.</summary>
    public const string TillSoil = "till-soil";

    /// <summary>Dead-animal harvest (knife or rip). Quantity is meat + fat after cooking yield.</summary>
    public const string Butchered = "butchered";

    /// <summary>Animal killed by a player arrow or thrown spear.</summary>
    public const string Hunted = "hunted";

    /// <summary>Animal caught in a basket / crate trap.</summary>
    public const string Trapped = "trapped";

    /// <summary>Slow-release fertilizer absorbed into farmland nutrients (whole percent quantity).</summary>
    public const string FertilizerAbsorbed = "fertilizer-absorbed";

    /// <summary>Meal finished in a cooking pot. Not stamped on leftover dirty pots.</summary>
    public const string CookingPot = "cooking-pot";

    /// <summary>Liquid metal hardened in a cast mold.</summary>
    public const string MoldCast = "mold-cast";

    /// <summary>Finished bloom taken from a bloomery.</summary>
    public const string BloomeryHarvest = "bloomery-harvest";

    /// <summary>Cementation furnace finished carburizing (iron → blister steel).</summary>
    public const string CementationFired = "cementation-fired";

    /// <summary>Block reinforced with the plumb and square.</summary>
    public const string Reinforced = "reinforced";

    /// <summary>Healing item successfully applied (bandage / poultice).</summary>
    public const string Healed = "healed";

    /// <summary>Caller identity for pit kilns.</summary>
    public const string PitKiln = "pit-kiln";

    /// <summary>Caller identity for beehive kilns.</summary>
    public const string BeehiveKiln = "beehive-kiln";

    public static string ToTag(this DeedToken token) => token switch
    {
        DeedToken.BlockBroken => BlockBroken,
        DeedToken.Harvested => Harvested,
        DeedToken.Crafted => Crafted,
        DeedToken.Crafting => Crafting,
        DeedToken.FishingCatch => FishingCatch,
        DeedToken.KilnFired => KilnFired,
        DeedToken.SaddleBreak => SaddleBreak,
        DeedToken.SaddleTame => SaddleTame,
        DeedToken.Grown => Grown,
        DeedToken.TillSoil => TillSoil,
        DeedToken.Butchered => Butchered,
        DeedToken.Hunted => Hunted,
        DeedToken.Trapped => Trapped,
        DeedToken.FertilizerAbsorbed => FertilizerAbsorbed,
        DeedToken.CookingPot => CookingPot,
        DeedToken.FedAnimal => FedAnimal,
        DeedToken.MoldCast => MoldCast,
        DeedToken.BloomeryHarvest => BloomeryHarvest,
        DeedToken.CementationFired => CementationFired,
        DeedToken.Reinforced => Reinforced,
        DeedToken.Healed => Healed,
        _ => token.ToString().ToLowerInvariant()
    };

    public static bool TryParse(string? raw, out DeedToken token)
    {
        token = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string t = raw.Trim();
        if (t.Equals(BlockBroken, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.BlockBroken;
            return true;
        }

        if (t.Equals(Harvested, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.Harvested;
            return true;
        }

        if (t.Equals(Crafted, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.Crafted;
            return true;
        }

        if (t.Equals(Crafting, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.Crafting;
            return true;
        }

        if (t.Equals(FishingCatch, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.FishingCatch;
            return true;
        }

        if (t.Equals(KilnFired, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.KilnFired;
            return true;
        }

        if (t.Equals(SaddleBreak, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.SaddleBreak;
            return true;
        }

        if (t.Equals(SaddleTame, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.SaddleTame;
            return true;
        }

        if (t.Equals(Grown, StringComparison.OrdinalIgnoreCase)
            || t.Equals(CropGrown, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.Grown;
            return true;
        }

        if (t.Equals(TillSoil, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.TillSoil;
            return true;
        }

        if (t.Equals(Butchered, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.Butchered;
            return true;
        }

        if (t.Equals(Hunted, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.Hunted;
            return true;
        }

        if (t.Equals(Trapped, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.Trapped;
            return true;
        }

        if (t.Equals(FertilizerAbsorbed, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.FertilizerAbsorbed;
            return true;
        }

        if (t.Equals(CookingPot, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.CookingPot;
            return true;
        }

        if (t.Equals(FedAnimal, StringComparison.OrdinalIgnoreCase)
            || t.Equals(TroughEaten, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.FedAnimal;
            return true;
        }

        if (t.Equals(MoldCast, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.MoldCast;
            return true;
        }

        if (t.Equals(BloomeryHarvest, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.BloomeryHarvest;
            return true;
        }

        if (t.Equals(CementationFired, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.CementationFired;
            return true;
        }

        if (t.Equals(Reinforced, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.Reinforced;
            return true;
        }

        if (t.Equals(Healed, StringComparison.OrdinalIgnoreCase))
        {
            token = DeedToken.Healed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Token stored on a criterion or emit set. <see cref="TroughEaten"/> becomes
    /// <see cref="FedAnimal"/> so old rules match the new emit.
    /// </summary>
    public static string Canonical(string token)
    {
        string t = token.Trim();
        return t.Equals(TroughEaten, StringComparison.OrdinalIgnoreCase) ? FedAnimal : t;
    }
}
