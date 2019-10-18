using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEditor;
using Codec.SerializableDictionary.Editor;

namespace Codec.SerializableDictionary.Example
{
    [CustomPropertyDrawer(typeof(IntStringSerializableDictionary))]
    public class ExtendedSerializableDictionaryPropertyDrawer : SerializableDictionaryPropertyDrawerBase
    {

    }
}