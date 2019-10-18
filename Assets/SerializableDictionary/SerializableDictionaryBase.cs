using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Codec.SerializableDictionary
{
    [Serializable]
    public class SerializableDictionaryBase<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary
    {
        //private struct Entry
        //{
        //    public int hashCode;    // Lower 31 bits of hash code, -1 if unused
        //    public int next;        // Index of next entry, -1 if last
        //    public TKey key;           // Key of entry
        //    public TValue value;         // Value of entry
        //}


        [SerializeField, HideInInspector] private int[] buckets;  // link index of first entry. empty is -1.
        [SerializeField, HideInInspector] private int count;

        //private Entry[] entries;
        [SerializeField, HideInInspector] private int[] entriesHashCode;    // Lower 31 bits of hash code, -1 if unused
        [SerializeField, HideInInspector] private int[] entriesNext;        // Index of next entry, -1 if last
        [SerializeField, HideInInspector] private TKey[] entriesKey;        // Key of entry
        [SerializeField, HideInInspector] private TValue[] entriesValue;    // Value of entry

        private int version;    // version does not serialize

        // when remove, mark slot to free
        [SerializeField, HideInInspector] private int freeList;
        [SerializeField, HideInInspector] private int freeCount;

        private KeyCollection keys;
        private ValueCollection values;
        private object _syncRoot;

        protected SerializableDictionaryBase()
        {
            Initialize(0);
        }

        protected SerializableDictionaryBase(int capacity)
        {
            Initialize(capacity);
        }

        private SerializableDictionaryBase(int staticCapacity, bool forceSize)
        {
            Initialize(staticCapacity, forceSize);
        }

        protected SerializableDictionaryBase(IDictionary<TKey, TValue> dictionary)
        {
            if (dictionary == null)
            {
                throw new ArgumentNullException("dictionary");
            }

            foreach (var pair in dictionary)
            {
                Add(pair.Key, pair.Value);
            }
        }

        public IEqualityComparer<TKey> Comparer => EqualityComparer<TKey>.Default;

        public int Count => count - freeCount;

        public KeyCollection Keys
        {
            get
            {
                if (keys == null)
                {
                    keys = new KeyCollection(this);
                }

                return keys;
            }
        }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys
        {
            get
            {
                if (keys == null)
                {
                    keys = new KeyCollection(this);
                }

                return keys;
            }
        }

        public ValueCollection Values
        {
            get
            {
                if (values == null)
                {
                    values = new ValueCollection(this);
                }

                return values;
            }
        }

        ICollection<TValue> IDictionary<TKey, TValue>.Values
        {
            get
            {
                if (values == null)
                {
                    values = new ValueCollection(this);
                }

                return values;
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                var i = FindEntry(key);
                if (i >= 0)
                {
                    return entriesValue[i];
                }

                throw new KeyNotFoundException();
            }
            set => Insert(key, value, false);
        }

        public void Add(TKey key, TValue value)
        {
            Insert(key, value, true);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> keyValuePair)
        {
            Add(keyValuePair.Key, keyValuePair.Value);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> keyValuePair)
        {
            var i = FindEntry(keyValuePair.Key);
            if (i >= 0 && EqualityComparer<TValue>.Default.Equals(entriesValue[i], keyValuePair.Value))
            {
                return true;
            }
            return false;
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
        {
            var i = FindEntry(keyValuePair.Key);
            if (i >= 0 && EqualityComparer<TValue>.Default.Equals(entriesValue[i], keyValuePair.Value))
            {
                Remove(keyValuePair.Key);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            if (count > 0)
            {
                for (var i = 0; i < buckets.Length; i++)
                {
                    buckets[i] = -1;
                }
                //Array.Clear(entries, 0, count);

                Array.Clear(entriesHashCode, 0, count);
                Array.Clear(entriesKey, 0, count);
                Array.Clear(entriesNext, 0, count);
                Array.Clear(entriesValue, 0, count);

                freeList = -1;
                count = 0;
                freeCount = 0;
                version++;
            }
        }

        public bool ContainsKey(TKey key)
        {
            return FindEntry(key) >= 0;
        }

        public bool ContainsValue(TValue value)
        {
            if (value == null)
            {
                for (var i = 0; i < count; i++)
                {
                    if (entriesHashCode[i] >= 0 && entriesValue[i] == null)
                    {
                        return true;
                    }
                }
            }
            else
            {
                var c = EqualityComparer<TValue>.Default;
                for (var i = 0; i < count; i++)
                {
                    if (entriesHashCode[i] >= 0 && c.Equals(entriesValue[i], value))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }

            if (index < 0 || index > array.Length)
            {
                throw new ArgumentOutOfRangeException("index", index, "ArgumentOutOfRange_NeedNonNegNum");
            }

            if (array.Length - index < Count)
            {
                throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
            }

            var count = this.count;

            var entriesHashCode = this.entriesHashCode;
            var entriesNext = this.entriesNext;
            var entriesKey = this.entriesKey;
            var entriesValue = this.entriesValue;

            for (var i = 0; i < count; i++)
            {
                if (entriesHashCode[i] >= 0)
                {
                    array[index++] = new KeyValuePair<TKey, TValue>(entriesKey[i], entriesValue[i]);
                }
            }
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this, Enumerator.KeyValuePair);
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return new Enumerator(this, Enumerator.KeyValuePair);
        }

        private int FindEntry(TKey key)
        {
            if (key == null)
            {
                throw new ArgumentNullException("key");
            }

            if (buckets != null)
            {
                var hashCode = Comparer.GetHashCode(key) & 0x7FFFFFFF;
                for (var i = buckets[hashCode % buckets.Length]; i >= 0; i = entriesNext[i])
                {
                    if (entriesHashCode[i] == hashCode && Comparer.Equals(entriesKey[i], key))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        private void Initialize(int capacity)
        {
            Initialize(capacity, false);
        }

        private void Initialize(int capacity, bool forceSize)
        {
            var size = forceSize ? capacity : HashHelpers.GetPrime(capacity);
            buckets = new int[size];
            for (var i = 0; i < buckets.Length; i++)
            {
                buckets[i] = -1;
            }

            entriesHashCode = new int[size];
            entriesNext = new int[size];
            entriesKey = new TKey[size];
            entriesValue = new TValue[size];

            freeList = -1;
        }

        private void Insert(TKey key, TValue value, bool add)
        {
            if (key == null)
            {
                throw new ArgumentNullException("key");
            }

            if (buckets == null || buckets.Length == 0)
            {
                Initialize(0);
            }

            var hashCode = Comparer.GetHashCode(key) & 0x7FFFFFFF;
            var targetBucket = hashCode % buckets.Length;

            for (var i = buckets[targetBucket]; i >= 0; i = entriesNext[i])
            {
                if (entriesHashCode[i] == hashCode && Comparer.Equals(entriesKey[i], key))
                {
                    if (add)
                    {
                        throw new ArgumentException("Argument_AddingDuplicate");
                    }
                    entriesValue[i] = value;
                    version++;
                    return;
                }
            }
            int index;
            if (freeCount > 0)
            {
                index = freeList;
                freeList = entriesNext[index];
                freeCount--;
            }
            else
            {
                if (count == entriesHashCode.Length)
                {
                    Resize();
                    targetBucket = hashCode % buckets.Length;
                }
                index = count;
                count++;
            }

            entriesHashCode[index] = hashCode;
            entriesNext[index] = buckets[targetBucket];
            entriesKey[index] = key;
            entriesValue[index] = value;
            buckets[targetBucket] = index;
            version++;
        }

        private void Resize()
        {
            Resize(HashHelpers.ExpandPrime(count), false);
        }

        private void Resize(int newSize, bool forceNewHashCodes)
        {
            var newBuckets = new int[newSize];
            for (var i = 0; i < newBuckets.Length; i++)
            {
                newBuckets[i] = -1;
            }

            var newEntriesHashCode = new int[newSize];
            var newEntriesNext = new int[newSize];
            var newEntriesKey = new TKey[newSize];
            var newEntriesValue = new TValue[newSize];
            Array.Copy(entriesHashCode, 0, newEntriesHashCode, 0, count);
            Array.Copy(entriesNext, 0, newEntriesNext, 0, count);
            Array.Copy(entriesKey, 0, newEntriesKey, 0, count);
            Array.Copy(entriesValue, 0, newEntriesValue, 0, count);

            if (forceNewHashCodes)
            {
                for (var i = 0; i < count; i++)
                {
                    if (newEntriesHashCode[i] != -1)
                    {
                        newEntriesHashCode[i] = (Comparer.GetHashCode(newEntriesKey[i]) & 0x7FFFFFFF);
                    }
                }
            }
            for (var i = 0; i < count; i++)
            {
                if (newEntriesHashCode[i] >= 0)
                {
                    var bucket = newEntriesHashCode[i] % newSize;
                    newEntriesNext[i] = newBuckets[bucket];
                    newBuckets[bucket] = i;
                }
            }
            buckets = newBuckets;

            entriesKey = newEntriesKey;
            entriesValue = newEntriesValue;
            entriesHashCode = newEntriesHashCode;
            entriesNext = newEntriesNext;
        }

        public bool Remove(TKey key)
        {
            if (key == null)
            {
                throw new ArgumentNullException("key");
            }

            if (buckets != null && buckets.Length > 0)
            {
                var hashCode = Comparer.GetHashCode(key) & 0x7FFFFFFF;
                var bucket = hashCode % buckets.Length;
                var last = -1;
                for (var i = buckets[bucket]; i >= 0; last = i, i = entriesNext[i])
                {
                    if (entriesHashCode[i] == hashCode && Comparer.Equals(entriesKey[i], key))
                    {
                        if (last < 0)
                        {
                            buckets[bucket] = entriesNext[i];
                        }
                        else
                        {
                            entriesNext[last] = entriesNext[i];
                        }
                        entriesHashCode[i] = -1;
                        entriesNext[i] = freeList;
                        entriesKey[i] = default(TKey);
                        entriesValue[i] = default(TValue);
                        freeList = i;
                        freeCount++;
                        version++;
                        return true;
                    }
                }
            }
            return false;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            var i = FindEntry(key);
            if (i >= 0)
            {
                value = entriesValue[i];
                return true;
            }
            value = default(TValue);
            return false;
        }

        public TValue GetValueOrDefault(TKey key)
        {
            var i = FindEntry(key);
            if (i >= 0)
            {
                return entriesValue[i];
            }
            return default(TValue);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            CopyTo(array, index);
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }

            if (array.Rank != 1)
            {
                throw new ArgumentException("Arg_RankMultiDimNotSupported");
            }

            if (array.GetLowerBound(0) != 0)
            {
                throw new ArgumentException("Arg_NonZeroLowerBound");
            }

            if (index < 0 || index > array.Length)
            {
                throw new ArgumentOutOfRangeException("index", "ArgumentOutOfRange_NeedNonNegNum");
            }

            if (array.Length - index < Count)
            {
                throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
            }

            var pairs = array as KeyValuePair<TKey, TValue>[];
            if (pairs != null)
            {
                CopyTo(pairs, index);
            }
            else if (array is DictionaryEntry[])
            {
                var dictEntryArray = array as DictionaryEntry[];

                var entriesHashCode = this.entriesHashCode;
                var entriesNext = this.entriesNext;
                var entriesKey = this.entriesKey;
                var entriesValue = this.entriesValue;

                for (var i = 0; i < count; i++)
                {
                    if (entriesHashCode[i] >= 0)
                    {
                        dictEntryArray[index++] = new DictionaryEntry(entriesKey[i], entriesValue[i]);
                    }
                }
            }
            else
            {
                var objects = array as object[];
                if (objects == null)
                {
                    throw new ArgumentException("Argument_InvalidArrayType");
                }

                try
                {
                    var count = this.count;

                    var entriesHashCode = this.entriesHashCode;
                    var entriesNext = this.entriesNext;
                    var entriesKey = this.entriesKey;
                    var entriesValue = this.entriesValue;

                    for (var i = 0; i < count; i++)
                    {
                        if (entriesHashCode[i] >= 0)
                        {
                            objects[index++] = new KeyValuePair<TKey, TValue>(entriesKey[i], entriesValue[i]);
                        }
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    throw new ArgumentException("Argument_InvalidArrayType");
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Enumerator(this, Enumerator.KeyValuePair);
        }

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot
        {
            get
            {
                if (_syncRoot == null)
                {
                    System.Threading.Interlocked.CompareExchange<object>(ref _syncRoot, new object(), null);
                }
                return _syncRoot;
            }
        }

        bool IDictionary.IsFixedSize => false;

        bool IDictionary.IsReadOnly => false;

        ICollection IDictionary.Keys => Keys;

        ICollection IDictionary.Values => Values;

        object IDictionary.this[object key]
        {
            get
            {
                if (IsCompatibleKey(key))
                {
                    var i = FindEntry((TKey)key);
                    if (i >= 0)
                    {
                        return entriesValue[i];
                    }
                }
                return null;
            }
            set
            {
                if (key == null)
                {
                    throw new ArgumentNullException("key");
                }

                if (value == null && !(default(TValue) == null))
                {
                    throw new ArgumentNullException("value");
                }

                try
                {
                    var tempKey = (TKey)key;
                    try
                    {
                        this[tempKey] = (TValue)value;
                    }
                    catch (InvalidCastException)
                    {
                        throw new ArgumentException($"Arg_WrongType -> value: {typeof(TValue)}");
                    }
                }
                catch (InvalidCastException)
                {
                    throw new ArgumentException($"Arg_WrongType -> key: {typeof(TKey)}");
                }
            }
        }

        private static bool IsCompatibleKey(object key)
        {
            if (key == null)
            {
                throw new ArgumentNullException("key");
            }
            return (key is TKey);
        }

        void IDictionary.Add(object key, object value)
        {
            if (key == null)
            {
                throw new ArgumentNullException("key");
            }

            if (value == null && !(default(TValue) == null))
            {
                throw new ArgumentNullException("value");
            }

            try
            {
                var tempKey = (TKey)key;

                try
                {
                    Add(tempKey, (TValue)value);
                }
                catch (InvalidCastException)
                {
                    throw new ArgumentException($"Arg_WrongType -> value: {typeof(TValue)}");
                }
            }
            catch (InvalidCastException)
            {
                throw new ArgumentException($"Arg_WrongType -> key: {typeof(TKey)}");
            }
        }

        bool IDictionary.Contains(object key)
        {
            if (IsCompatibleKey(key))
            {
                return ContainsKey((TKey)key);
            }

            return false;
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            return new Enumerator(this, Enumerator.DictEntry);
        }

        void IDictionary.Remove(object key)
        {
            if (IsCompatibleKey(key))
            {
                Remove((TKey)key);
            }
        }

        public void TrimExcess()
        {
            var newDict = new SerializableDictionaryBase<TKey, TValue>(Count, true);

            // fast copy
            for (var i = 0; i < count; i++)
            {
                if (entriesHashCode[i] >= 0)
                {
                    newDict.Add(entriesKey[i], entriesValue[i]);
                }
            }

            // copy internal field to this
            this.buckets = newDict.buckets;
            this.count = newDict.count;
            this.entriesHashCode = newDict.entriesHashCode;
            this.entriesKey = newDict.entriesKey;
            this.entriesNext = newDict.entriesNext;
            this.entriesValue = newDict.entriesValue;
            this.freeCount = newDict.freeCount;
            this.freeList = newDict.freeList;
        }

        public void CopyTo(Array array, int index)
        {
            throw new NotImplementedException();
        }

        [Serializable]
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
        {
            private SerializableDictionaryBase<TKey, TValue> dictionary;
            private int version;
            private int index;
            private KeyValuePair<TKey, TValue> current;
            private int getEnumeratorRetType;  // What should Enumerator.Current return?

            internal const int DictEntry = 1;
            internal const int KeyValuePair = 2;

            internal Enumerator(SerializableDictionaryBase<TKey, TValue> dictionary, int getEnumeratorRetType)
            {
                this.dictionary = dictionary;
                version = dictionary.version;
                index = 0;
                this.getEnumeratorRetType = getEnumeratorRetType;
                current = new KeyValuePair<TKey, TValue>();
            }

            public bool MoveNext()
            {
                if (version != dictionary.version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is Int32.MaxValue
                while ((uint)index < (uint)dictionary.count)
                {
                    if (dictionary.entriesHashCode[index] >= 0)
                    {
                        current = new KeyValuePair<TKey, TValue>(dictionary.entriesKey[index], dictionary.entriesValue[index]);
                        index++;
                        return true;
                    }
                    index++;
                }

                index = dictionary.count + 1;
                current = new KeyValuePair<TKey, TValue>();
                return false;
            }

            public KeyValuePair<TKey, TValue> Current => current;

            public void Dispose()
            {
            }

            object IEnumerator.Current
            {
                get
                {
                    if (index == 0 || (index == dictionary.count + 1))
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    if (getEnumeratorRetType == DictEntry)
                    {
                        return new System.Collections.DictionaryEntry(current.Key, current.Value);
                    }
                    else
                    {
                        return new KeyValuePair<TKey, TValue>(current.Key, current.Value);
                    }
                }
            }

            void IEnumerator.Reset()
            {
                if (version != dictionary.version)
                {
                    throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                }

                index = 0;
                current = new KeyValuePair<TKey, TValue>();
            }

            DictionaryEntry IDictionaryEnumerator.Entry
            {
                get
                {
                    if (index == 0 || (index == dictionary.count + 1))
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    return new DictionaryEntry(current.Key, current.Value);
                }
            }

            object IDictionaryEnumerator.Key
            {
                get
                {
                    if (index == 0 || (index == dictionary.count + 1))
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    return current.Key;
                }
            }

            object IDictionaryEnumerator.Value
            {
                get
                {
                    if (index == 0 || (index == dictionary.count + 1))
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                    }

                    return current.Value;
                }
            }
        }

        [DebuggerTypeProxy(typeof(DictionaryKeyCollectionDebugView<,>))]
        [DebuggerDisplay("Count = {Count}")]
        [Serializable]
        public sealed class KeyCollection : ICollection<TKey>, ICollection
        {
            private SerializableDictionaryBase<TKey, TValue> dictionary;

            public KeyCollection(SerializableDictionaryBase<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    throw new ArgumentNullException("dictionary");
                }
                this.dictionary = dictionary;
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            public void CopyTo(TKey[] array, int index)
            {
                if (array == null)
                {
                    throw new ArgumentNullException("array");
                }

                if (index < 0 || index > array.Length)
                {
                    throw new ArgumentOutOfRangeException("index", "ArgumentOutOfRange_NeedNonNegNum");
                }

                if (array.Length - index < dictionary.Count)
                {
                    throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
                }

                var count = dictionary.count;

                var entriesHashCode = dictionary.entriesHashCode;
                var entriesNext = dictionary.entriesNext;
                var entriesKey = dictionary.entriesKey;
                var entriesValue = dictionary.entriesValue;

                for (var i = 0; i < count; i++)
                {
                    if (entriesHashCode[i] >= 0)
                    {
                        array[index++] = entriesKey[i];
                    }
                }
            }

            public int Count => dictionary.Count;

            bool ICollection<TKey>.IsReadOnly => true;

            void ICollection<TKey>.Add(TKey item)
            {
                throw new NotSupportedException("NotSupported_KeyCollectionSet");
            }

            void ICollection<TKey>.Clear()
            {
                throw new NotSupportedException("NotSupported_KeyCollectionSet");
            }

            bool ICollection<TKey>.Contains(TKey item)
            {
                return dictionary.ContainsKey(item);
            }

            bool ICollection<TKey>.Remove(TKey item)
            {
                throw new NotSupportedException("NotSupported_KeyCollectionSet");
            }

            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            void ICollection.CopyTo(Array array, int index)
            {
                if (array == null)
                {
                    throw new ArgumentNullException("array");
                }

                if (array.Rank != 1)
                {
                    throw new ArgumentNullException("Arg_RankMultiDimNotSupported");
                }

                if (array.GetLowerBound(0) != 0)
                {
                    throw new ArgumentNullException("Arg_NonZeroLowerBound");
                }

                if (index < 0 || index > array.Length)
                {
                    throw new ArgumentOutOfRangeException("ArgumentOutOfRange_NeedNonNegNum");
                }

                if (array.Length - index < dictionary.Count)
                {
                    throw new ArgumentNullException("Arg_ArrayPlusOffTooSmall");
                }

                var keys = array as TKey[];
                if (keys != null)
                {
                    CopyTo(keys, index);
                }
                else
                {
                    var objects = array as object[];
                    if (objects == null)
                    {
                        throw new ArgumentException("Argument_InvalidArrayType");
                    }

                    var count = dictionary.count;

                    var entriesHashCode = dictionary.entriesHashCode;
                    var entriesNext = dictionary.entriesNext;
                    var entriesKey = dictionary.entriesKey;
                    var entriesValue = dictionary.entriesValue;

                    try
                    {
                        for (var i = 0; i < count; i++)
                        {
                            if (entriesHashCode[i] >= 0)
                            {
                                objects[index++] = entriesKey[i];
                            }
                        }
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        throw new ArgumentException("Argument_InvalidArrayType");
                    }
                }
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => ((ICollection)dictionary).SyncRoot;

            [Serializable]
            public struct Enumerator : IEnumerator<TKey>, System.Collections.IEnumerator
            {
                private SerializableDictionaryBase<TKey, TValue> dictionary;
                private int index;
                private int version;
                private TKey currentKey;

                internal Enumerator(SerializableDictionaryBase<TKey, TValue> dictionary)
                {
                    this.dictionary = dictionary;
                    version = dictionary.version;
                    index = 0;
                    currentKey = default(TKey);
                }

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    if (version != dictionary.version)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                    }

                    while ((uint)index < (uint)dictionary.count)
                    {
                        if (dictionary.entriesHashCode[index] >= 0)
                        {
                            currentKey = dictionary.entriesKey[index];
                            index++;
                            return true;
                        }
                        index++;
                    }

                    index = dictionary.count + 1;
                    currentKey = default(TKey);
                    return false;
                }

                public TKey Current => currentKey;

                object System.Collections.IEnumerator.Current
                {
                    get
                    {
                        if (index == 0 || (index == dictionary.count + 1))
                        {
                            throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                        }

                        return currentKey;
                    }
                }

                void System.Collections.IEnumerator.Reset()
                {
                    if (version != dictionary.version)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                    }

                    index = 0;
                    currentKey = default(TKey);
                }
            }
        }

        [DebuggerTypeProxy(typeof(DictionaryValueCollectionDebugView<,>))]
        [DebuggerDisplay("Count = {Count}")]
        [Serializable]
        public sealed class ValueCollection : ICollection<TValue>, ICollection
        {
            private SerializableDictionaryBase<TKey, TValue> dictionary;

            public ValueCollection(SerializableDictionaryBase<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    throw new ArgumentNullException("dictionary");
                }
                this.dictionary = dictionary;
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            public void CopyTo(TValue[] array, int index)
            {
                if (array == null)
                {
                    throw new ArgumentNullException("array");
                }

                if (index < 0 || index > array.Length)
                {
                    throw new ArgumentOutOfRangeException("index", "ArgumentOutOfRange_NeedNonNegNum");
                }

                if (array.Length - index < dictionary.Count)
                {
                    throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
                }

                var count = dictionary.count;

                var entriesHashCode = dictionary.entriesHashCode;
                var entriesNext = dictionary.entriesNext;
                var entriesKey = dictionary.entriesKey;
                var entriesValue = dictionary.entriesValue;

                for (var i = 0; i < count; i++)
                {
                    if (entriesHashCode[i] >= 0)
                    {
                        array[index++] = entriesValue[i];
                    }
                }
            }

            public int Count => dictionary.Count;

            bool ICollection<TValue>.IsReadOnly => true;

            void ICollection<TValue>.Add(TValue item)
            {
                throw new NotSupportedException("NotSupported_ValueCollectionSet");
            }

            bool ICollection<TValue>.Remove(TValue item)
            {
                throw new NotSupportedException("NotSupported_ValueCollectionSet");
            }

            void ICollection<TValue>.Clear()
            {
                throw new NotSupportedException("NotSupported_ValueCollectionSet");
            }

            bool ICollection<TValue>.Contains(TValue item)
            {
                return dictionary.ContainsValue(item);
            }

            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new Enumerator(dictionary);
            }

            void ICollection.CopyTo(Array array, int index)
            {
                if (array == null)
                {
                    throw new ArgumentNullException("array");
                }

                if (array.Rank != 1)
                {
                    throw new ArgumentException("Arg_RankMultiDimNotSupported");
                }

                if (array.GetLowerBound(0) != 0)
                {
                    throw new ArgumentException("Arg_NonZeroLowerBound");
                }

                if (index < 0 || index > array.Length)
                {
                    throw new ArgumentOutOfRangeException("index", "ArgumentOutOfRange_NeedNonNegNum");
                }

                if (array.Length - index < dictionary.Count)
                {
                    throw new ArgumentException("Arg_ArrayPlusOffTooSmall");
                }

                var values = array as TValue[];
                if (values != null)
                {
                    CopyTo(values, index);
                }
                else
                {
                    var objects = array as object[];
                    if (objects == null)
                    {
                        throw new ArgumentException("Argument_InvalidArrayType");
                    }

                    var count = dictionary.count;

                    var entriesHashCode = dictionary.entriesHashCode;
                    var entriesNext = dictionary.entriesNext;
                    var entriesKey = dictionary.entriesKey;
                    var entriesValue = dictionary.entriesValue;

                    try
                    {
                        for (var i = 0; i < count; i++)
                        {
                            if (entriesHashCode[i] >= 0)
                            {
                                objects[index++] = entriesValue[i];
                            }
                        }
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        throw new ArgumentException("Argument_InvalidArrayType");
                    }
                }
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => ((ICollection)dictionary).SyncRoot;

            [Serializable]
            public struct Enumerator : IEnumerator<TValue>, System.Collections.IEnumerator
            {
                private SerializableDictionaryBase<TKey, TValue> dictionary;
                private int index;
                private int version;
                private TValue currentValue;

                internal Enumerator(SerializableDictionaryBase<TKey, TValue> dictionary)
                {
                    this.dictionary = dictionary;
                    version = dictionary.version;
                    index = 0;
                    currentValue = default(TValue);
                }

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    if (version != dictionary.version)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                    }

                    while ((uint)index < (uint)dictionary.count)
                    {
                        if (dictionary.entriesHashCode[index] >= 0)
                        {
                            currentValue = dictionary.entriesValue[index];
                            index++;
                            return true;
                        }
                        index++;
                    }
                    index = dictionary.count + 1;
                    currentValue = default(TValue);
                    return false;
                }

                public TValue Current => currentValue;

                object System.Collections.IEnumerator.Current
                {
                    get
                    {
                        if (index == 0 || (index == dictionary.count + 1))
                        {
                            throw new InvalidOperationException("InvalidOperation_EnumOpCantHappen");
                        }

                        return currentValue;
                    }
                }

                void System.Collections.IEnumerator.Reset()
                {
                    if (version != dictionary.version)
                    {
                        throw new InvalidOperationException("InvalidOperation_EnumFailedVersion");
                    }
                    index = 0;
                    currentValue = default(TValue);
                }
            }
        }

        private static class HashHelpers
        {
            // Table of prime numbers to use as hash table sizes. 
            // A typical resize algorithm would pick the smallest prime number in this array
            // that is larger than twice the previous capacity. 
            // Suppose our Hashtable currently has capacity x and enough elements are added 
            // such that a resize needs to occur. Resizing first computes 2x then finds the 
            // first prime in the table greater than 2x, i.e. if primes are ordered 
            // p_1, p_2, ..., p_i, ..., it finds p_n such that p_n-1 < 2x < p_n. 
            // Doubling is important for preserving the asymptotic complexity of the 
            // hashtable operations such as add.  Having a prime guarantees that double 
            // hashing does not lead to infinite loops.  IE, your hash function will be 
            // h1(key) + i*h2(key), 0 <= i < size.  h2 and the size must be relatively prime.
            public static readonly int[] primes = {
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
            1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
            17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
            187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
            1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369, 8639249, 10367101,
            12440537, 14928671, 17914409, 21497293, 25796759, 30956117, 37147349, 44576837, 53492207, 64190669,
            77028803, 92434613, 110921543, 133105859, 159727031, 191672443, 230006941, 276008387, 331210079,
            397452101, 476942527, 572331049, 686797261, 824156741, 988988137, 1186785773, 1424142949, 1708971541,
            2050765853, MaxPrimeArrayLength };

            public static bool IsPrime(int candidate)
            {
                if ((candidate & 1) != 0)
                {
                    var limit = (int)Math.Sqrt(candidate);
                    for (var divisor = 3; divisor <= limit; divisor += 2)
                    {
                        if ((candidate % divisor) == 0)
                        {
                            return false;
                        }
                    }
                    return true;
                }
                return (candidate == 2);
            }

            public static int GetPrime(int min)
            {
                if (min < 0)
                {
                    throw new ArgumentException(/*SR.Arg_HTCapacityOverflow*/"Arg_HTCapacityOverflow");
                }

                for (var i = 0; i < primes.Length; i++)
                {
                    var prime = primes[i];
                    if (prime >= min)
                    {
                        return prime;
                    }
                }

                for (var i = (min | 1); i < int.MaxValue; i += 2)
                {
                    if (IsPrime(i) && ((i - 1) % HashPrime != 0))
                    {
                        return i;
                    }
                }

                return min;
            }

            public static int GetMinPrime()
            {
                return primes[0];
            }

            // Returns size of hashtable to grow to.
            public static int ExpandPrime(int oldSize)
            {
                var newSize = 2 * oldSize;

                // Allow the hashtables to grow to maximum possible size (~2G elements) before encoutering capacity overflow.
                // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
                if ((uint)newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize)
                {
                    return MaxPrimeArrayLength;
                }

                return GetPrime(newSize);
            }


            // This is the maximum prime smaller than Array.MaxArrayLength
            public const int MaxPrimeArrayLength = 0x7FEFFFFD;

            internal const int HashPrime = 101;
        }
    }

    internal sealed class IDictionaryDebugView<K, V>
    {
        private readonly IDictionary<K, V> _dict;

        public IDictionaryDebugView(IDictionary<K, V> dictionary)
        {
            _dict = dictionary ?? throw new ArgumentNullException("dictionary");
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<K, V>[] Items
        {
            get
            {
                var items = new KeyValuePair<K, V>[_dict.Count];
                _dict.CopyTo(items, 0);
                return items;
            }
        }
    }

    internal sealed class DictionaryKeyCollectionDebugView<TKey, TValue>
    {
        private readonly ICollection<TKey> _collection;

        public DictionaryKeyCollectionDebugView(ICollection<TKey> collection)
        {
            _collection = collection ?? throw new ArgumentNullException("collection");
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public TKey[] Items
        {
            get
            {
                var items = new TKey[_collection.Count];
                _collection.CopyTo(items, 0);
                return items;
            }
        }
    }

    internal sealed class DictionaryValueCollectionDebugView<TKey, TValue>
    {
        private readonly ICollection<TValue> _collection;

        public DictionaryValueCollectionDebugView(ICollection<TValue> collection)
        {
            _collection = collection ?? throw new ArgumentNullException("collection");
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public TValue[] Items
        {
            get
            {
                var items = new TValue[_collection.Count];
                _collection.CopyTo(items, 0);
                return items;
            }
        }
    }
}