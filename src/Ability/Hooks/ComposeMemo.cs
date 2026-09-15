namespace Prosequor.Ability.Hooks;

/// <summary>
/// Per-player memo of stacked scalar pipeline results keyed by hook, verb, phase, fact, and base.
/// </summary>
public sealed class ComposeMemo
{
    const int MaxEntriesPerPhase = 8;

    readonly Dictionary<(HookId Hook, VerbId Verb, PhaseId Phase), PhaseBucket> buckets = new();
    int progressRevision;

    public int ProgressRevision => progressRevision;
    public int HitCount { get; private set; }
    public int MissCount { get; private set; }

    public void BumpProgressRevision()
    {
        progressRevision++;
        buckets.Clear();
    }

    /// <summary>Drops memo entries for one hook/verb/phase without clearing other addresses.</summary>
    public void InvalidatePhase(HookId hook, VerbId verb, PhaseId phase) =>
        buckets.Remove((hook, verb, phase));

    public bool TryGet<T>(
        HookId hook,
        VerbId verb,
        PhaseId phase,
        FactFingerprint fingerprint,
        long baseBits,
        out T value)
    {
        value = default!;
        if (!buckets.TryGetValue((hook, verb, phase), out PhaseBucket? bucket))
        {
            return false;
        }

        if (!bucket.TryGet(progressRevision, fingerprint, baseBits, out object? boxed))
        {
            return false;
        }

        if (boxed is not T typed)
        {
            return false;
        }

        HitCount++;
        value = typed;
        return true;
    }

    public void Store<T>(
        HookId hook,
        VerbId verb,
        PhaseId phase,
        FactFingerprint fingerprint,
        long baseBits,
        T value)
    {
        MissCount++;
        if (!buckets.TryGetValue((hook, verb, phase), out PhaseBucket? bucket))
        {
            bucket = new PhaseBucket();
            buckets[(hook, verb, phase)] = bucket;
        }

        bucket.Store(progressRevision, fingerprint, baseBits, value!);
    }

    sealed class PhaseBucket
    {
        readonly LinkedList<Entry> lru = new();
        readonly Dictionary<(int Revision, FactFingerprint Fingerprint, long BaseBits), LinkedListNode<Entry>> index =
            new();

        public bool TryGet(int revision, FactFingerprint fingerprint, long baseBits, out object? value)
        {
            if (!index.TryGetValue((revision, fingerprint, baseBits), out LinkedListNode<Entry>? node))
            {
                value = null;
                return false;
            }

            lru.Remove(node);
            lru.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        public void Store(int revision, FactFingerprint fingerprint, long baseBits, object value)
        {
            (int Revision, FactFingerprint Fingerprint, long BaseBits) key = (revision, fingerprint, baseBits);
            if (index.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                existing.Value.Value = value;
                lru.Remove(existing);
                lru.AddFirst(existing);
                return;
            }

            while (lru.Count >= MaxEntriesPerPhase)
            {
                LinkedListNode<Entry>? last = lru.Last;
                if (last == null)
                {
                    break;
                }

                Entry evicted = last.Value;
                index.Remove((evicted.Revision, evicted.Fingerprint, evicted.BaseBits));
                lru.RemoveLast();
            }

            Entry entry = new(revision, fingerprint, baseBits, value);
            LinkedListNode<Entry> node = lru.AddFirst(entry);
            index[key] = node;
        }

        sealed class Entry
        {
            public Entry(int revision, FactFingerprint fingerprint, long baseBits, object value)
            {
                Revision = revision;
                Fingerprint = fingerprint;
                BaseBits = baseBits;
                Value = value;
            }

            public int Revision { get; }
            public FactFingerprint Fingerprint { get; }
            public long BaseBits { get; }
            public object Value { get; set; }
        }
    }
}

/// <summary>Packs phase base seed into memo key bits.</summary>
public static class ComposeMemoBaseBits
{
    public static long From(float baseValue) => BitConverter.SingleToInt32Bits(baseValue);

    public static long From(int baseValue) => baseValue;
}
