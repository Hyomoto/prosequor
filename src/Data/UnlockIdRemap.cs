namespace Prosequor.Data;

/// <summary>
/// One-pass unlock id renames applied on load. Keys are stored ids; values are current ids.
/// Lookups are not chained, so forestry <c>lumberjack</c> and <c>forester</c> can swap in one step.
/// </summary>
public static class UnlockIdRemap
{
    /// <summary>Progress schema that first includes these ids. Older blobs are remapped once.</summary>
    public const int Schema = 5;
    static readonly Dictionary<string, Dictionary<string, string>> BySkill =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["farming"] = Map(
                ("demetersbless", "seed-bank"),
                ("bushnursery", "pomologist"),
                ("composting", "soil-enrichment"),
                ("orchardist", "fruit-picker"),
                ("recycler", "edaphologist"),
                ("grafter", "orchidist"),
                ("farmer", "farmhand")),
            ["forestry"] = Map(
                ("carefullumberjack", "axe-care"),
                ("afforestation", "sapling-saver"),
                ("treenursery", "woodland-spirit"),
                ("resinfarmer", "sapjack"),
                ("axeexpert", "tree-felling"),
                ("lumberjack", "seasoned-logger"),
                ("forester", "lumberjack")),
            ["riding"] = Map(
                ("tirelesstrot", "jockey"),
                ("lighttack", "steppe-rider")),
            ["fishing"] = Map(
                ("magnetichook", "junk-attractor"),
                ("coastalmaster", "saltwater-fishing"),
                ("riverspecialist", "freshwater-fishing"),
                ("autobaiter", "baiting"),
                ("baitmaster", "superior-bait"),
                ("goodbait", "master-baiter"),
                ("strongline", "line-care")),
            ["digging"] = Map(
                ("carefuldigger", "shovel-care"),
                ("saltpeterdigger", "saltpeter-digging"),
                ("claydigger", "clay-digging"),
                ("peatcutter", "peat-digging"),
                ("mixedclay", "clay-spotter"),
                ("digger", "shovelman")),
            ["mining"] = Map(
                ("crystalseeker", "shiny-hunter"),
                ("stonebreaker", "rock-hoarder"),
                ("carefulminer", "pickaxe-care"),
                ("gemstoneminer", "glittering-swings"),
                ("stonecutter", "quarrying-strikes"),
                ("oreminer", "abundant-ores")),
            ["panning"] = Map(("golddigger", "very-patient")),
            ["clayforming"] = Map(
                ("carefulhand", "claywright"),
                ("steadyhand", "claymation")),
            ["tailoring"] = Map(
                ("summerweaver", "cool-garments"),
                ("winterweaver", "warm-garments"),
                ("clothweaver", "textile-laborer")),
            ["metalworking"] = Map(
                ("bloomeryexpert", "bloom-brigand"),
                ("heated-strikes", "hammer-cadence"),
                ("metal-recovery", "thrifty-smithing"),
                ("hammer-expert", "hammer-care"),
                ("bits-forging", "bit-parts"),
                ("heavyhits", "slag-smasher"))
        };

    public static bool TryMap(string skillId, string nodeId, out string mapped)
    {
        mapped = nodeId;
        if (string.IsNullOrWhiteSpace(skillId)
            || string.IsNullOrWhiteSpace(nodeId)
            || !BySkill.TryGetValue(skillId, out Dictionary<string, string>? map)
            || !map.TryGetValue(nodeId, out string? next)
            || string.Equals(next, nodeId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        mapped = next;
        return true;
    }

    static Dictionary<string, string> Map(params (string Old, string New)[] pairs)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string oldId, string newId) in pairs)
        {
            map[oldId] = newId;
        }

        return map;
    }
}
