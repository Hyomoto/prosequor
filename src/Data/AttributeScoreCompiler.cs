namespace Prosequor.Data;

/// <summary>Compiles skill-root <c>attributeScores</c> into runtime bucket-fill entries.</summary>
public static class AttributeScoreCompiler
{
    /// <summary>
    /// Validates and canonicalizes <paramref name="rows"/>. Invalid entries are skipped and
    /// reported via <paramref name="errors"/>. Duplicate canonical ids last-win (with an error).
    /// </summary>
    public static IReadOnlyList<AttributeScoreEntry> Compile(
        string skillId,
        AttributeScoreJson[]? rows,
        List<string> errors)
    {
        if (rows == null || rows.Length == 0)
        {
            return Array.Empty<AttributeScoreEntry>();
        }

        Dictionary<string, AttributeScoreEntry> byId = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rows.Length; i++)
        {
            AttributeScoreJson? row = rows[i];
            if (row == null)
            {
                errors.Add(
                    $"[prosequor] Skill '{skillId}' attributeScores[{i}]: entry is null.");
                continue;
            }

            string? canonical = AttributeIds.Canonicalize(row.id);
            if (canonical == null)
            {
                errors.Add(
                    $"[prosequor] Skill '{skillId}' attributeScores[{i}]: unknown attribute id '{row.id}'.");
                continue;
            }

            if (!float.IsFinite(row.value) || row.value <= 0f)
            {
                errors.Add(
                    $"[prosequor] Skill '{skillId}' attributeScores[{i}] '{canonical}': value must be a finite number > 0 (got {row.value}).");
                continue;
            }

            if (byId.ContainsKey(canonical))
            {
                errors.Add(
                    $"[prosequor] Skill '{skillId}' attributeScores[{i}]: duplicate attribute id '{canonical}' (last entry wins).");
            }

            byId[canonical] = new AttributeScoreEntry(canonical, row.value);
        }

        if (byId.Count == 0)
        {
            return Array.Empty<AttributeScoreEntry>();
        }

        List<AttributeScoreEntry> compiled = new(byId.Count);
        foreach (string id in AttributeIds.All)
        {
            if (byId.TryGetValue(id, out AttributeScoreEntry entry))
            {
                compiled.Add(entry);
            }
        }

        return compiled;
    }
}
