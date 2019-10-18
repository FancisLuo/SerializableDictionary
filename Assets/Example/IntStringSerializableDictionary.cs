using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Codec.SerializableDictionary.Example
{
    [Serializable]
    public class IntStringSerializableDictionary : SerializableDictionaryBase<int, string>
    {
        public IntStringSerializableDictionary() : base() { }

        public IntStringSerializableDictionary(int capacity) : base(capacity) { }

        public IntStringSerializableDictionary(IDictionary<int, string> pairs) : base(pairs) { }
    }
}