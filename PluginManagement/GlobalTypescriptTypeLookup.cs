using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using System.Threading;
using System.Collections.ObjectModel;

namespace jsdal_server_core
{
    public static class GlobalTypescriptTypeLookup
    {
        private static Dictionary<string/*(type fully qualified name)*/, DeferredDefinition> _cachedDefinitions;
        private static Dictionary<int/*RefId*/, string> _refLookup;

        public static ReadOnlyCollection<DeferredDefinition> Definitions
        {
            get
            {
                return _cachedDefinitions.Values.ToList().AsReadOnly();
            }
        }


        private static int _curRefId = 0;
        static GlobalTypescriptTypeLookup()
        {
            _cachedDefinitions = new Dictionary<string, DeferredDefinition>();
            _refLookup = new Dictionary<int, string>();
        }

        public class DeferredDefinition
        {
            private Type _type;
            private string _cachedDefinition;
            private bool _isComplete;

            public DeferredDefinition(Type type, int refId)
            {
                _type = type;
                _isComplete = false;
                RefId = refId;

                this.TypeName = JsFileGenerator.MakeNameJsSafe($"{type.Assembly.GetName().Name}_{type.Namespace}_{type.Name}");
            }

            public int RefId { get; private set; }

            public void Complete(string definition)
            {
                if (_isComplete) return;
                _cachedDefinition = definition;
                _isComplete = true;
            }

            public bool IsComplete
            {
                get { return _isComplete; }
            }

            public string TypeName
            {
                get; set;
            }

            public string Definition
            {
                get { return _cachedDefinition; }
            }
        }


        private static (DeferredDefinition, bool/*isExisting*/, int/*Ref*/) RegisterDefinition(Type type)
        {
            DeferredDefinition def;

            // look for existing defintion
            lock (_cachedDefinitions)
            {
                if (_cachedDefinitions.ContainsKey(type.AssemblyQualifiedName))
                {
                    def = _cachedDefinitions[type.AssemblyQualifiedName];

                    int refId = _refLookup.First(kv => kv.Value.Equals(type.AssemblyQualifiedName, StringComparison.Ordinal)).Key;

                    return (def, true, refId);
                }
                else
                {
                    var refId = Interlocked.Increment(ref _curRefId);

                    def = new DeferredDefinition(type, refId);
                    _cachedDefinitions.Add(type.AssemblyQualifiedName, def);



                    _refLookup[refId] = type.AssemblyQualifiedName;

                    return (def, false, refId);
                }
            }
        }

        public static string GetTypescriptTypeFromCSharp(Type type)
        {
            if (type.FullName.Equals("System.Void", StringComparison.Ordinal)) return "void";

            const string any = "any";

            if (type.IsByRef)
            {
                // switch from 'ref' type to actual (e.g. System.Int32& to System.Int32)
                type = type.GetElementType();
            }

            var underlyingNullableType = Nullable.GetUnderlyingType(type);
            var isNullable = underlyingNullableType != null;

            if (isNullable)
            {
                type = underlyingNullableType;
            }

            var jsonObjectAttrib = type.GetCustomAttribute(typeof(JsonObjectAttribute));

            // JSON serializable!
            if (jsonObjectAttrib != null)
            {
                (var typeDefinition, var isExisting, var refId) = RegisterDefinition(type);


                if (isExisting)
                {
                    return $"__.{typeDefinition.TypeName}";
                }

                var availableProps = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetCustomAttribute(typeof(JsonPropertyAttribute)) != null);
                var availableFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetCustomAttribute(typeof(JsonPropertyAttribute)) != null);

                var q1 = from p in availableProps
                         select $"{p.Name}: {GetTypescriptTypeFromCSharp(p.PropertyType)}";

                var q2 = from f in availableFields
                         select $"{f.Name}: {GetTypescriptTypeFromCSharp(f.FieldType)}";


                var res = "{" + string.Join(", ", q1.Concat(q2)) + "}";

                typeDefinition.Complete(res);

                return $"__.{typeDefinition.TypeName}";

            }
            // TUPLES
            else if (type.IsValueTupleType())
            {
                var fields = type.GetValueTupleItemFields();

                if (fields == null) return any;

                var q = (from f in fields
                         select $"{f.Name}: {GetTypescriptTypeFromCSharp(f.FieldType)}");

                return "{" + string.Join(", ", q) + "}";

            }
            // List<>
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var arg = type.GetGenericArguments();

                if (arg.Length != 1) return any;

                return GetTypescriptTypeFromCSharp(arg[0]) + "[]";
            }
            // Dictionary<>
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = type.GetGenericArguments();

                if (args.Length != 2) return any;

                var keyType = GetTypescriptTypeFromCSharp(args[0]);
                var valueType = GetTypescriptTypeFromCSharp(args[1]);

                return $"{{ [id: {keyType}] : {valueType} }}";
            }

            var lookup = new Dictionary<string, List<string>>()
            {
                ["number"] = new List<string> { nameof(System.Int16), nameof(System.Int32), nameof(System.Int64), nameof(System.Double), nameof(System.Decimal), nameof(System.UInt16), nameof(System.UInt32), nameof(System.UInt64) },
                ["string"] = new List<string> { nameof(System.String), nameof(System.Guid) },
                ["Date"] = new List<string> { nameof(System.DateTime) },
                ["boolean"] = new List<string> { nameof(System.Boolean) }
            };

            var match = lookup.FirstOrDefault(kv => kv.Value.Contains(type.Name.TrimEnd('&')));

            if (match.Key == null)
            {
                int n = 0;
            }

            return match.Key == null ? any : match.Key;
        }

    }
}