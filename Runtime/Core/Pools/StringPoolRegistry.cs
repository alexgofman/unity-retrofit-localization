using System;
using System.Collections.Generic;

namespace RetrofitLocalization
{
    /// <summary>
    /// Supplies the text a pool should hold in the active locale. Returns <paramref name="source"/>
    /// itself when the pool stays in the source language.
    /// </summary>
    public delegate string[] PoolResolver(string table, string key, string[] source);

    /// <summary>
    /// Keeps track of every string pool that was handed out, so a language change can rewrite the
    /// pools in place.
    ///
    /// The problem this solves: an older code base keeps its prose in
    /// <c>static readonly string[]</c> fields. Each field is initialised once, the first time its
    /// owning type is touched, and from then on call sites all over the project hold that exact
    /// array. A later language change cannot give those fields a new array.
    ///
    /// But <c>readonly</c> protects the reference, not the contents: the elements of an array and
    /// the items of a list stay writable. So the registry remembers each pool object together with
    /// a private copy of its source-language text, and a refresh overwrites the contents. No
    /// reflection, no call-site changes, and every caller keeps the object it already cached.
    ///
    /// Registration is safe from any thread, because a static initializer runs wherever its type is
    /// first touched, which a loading thread can be. Refreshing is meant for the main thread; it holds
    /// the registry lock while the resolver runs.
    /// </summary>
    public sealed class StringPoolRegistry
    {
        private sealed class Entry
        {
            public string Table;
            public string Key;

            // A copy of the authored text, never an alias of the live pool. The live pool is
            // overwritten on every language change, so an alias would lose the source text on the
            // first switch and a later switch back to the source language could not restore it.
            public string[] Source;

            // Weak, so a pool whose owner is gone can be collected instead of being pinned here.
            public WeakReference Live;
        }

        // Bucketed by table and key, so finding out whether an object is already registered scans
        // the one or two entries that share its key, never the whole registry.
        private readonly Dictionary<(string Table, string Key), List<Entry>> _buckets =
            new Dictionary<(string Table, string Key), List<Entry>>();

        private readonly object _gate = new object();

        /// <summary>Number of pools that are still alive. Drops entries whose pool was collected.</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    int count = 0;
                    foreach (List<Entry> bucket in _buckets.Values)
                    {
                        for (int i = bucket.Count - 1; i >= 0; i--)
                        {
                            if (bucket[i].Live.Target == null) bucket.RemoveAt(i);
                            else count++;
                        }
                    }

                    return count;
                }
            }
        }

        /// <summary>
        /// Starts tracking <paramref name="pool"/> and returns the copy of its source text.
        /// Registering the same object again is harmless and returns the copy made the first time,
        /// which matters because by then the pool may already hold translated text.
        /// </summary>
        public string[] Track(string table, string key, string[] pool)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            lock (_gate)
            {
                Entry entry = FindOrAdd(table, key, pool);
                return entry.Source ?? (entry.Source = (string[])pool.Clone());
            }
        }

        /// <summary>List-shaped twin of <see cref="Track(string,string,string[])"/>.</summary>
        public string[] Track(string table, string key, List<string> pool)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            lock (_gate)
            {
                Entry entry = FindOrAdd(table, key, pool);
                return entry.Source ?? (entry.Source = pool.ToArray());
            }
        }

        /// <summary>
        /// Rewrites every live pool with the text <paramref name="resolver"/> returns for it. Called
        /// once per language change, never per frame.
        /// </summary>
        public void Refresh(PoolResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            lock (_gate)
            {
                foreach (List<Entry> bucket in _buckets.Values)
                {
                    for (int i = bucket.Count - 1; i >= 0; i--)
                    {
                        Entry entry = bucket[i];
                        object live = entry.Live.Target;
                        if (live == null)
                        {
                            bucket.RemoveAt(i);
                            continue;
                        }

                        Write(live, resolver(entry.Table, entry.Key, entry.Source) ?? entry.Source);
                    }
                }
            }
        }

        /// <summary>
        /// Calls <paramref name="visitor"/> with the table, key and source text of every live pool.
        /// The same table and key can be reported more than once when several objects share them.
        /// </summary>
        public void ForEach(Action<string, string, string[]> visitor)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            lock (_gate)
            {
                foreach (List<Entry> bucket in _buckets.Values)
                {
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        Entry entry = bucket[i];
                        if (entry.Live.Target != null) visitor(entry.Table, entry.Key, entry.Source);
                    }
                }
            }
        }

        /// <summary>Forgets every pool. The pools themselves keep whatever text they hold.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _buckets.Clear();
            }
        }

        /// <summary>
        /// Overwrites the contents of a pool object. An array keeps its length (a resolver never
        /// returns a different one); a list is reset to exactly the given items, which also undoes
        /// any run-time change to it. Pools are authored content, not scratch state.
        /// </summary>
        public static void Write(object pool, string[] contents)
        {
            if (pool == null || contents == null) return;

            if (pool is string[] array)
            {
                if (ReferenceEquals(array, contents) || array.Length != contents.Length) return;
                Array.Copy(contents, array, array.Length);
                return;
            }

            if (pool is List<string> list)
            {
                list.Clear();
                list.AddRange(contents);
            }
        }

        // Callers hold the registry lock.
        private Entry FindOrAdd(string table, string key, object pool)
        {
            var bucketKey = (table, key);
            if (!_buckets.TryGetValue(bucketKey, out List<Entry> bucket))
            {
                bucket = new List<Entry>(1);
                _buckets.Add(bucketKey, bucket);
            }

            for (int i = bucket.Count - 1; i >= 0; i--)
            {
                object existing = bucket[i].Live.Target;
                if (existing == null)
                {
                    bucket.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(existing, pool)) return bucket[i];
            }

            var entry = new Entry { Table = table, Key = key, Live = new WeakReference(pool) };
            bucket.Add(entry);
            return entry;
        }
    }
}
