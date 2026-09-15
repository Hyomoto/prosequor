namespace Prosequor.Ability;

/// <summary>
/// Narrow ThreadStatic window around craft ingredient consume (tool durability).
/// </summary>
public static class CraftConsumeScope
{
    [ThreadStatic]
    static int depth;

    public static bool IsActive => depth > 0;

    public static void Push() => depth++;

    public static void Pop()
    {
        depth--;
        if (depth < 0)
        {
            depth = 0;
        }
    }
}
