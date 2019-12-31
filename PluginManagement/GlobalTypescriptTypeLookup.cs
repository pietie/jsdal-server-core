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

            // public bool IsComplete
            // {
            //     get { return _isComplete; }
            // }

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

        private static readonly Dictionary<string, List<string>> TypeToTSlookup = new Dictionary<string, List<string>>()
        {
            ["number"] = new List<string> { nameof(System.Int16), nameof(System.Int32), nameof(System.Int64),
                                                nameof(System.UInt16), nameof(System.UInt32), nameof(System.UInt64),
                                                nameof(System.Double), nameof(System.Decimal), nameof(System.Single)

                                            },
            ["string"] = new List<string> { nameof(System.String), nameof(System.Guid), nameof(System.Char) },
            ["Date"] = new List<string> { nameof(System.DateTime) },
            ["boolean"] = new List<string> { nameof(System.Boolean) },
            ["Uint8Array"] = new List<string> { nameof(System.Byte) }
        };


        public static string GetTypescriptTypeFromCSharp(Type type)
        {
            if (type.FullName.Equals("System.Void", StringComparison.Ordinal)) return "void";

            const string any = "any";


            // NOTE: The order of the checks for IsByRef, IsArray and IsNullable are very important
            if (type.IsByRef)
            {
                // switch from 'ref' type to actual (e.g. System.Int32& to System.Int32)
                type = type.GetElementType();
            }

            var isArray = type.IsArray;

            if (isArray)
            {
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

            var match = TypeToTSlookup.FirstOrDefault(kv => kv.Value.Contains(type.Name.TrimEnd('&')));

            if (match.Key == null)
            {
                int n = 0;
            }

            var val = match.Key;

            if (val != null && isArray && val != "Uint8Array") // Uint32Array are already an array structure
            {
                return val += "[]";
            }

            return val == null ? any : val;
        }

        // TODO: Decide where to place this function
        public static string SerializeCSharpToJavaScript(string objectName, object val)
        {
            if (val == null) return null;
            var type = val.GetType();

            if (type.IsByRef)
            {
                // switch from 'ref' type to actual (e.g. System.Int32& to System.Int32)
                type = type.GetElementType();
            }

            var isArray = type.IsArray;

            if (isArray)
            {
                type = type.GetElementType();
            }

            var underlyingNullableType = Nullable.GetUnderlyingType(type);
            var isNullable = underlyingNullableType != null;

            if (isNullable)
            {
                type = underlyingNullableType;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                // TODO: converter will be on the LIST level (not item). If the item is a List itself for example then the next level should take care of it???


                var list = (IEnumerable<object>)val;

                var res = (from item in list
                           select $"{SerializeCSharpToJavaScript(null, item)}").ToArray();

                return "[" + string.Join(",", res) + "]";

            }
            else if (isArray && type == typeof(Byte))
            {
                var b64 = $"\"{Convert.ToBase64String((byte[])val).TrimEnd('=')}\"";

                return b64;
            }
            else
            {
                // fallback to default serializer
                return JsonConvert.SerializeObject(val);
            }

        }


    }

    public class ComplexTypeConverterDefinitionWrapper
    {
        public delegate void OnCompleteDelegate(ref Dictionary<string, ConverterDefinition> convertersFlatLookup, string parentName, Dictionary<string, ConverterDefinition> typeLookup);

        public Dictionary<string/*prop name*/, ConverterDefinition> Lookup { get; private set; }

        private List<dynamic> _onCompleteActions;

        private bool _isComplete = false;
        object _lock = new Object();

        public ComplexTypeConverterDefinitionWrapper()
        {
            this.Lookup = new Dictionary<string, ConverterDefinition>();
        }

        public void OnComplete(string keyName, OnCompleteDelegate action, ref Dictionary<string, ConverterDefinition> masterLookup)
        {
            lock (_lock)
            {
                //if (_isComplete) throw new InvalidOperationException("Already marked complete");
                if (_onCompleteActions == null) _onCompleteActions = new List<dynamic>();

                if (!_isComplete)
                {
                    _onCompleteActions.Add(new { KeyName = keyName, Action = action });
                }
                else // already complete so fire immediately
                {
                    action(ref masterLookup, keyName, this.Lookup);
                }

            }
        }

        public void Complete(ref Dictionary<string, ConverterDefinition> masterLookup, string parentName)
        {
            lock (_lock)
            {
                _isComplete = true;
                if (_onCompleteActions != null)
                {
                    foreach (var callback in _onCompleteActions)
                    {
                        callback.Action(ref masterLookup, callback.KeyName, this.Lookup);
                    }
                }
            }

        }
    }

    public static class GlobalConverterLookup
    {
        private static Dictionary<string/*(type fully qualified name)*/, ComplexTypeConverterDefinitionWrapper> _cachedLookups;

        static GlobalConverterLookup()
        {
            _cachedLookups = new Dictionary<string/*AssemblyQualifiedName*/, ComplexTypeConverterDefinitionWrapper>();
        }

        public static (ComplexTypeConverterDefinitionWrapper, bool/*isExisting*/) RegisterDefinition(Type type)
        {
            lock (_cachedLookups)
            {
                if (_cachedLookups.ContainsKey(type.AssemblyQualifiedName))
                {
                    var existing = _cachedLookups[type.AssemblyQualifiedName];
                    return (existing, true);
                }
                else
                {
                    var n = new ComplexTypeConverterDefinitionWrapper();

                    _cachedLookups.Add(type.AssemblyQualifiedName, n);

                    return (n, false);
                }
            }
        }

        public static void AnalyseForRequiredOutputConverters(string objectName, Type type, string parentName, ref Dictionary<string, ConverterDefinition> convertersFlatLookup)
        {
            if (type == null) return;

            var keyName = string.IsNullOrWhiteSpace(parentName) ? objectName : $"{parentName}.{objectName}";

            if (convertersFlatLookup.ContainsKey(keyName))
            {
                System.Diagnostics.Debugger.Break();
            }

            if (type.IsByRef)
            {
                // switch from 'ref' type to actual (e.g. System.Int32& to System.Int32)
                type = type.GetElementType();
            }

            var isArray = type.IsArray;

            if (isArray)
            {
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
                (var wrapper, var isExisting) = RegisterDefinition(type);

                if (isExisting)
                {
                    wrapper.OnComplete(keyName, (ref Dictionary<string, ConverterDefinition> masterLookup, string parentKeyName, Dictionary<string, ConverterDefinition> typeLookup) =>
                    {
                        foreach (var kv in typeLookup)
                        {
                            masterLookup.Add($"{parentKeyName}.{kv.Key}", kv.Value);
                        }
                    }, ref convertersFlatLookup);


                    return;
                }
                var availableProps = from p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetCustomAttribute(typeof(JsonPropertyAttribute)) != null)
                                     select new { p.Name, Type = p.PropertyType };

                var availableFields = from f in type.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetCustomAttribute(typeof(JsonPropertyAttribute)) != null).ToList()
                                      select new { f.Name, Type = f.FieldType };


                var typeSpecificLookup = new Dictionary<string, ConverterDefinition>();

                foreach (var item in availableProps.Concat(availableFields))
                {
                    AnalyseForRequiredOutputConverters(item.Name, item.Type, keyName, ref typeSpecificLookup);
                }

                foreach (var kv in typeSpecificLookup)
                {
                    wrapper.Lookup.Add(kv.Key, kv.Value);
                    convertersFlatLookup.Add(kv.Key, kv.Value);
                }


                wrapper.Complete(ref convertersFlatLookup, keyName);

                return;
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                // TODO: converter will be on the LIST level (not item). If the item is a List itself for example then the next level should take care of it???
                var arg = type.GetGenericArguments();

                if (arg.Length != 1) return;

                AnalyseForRequiredOutputConverters(objectName, arg[0], null, ref convertersFlatLookup);

                return;
            }
            // TUPLES
            else if (type.IsValueTupleType())
            {
                var fields = type.GetValueTupleItemFields();

                if (fields == null) return;

                foreach(var f in fields)
                {
                    AnalyseForRequiredOutputConverters(f.Name, f.FieldType, keyName, ref convertersFlatLookup);
                }
            }
            else if (isArray && type == typeof(Byte))
            {
                convertersFlatLookup.Add(keyName, ConverterDefinition.Create("jsDAL.Converters.ByteArrayConverter"));
            }
            else if (type == typeof(DateTime))
            {
                convertersFlatLookup.Add(keyName, ConverterDefinition.Create("jsDAL.Converters.DateTimeConverter"));
            }
        }

    }


    public class ConverterDefinition
    {
        public string Converter { get; private set; }
        public string ConverterOptions { get; private set; }

        private ConverterDefinition() { }

        public static ConverterDefinition Create(string converter, string options = null)
        {
            return new ConverterDefinition() { Converter = converter, ConverterOptions = options };
        }

        // public static string Create(string converter, string options = null)
        // {
        //     return new ConverterDefinition() { _type = type, Converter = converter, ConverterOptions = options };
        // }

        public string ToJson()
        {
            return $"{{ converter: {this.Converter} }}";
        }
    }
}