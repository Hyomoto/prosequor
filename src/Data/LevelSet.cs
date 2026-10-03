using System.Globalization;

namespace Prosequor.Data;

/// <summary>One arithmetic run of grant levels, from <see cref="Start"/> through <see cref="End"/> inclusive.</summary>
public readonly struct LevelRun
{
    public LevelRun(int start, int end, int step)
    {
        Start = start;
        End = end;
        Step = step;
    }

    public int Start { get; }
    public int End { get; }
    public int Step { get; }

    public int GrantCount => Step < 1 || End < Start ? 0 : ((End - Start) / Step) + 1;
}

/// <summary>
/// Grant levels stored as merged runs (<c>1..50</c>, <c>20..100^20</c>).
/// An open end uses <c>openEnd</c>. A missing side copies the neighboring run's bound.
/// </summary>
public readonly struct LevelSet : IEquatable<LevelSet>
{
    public const int MaxSpan = 10000;

    readonly LevelRun[]? runs;
    readonly int count;

    LevelSet(LevelRun[] runs, int count)
    {
        this.runs = runs;
        this.count = count;
    }

    public static LevelSet Empty { get; } = new(Array.Empty<LevelRun>(), 0);

    public int Count => count;

    LevelRun[] Items => runs ?? Array.Empty<LevelRun>();

    public static LevelSet Parse(string? text, int openEnd)
    {
        if (!TryParse(text, openEnd, out LevelSet set, out string? error))
        {
            throw new FormatException(error);
        }

        return set;
    }

    /// <summary>
    /// Empty or whitespace is an empty set. Commas split pieces. Each piece is a level
    /// or a range <c>X..Y</c>, <c>..Y</c>, <c>X..</c>, <c>..</c>, with an optional <c>^Z</c> step.
    /// </summary>
    public static bool TryParse(string? text, int openEnd, out LevelSet set, out string? error)
    {
        set = Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        string[] tokens = text.Split(',');
        Piece[] pieces = new Piece[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            if (!TryParsePiece(tokens[i], out pieces[i], out error))
            {
                return false;
            }
        }

        if (!TryFillBounds(pieces, openEnd, out error))
        {
            return false;
        }

        List<LevelRun> concrete = new(pieces.Length);
        for (int i = 0; i < pieces.Length; i++)
        {
            if (!TrySnap(pieces[i], out LevelRun run, out error))
            {
                return false;
            }

            concrete.Add(run);
        }

        return TryNormalize(concrete, out set, out error);
    }

    public static LevelSet FromRuns(IEnumerable<LevelRun>? source)
    {
        if (source == null)
        {
            return Empty;
        }

        List<LevelRun> concrete = new();
        foreach (LevelRun run in source)
        {
            if (run.Step < 1 || run.Start < 0 || run.End < run.Start)
            {
                continue;
            }

            int last = run.Start + ((run.End - run.Start) / run.Step) * run.Step;
            int step = last == run.Start ? 1 : run.Step;
            concrete.Add(new LevelRun(run.Start, last, step));
        }

        if (!TryNormalize(concrete, out LevelSet set, out _))
        {
            return Empty;
        }

        return set;
    }

    public LevelRun[] ToRuns()
    {
        LevelRun[] items = Items;
        LevelRun[] copy = new LevelRun[items.Length];
        Array.Copy(items, copy, items.Length);
        return copy;
    }

    /// <summary>Canonical list text: <c>20</c>, <c>1..50</c>, <c>20..100^20</c>, joined by commas.</summary>
    public string Format()
    {
        LevelRun[] items = Items;
        if (items.Length == 0)
        {
            return "";
        }

        string[] parts = new string[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            LevelRun run = items[i];
            if (run.GrantCount <= 1)
            {
                parts[i] = run.Start.ToString(CultureInfo.InvariantCulture);
                continue;
            }

            string span = run.Start.ToString(CultureInfo.InvariantCulture)
                + ".."
                + run.End.ToString(CultureInfo.InvariantCulture);
            parts[i] = run.Step <= 1
                ? span
                : span + "^" + run.Step.ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(",", parts);
    }

    /// <summary>How many listed levels sit in (before, after].</summary>
    public int CountCrossed(int before, int after)
    {
        if (after <= before)
        {
            return 0;
        }

        return CountAtMost(after) - CountAtMost(before);
    }

    /// <summary>How many listed levels are &lt;= <paramref name="level"/>.</summary>
    public int CountAtMost(int level)
    {
        int total = 0;
        LevelRun[] items = Items;
        for (int i = 0; i < items.Length; i++)
        {
            LevelRun run = items[i];
            if (level < run.Start)
            {
                continue;
            }

            int last = level >= run.End
                ? run.End
                : run.Start + ((level - run.Start) / run.Step) * run.Step;
            total += ((last - run.Start) / run.Step) + 1;
        }

        return total;
    }

    public bool Equals(LevelSet other)
    {
        if (count != other.count)
        {
            return false;
        }

        LevelRun[] left = Items;
        LevelRun[] right = other.Items;
        if (left.Length == right.Length)
        {
            bool sameShape = true;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i].Start != right[i].Start
                    || left[i].End != right[i].End
                    || left[i].Step != right[i].Step)
                {
                    sameShape = false;
                    break;
                }
            }

            if (sameShape)
            {
                return true;
            }
        }

        if (count == 0)
        {
            return true;
        }

        return SameGrants(left, right);
    }

    public override bool Equals(object? obj) => obj is LevelSet other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(count);
        LevelRun[] items = Items;
        int[] heads = Heads(items);
        for (int n = 0; n < count; n++)
        {
            int index = MinHead(heads);
            hash.Add(heads[index]);
            Advance(items, heads, index);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(LevelSet left, LevelSet right) => left.Equals(right);

    public static bool operator !=(LevelSet left, LevelSet right) => !left.Equals(right);

    static bool TryParsePiece(string raw, out Piece piece, out string? error)
    {
        piece = default;
        error = null;
        string token = raw.Trim();
        piece.Token = token;
        if (token.Length == 0)
        {
            error = "level list has an empty entry.";
            return false;
        }

        int dots = token.IndexOf("..", StringComparison.Ordinal);
        if (dots < 0)
        {
            if (token.Contains('^'))
            {
                error = $"level '{token}' is not a whole number.";
                return false;
            }

            if (!TryReadLevel(token, out int one))
            {
                error = $"level '{token}' is not a whole number.";
                return false;
            }

            piece.Start = one;
            piece.End = one;
            piece.Step = 1;
            piece.HasStart = true;
            piece.HasEnd = true;
            return true;
        }

        if (token.Contains("...", StringComparison.Ordinal)
            || token.IndexOf("..", dots + 2, StringComparison.Ordinal) >= 0)
        {
            error = $"level range '{token}' is not a valid inclusive range.";
            return false;
        }

        string left = token[..dots].Trim();
        string right = token[(dots + 2)..].Trim();
        if (left.Contains('^'))
        {
            error = $"level range '{token}' is not a valid inclusive range.";
            return false;
        }

        if (!TryReadStep(right, out string endText, out int step))
        {
            error = $"level range '{token}' is not a valid inclusive range.";
            return false;
        }

        piece.Step = step;
        if (left.Length > 0)
        {
            if (!TryReadLevel(left, out int start))
            {
                error = $"level range '{token}' is not a valid inclusive range.";
                return false;
            }

            piece.Start = start;
            piece.HasStart = true;
        }

        if (endText.Length > 0)
        {
            if (!TryReadLevel(endText, out int end))
            {
                error = $"level range '{token}' is not a valid inclusive range.";
                return false;
            }

            piece.End = end;
            piece.HasEnd = true;
        }

        if (piece.HasStart && piece.HasEnd && piece.Start > piece.End)
        {
            error = $"level range '{token}' runs backwards.";
            return false;
        }

        return true;
    }

    static bool TryFillBounds(Piece[] pieces, int openEnd, out string? error)
    {
        error = null;
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < pieces.Length; i++)
            {
                if (!pieces[i].HasStart)
                {
                    if (i == 0)
                    {
                        pieces[i].Start = 1;
                        pieces[i].HasStart = true;
                        changed = true;
                    }
                    else if (pieces[i - 1].HasEnd)
                    {
                        long next = (long)pieces[i - 1].End + 1;
                        if (next > int.MaxValue)
                        {
                            error = $"level range '{pieces[i].Token}' is not a valid inclusive range.";
                            return false;
                        }

                        pieces[i].Start = (int)next;
                        pieces[i].HasStart = true;
                        changed = true;
                    }
                }

                if (!pieces[i].HasEnd)
                {
                    if (i == pieces.Length - 1)
                    {
                        pieces[i].End = openEnd;
                        pieces[i].HasEnd = true;
                        changed = true;
                    }
                    else if (pieces[i + 1].HasStart)
                    {
                        pieces[i].End = pieces[i + 1].Start - 1;
                        pieces[i].HasEnd = true;
                        changed = true;
                    }
                }
            }
        }

        for (int i = 0; i < pieces.Length; i++)
        {
            if (!pieces[i].HasStart || !pieces[i].HasEnd)
            {
                error = $"level range '{pieces[i].Token}' has no bound to meet.";
                return false;
            }

            if (pieces[i].Start > pieces[i].End)
            {
                error = $"level range '{pieces[i].Token}' runs backwards.";
                return false;
            }
        }

        return true;
    }

    static bool TrySnap(Piece piece, out LevelRun run, out string? error)
    {
        long steps = ((long)piece.End - piece.Start) / piece.Step;
        long grants = steps + 1;
        if (grants > MaxSpan)
        {
            error = $"level range '{piece.Token}' is wider than {MaxSpan}.";
            run = default;
            return false;
        }

        int last = (int)(piece.Start + (steps * piece.Step));
        int step = grants == 1 ? 1 : piece.Step;
        run = new LevelRun(piece.Start, last, step);
        error = null;
        return true;
    }

    static bool TryNormalize(List<LevelRun> concrete, out LevelSet set, out string? error)
    {
        set = Empty;
        error = null;
        List<LevelRun> step1 = new();
        List<LevelRun> stepped = new();
        for (int i = 0; i < concrete.Count; i++)
        {
            LevelRun run = concrete[i];
            if (run.Step <= 1)
            {
                step1.Add(new LevelRun(run.Start, run.End, 1));
            }
            else
            {
                stepped.Add(run);
            }
        }

        int guard = stepped.Count + concrete.Count + 2;
        while (guard-- > 0)
        {
            MergeStepOne(step1);
            List<LevelRun> nextStepped = new();
            List<LevelRun> extra = new();
            for (int i = 0; i < stepped.Count; i++)
            {
                ClipStepped(stepped[i], step1, nextStepped, extra);
            }

            stepped = nextStepped;
            if (extra.Count == 0)
            {
                break;
            }

            step1.AddRange(extra);
        }

        MergeStepOne(step1);
        List<LevelRun> phased = MergePhases(stepped);
        for (int i = 0; i < phased.Count; i++)
        {
            for (int j = i + 1; j < phased.Count; j++)
            {
                if (SharesLevel(phased[i], phased[j]))
                {
                    error = "level ranges overlap; each level must come from one range.";
                    return false;
                }
            }
        }

        List<LevelRun> all = new(step1.Count + phased.Count);
        all.AddRange(step1);
        all.AddRange(phased);
        all.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        long total = 0;
        for (int i = 0; i < all.Count; i++)
        {
            total += all[i].GrantCount;
        }

        if (total > MaxSpan)
        {
            error = $"level list is wider than {MaxSpan}.";
            return false;
        }

        set = new LevelSet(all.ToArray(), (int)total);
        return true;
    }

    static void MergeStepOne(List<LevelRun> runs)
    {
        if (runs.Count <= 1)
        {
            return;
        }

        runs.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        List<LevelRun> merged = new();
        LevelRun current = runs[0];
        for (int i = 1; i < runs.Count; i++)
        {
            LevelRun next = runs[i];
            if ((long)next.Start <= (long)current.End + 1)
            {
                int end = Math.Max(current.End, next.End);
                current = new LevelRun(current.Start, end, 1);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        runs.Clear();
        runs.AddRange(merged);
    }

    static void ClipStepped(LevelRun run, List<LevelRun> blocks, List<LevelRun> steppedOut, List<LevelRun> step1Out)
    {
        long gapLo = run.Start;
        for (int i = 0; i < blocks.Count; i++)
        {
            LevelRun block = blocks[i];
            if (block.End < run.Start)
            {
                continue;
            }

            if (block.Start > run.End)
            {
                break;
            }

            long blockedLo = Math.Max(block.Start, run.Start);
            EmitLattice(run, gapLo, blockedLo - 1, steppedOut, step1Out);
            gapLo = (long)Math.Min(block.End, run.End) + 1;
        }

        EmitLattice(run, gapLo, run.End, steppedOut, step1Out);
    }

    static void EmitLattice(LevelRun run, long gapLo, long gapHi, List<LevelRun> steppedOut, List<LevelRun> step1Out)
    {
        if (gapLo > gapHi)
        {
            return;
        }

        long first = FirstOnOrAfter(run.Start, run.Step, gapLo);
        if (first > gapHi || first > run.End)
        {
            return;
        }

        long limit = Math.Min(gapHi, run.End);
        long last = run.Start + ((limit - run.Start) / run.Step) * run.Step;
        if (last < first)
        {
            return;
        }

        int start = (int)first;
        int end = (int)last;
        long grants = ((last - first) / run.Step) + 1;
        if (grants <= 1)
        {
            step1Out.Add(new LevelRun(start, start, 1));
            return;
        }

        steppedOut.Add(new LevelRun(start, end, run.Step));
    }

    static long FirstOnOrAfter(int origin, int step, long gapLo)
    {
        if (gapLo <= origin)
        {
            return origin;
        }

        long delta = gapLo - origin;
        long k = (delta + step - 1) / step;
        return origin + (k * step);
    }

    static List<LevelRun> MergePhases(List<LevelRun> stepped)
    {
        Dictionary<(int step, int phase), List<LevelRun>> groups = new();
        for (int i = 0; i < stepped.Count; i++)
        {
            LevelRun run = stepped[i];
            (int, int) key = (run.Step, run.Start % run.Step);
            if (!groups.TryGetValue(key, out List<LevelRun>? group))
            {
                group = new List<LevelRun>();
                groups[key] = group;
            }

            group.Add(run);
        }

        List<LevelRun> result = new();
        foreach (List<LevelRun> group in groups.Values)
        {
            group.Sort(static (a, b) => a.Start.CompareTo(b.Start));
            LevelRun current = group[0];
            for (int i = 1; i < group.Count; i++)
            {
                LevelRun next = group[i];
                if ((long)next.Start <= (long)current.End + current.Step)
                {
                    int end = Math.Max(current.End, next.End);
                    current = new LevelRun(current.Start, end, current.Step);
                }
                else
                {
                    result.Add(current);
                    current = next;
                }
            }

            result.Add(current);
        }

        return result;
    }

    static bool SharesLevel(LevelRun left, LevelRun right)
    {
        if (left.GrantCount <= right.GrantCount)
        {
            return RunHits(left, right);
        }

        return RunHits(right, left);
    }

    static bool RunHits(LevelRun needle, LevelRun hay)
    {
        if (needle.End < hay.Start || hay.End < needle.Start)
        {
            return false;
        }

        for (long level = needle.Start; level <= needle.End; level += needle.Step)
        {
            if (level >= hay.Start && level <= hay.End && (level - hay.Start) % hay.Step == 0)
            {
                return true;
            }
        }

        return false;
    }

    static bool SameGrants(LevelRun[] left, LevelRun[] right)
    {
        int[] leftHeads = Heads(left);
        int[] rightHeads = Heads(right);
        int remaining = 0;
        for (int i = 0; i < left.Length; i++)
        {
            remaining += left[i].GrantCount;
        }

        for (int n = 0; n < remaining; n++)
        {
            int leftIndex = MinHead(leftHeads);
            int rightIndex = MinHead(rightHeads);
            if (leftHeads[leftIndex] != rightHeads[rightIndex])
            {
                return false;
            }

            Advance(left, leftHeads, leftIndex);
            Advance(right, rightHeads, rightIndex);
        }

        return true;
    }

    static int[] Heads(LevelRun[] items)
    {
        int[] heads = new int[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            heads[i] = items[i].Start;
        }

        return heads;
    }

    static int MinHead(int[] heads)
    {
        int best = 0;
        for (int i = 1; i < heads.Length; i++)
        {
            if (heads[i] < heads[best])
            {
                best = i;
            }
        }

        return best;
    }

    static void Advance(LevelRun[] items, int[] heads, int index)
    {
        long next = (long)heads[index] + items[index].Step;
        heads[index] = next <= items[index].End ? (int)next : int.MaxValue;
    }

    static bool TryReadLevel(string text, out int level)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out level) || level < 0)
        {
            level = 0;
            return false;
        }

        return true;
    }

    static bool TryReadStep(string right, out string endText, out int step)
    {
        step = 1;
        endText = right;
        int caret = right.IndexOf('^');
        if (caret < 0)
        {
            return true;
        }

        if (right.IndexOf('^', caret + 1) >= 0)
        {
            return false;
        }

        endText = right[..caret].Trim();
        string stepText = right[(caret + 1)..].Trim();
        return int.TryParse(stepText, NumberStyles.Integer, CultureInfo.InvariantCulture, out step) && step >= 1;
    }

    struct Piece
    {
        public string Token;
        public int Start;
        public int End;
        public int Step;
        public bool HasStart;
        public bool HasEnd;
    }
}
