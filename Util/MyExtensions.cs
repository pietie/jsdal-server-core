using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using Microsoft.Net.Http.Headers;
using System.Diagnostics;
using System.Linq.Expressions;

namespace jsdal_server_core
{
    // https://stackoverflow.com/a/63685720
    public static class ExceptionExtensions
    {
        public static Exception SetStackTrace(this Exception target, StackTrace stack)
        {
            return _SetStackTrace(target, stack);
        }

        private static readonly Func<Exception, StackTrace, Exception> _SetStackTrace = new Func<Func<Exception, StackTrace, Exception>>(() =>
        {
            System.Diagnostics.Debugger.Break();
            ParameterExpression target = Expression.Parameter(typeof(Exception));
            ParameterExpression stack = Expression.Parameter(typeof(StackTrace));
            Type traceFormatType = typeof(StackTrace).GetNestedType("TraceFormat", BindingFlags.NonPublic);
            MethodInfo toString = typeof(StackTrace).GetMethod("ToString", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { traceFormatType }, null);
            object normalTraceFormat = Enum.GetValues(traceFormatType).GetValue(0);
            MethodCallExpression stackTraceString = Expression.Call(stack, toString, Expression.Constant(normalTraceFormat, traceFormatType));
            FieldInfo stackTraceStringField = typeof(Exception).GetField("_stackTraceString", BindingFlags.NonPublic | BindingFlags.Instance);
            BinaryExpression assign = Expression.Assign(Expression.Field(target, stackTraceStringField), stackTraceString);
            return Expression.Lambda<Func<Exception, StackTrace, Exception>>(Expression.Block(assign, target), target, stack).Compile();
        })();
    }

    public static class MyExtensions
    {
        public static int ByteSize(this string s)
        {
            if (s == null) return 0;
            return s.Length * sizeof(char) + sizeof(int)/*Length parameter*/;
        }

        public static V Val<T, V>(this Dictionary<T, V> dict, T key)  where T : notnull
        {
            // fallback to case-insensitive key lookup if necessary
            if (!dict.ContainsKey(key) && typeof(T) == typeof(string))
            {
                key = dict.Keys.FirstOrDefault(k=>k!.ToString()!.Equals(key.ToString(), StringComparison.OrdinalIgnoreCase));
                if (key == null) return default;
            }
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

        public static string Left(this string s, int left, bool addEllipse = false)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string ellipse = addEllipse ? "..." : "";
            return s.Length <= left ? s : (s.Substring(0, left) + ellipse);
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