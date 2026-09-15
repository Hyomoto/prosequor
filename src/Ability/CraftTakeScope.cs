namespace Prosequor.Ability;

/// <summary>
/// Tracks a craft-output take so multi-craft (<c>CraftMany</c>) completions can be counted.
/// Vanilla resets <c>op.MovedQuantity</c> each loop and never restores the total, so
/// <c>TryPutInto</c>'s return value is only the last craft's moved count.
/// </summary>
public static class CraftTakeScope
{
    [ThreadStatic]
    static int depth;

    [ThreadStatic]
    static int consumeCount;

    public static bool IsActive => depth > 0;

    public static int ConsumeCount => consumeCount;

    public static void Begin()
    {
        depth++;
        if (depth == 1)
        {
            consumeCount = 0;
        }
    }

    public static void NoteConsume()
    {
        if (depth > 0)
        {
            consumeCount++;
        }
    }

    public static void End()
    {
        depth--;
        if (depth <= 0)
        {
            depth = 0;
            consumeCount = 0;
        }
    }
}
