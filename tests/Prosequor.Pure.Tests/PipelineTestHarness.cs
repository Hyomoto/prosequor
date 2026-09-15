using Prosequor.Data;

namespace Prosequor.Pure.Tests;

/// <summary>Shared skill registry stub for pipeline mechanism tests.</summary>
public sealed class PipelineSkillRegistry : ISkillRegistry
{
    readonly List<SkillDef> all = new();

    public IReadOnlyList<SkillDef> All => all;

    public SkillMenuIndex MenuIndex => SkillMenuIndex.Build(all);

    public void Register(SkillDef def) => all.Add(def);

    public bool TryGet(string id, out SkillDef def)
    {
        def = all.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))!;
        return def != null;
    }

    public bool TryResolve(string idOrDisplayName, string? languageCode, out SkillDef def) =>
        TryGet(idOrDisplayName, out def);
}
