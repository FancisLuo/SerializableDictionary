using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Codec.SerializableDictionary.Example
{
    public class Example : MonoBehaviour
    {
        public IntStringSerializableDictionary IntStringMap = new IntStringSerializableDictionary();

        private void Start()
        {
            IntStringMap.Add(4, "5555");

        }
    }
}