using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using Microsoft.Net.Http.Headers;

namespace jsdal_server_core
{
    public static class MyExtensions
    {
        public static V Val<T,V>(this Dictionary<T,V>  dict, T key)
        {
            if (!dict.ContainsKey(key)) return default;

            return dict[key];
        }
        public static long? ToEpochMS(this DateTime? dt)
        {
            if (!dt.HasValue) return null;
            return dt.Value.ToEpochMS();
        }

        public static long ToEpochMS(this DateTime dt)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Convert.ToInt64((dt.ToUniversalTime() - epoch).TotalMilliseconds);
        }

        public static EntityTagHeaderValue ToETag(this FileInfo fi)
        {
            long etagHash = fi.LastWriteTimeUtc.ToFileTime() ^ fi.Length;
            return new EntityTagHeaderValue('\"' + Convert.ToString(etagHash, 16) + '\"');
        }

    }

    public static class TupleExtensions
    {
        //https://stackoverflow.com/a/46708944
        private static readonly HashSet<Type> ValueTupleTypes = new HashSet<Type>(new Type[]
        {
            typeof(ValueTuple<>),
            typeof(ValueTuple<,>),
            typeof(ValueTuple<,,>),
            typeof(ValueTuple<,,,>),
            typeof(ValueTuple<,,,,>),
            typeof(ValueTuple<,,,,,>),
            typeof(ValueTuple<,,,,,,>),
            typeof(ValueTuple<,,,,,,,>)
        });

        public static bool IsValueTuple(this object obj) => IsValueTupleType(obj.GetType());
        public static bool IsValueTupleType(this Type type)
        {
            return type.GetTypeInfo().IsGenericType && ValueTupleTypes.Contains(type.GetGenericTypeDefinition());
        }

        //public static List<object> GetValueTupleItemObjects(this object tuple) => GetValueTupleItemFields(tuple.GetType()).Select(f => f.GetValue(tuple)).ToList();
        public static List<Type> GetValueTupleItemTypes(this Type tupleType) => GetValueTupleItemFields(tupleType).Select(f => f.FieldType).ToList();
        public static List<FieldInfo> GetValueTupleItemFields(this Type tupleType)
        {
            var items = new List<FieldInfo>();

            FieldInfo field;
            int nth = 1;
            while ((field = tupleType.GetRuntimeField($"Item{nth}")) != null)
            {
                nth++;
                items.Add(field);
            }

            return items;
        }
    }



}