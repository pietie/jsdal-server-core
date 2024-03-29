// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Linq;
// using Newtonsoft.Json;
// using shortid;

// namespace jsdal_server_core
// {

//     [Obsolete("Replaced with LiteDB based implementation")]
//     public class ExceptionLoggerFileBased
//     {
//         private static string ExceptionFilePath = "./data/exceptions.lst";
//         private static readonly int MAX_ENTRIES_PER_ENDPOINT = 1000;
//         private static Dictionary<string/*EndpointKey*/, List<ExceptionWrapper>> exceptionDict = new Dictionary<string, List<ExceptionWrapper>>();

//         public static List<string> Endpoints
//         {
//             get
//             {
//                 return exceptionDict.Keys.OrderBy(k => k).ToList();
//             }
//         }

//         public static List<string> AppTitles
//         {
//             get
//             {
//                 return exceptionDict.Values.SelectMany(l => l).Select(e => e.appTitle).Where(at => !string.IsNullOrWhiteSpace(at)).Distinct().OrderBy(at => at).ToList();
//             }
//         }


//         private static void SaveToFile()
//         {
//             try
//             {
//                 lock (exceptionDict)
//                 {
//                     var fi = new FileInfo(ExceptionFilePath);

//                     if (!fi.Directory.Exists)
//                     {
//                         fi.Directory.Create();
//                     }

//                     var json = JsonConvert.SerializeObject(exceptionDict);
//                     File.WriteAllText(ExceptionFilePath, json, System.Text.Encoding.UTF8);
//                 }
//             }
//             catch (Exception ex)
//             {
//                 ExceptionLoggerFileBased.LogException(ex);
//             }
//         }

//         private static bool LoadFromFile()
//         {
//             try
//             {
//                 lock (exceptionDict)
//                 {
//                     if (!File.Exists(ExceptionFilePath)) return false;

//                     var data = File.ReadAllText(ExceptionFilePath, System.Text.Encoding.UTF8);

//                     if (data != null)
//                     {
//                         exceptionDict = JsonConvert.DeserializeObject<Dictionary<string, List<ExceptionWrapper>>>(data);

//                     }
//                     return true;
//                 }
//             }
//             catch (Exception ex)
//             {
//                 ExceptionLoggerFileBased.LogException(ex);
//                 return false;
//             }

//         }

//         public static void Init()
//         {
//             LoadFromFile();
//         }

//         // public static List<ExceptionWrapper> Exceptions
//         // {
//         //     get { return ExceptionLogger.exceptionDict.Values.ToList(); }
//         // }

//         public static ExceptionWrapper GetException(string id)
//         {
//             return ExceptionLoggerFileBased.exceptionDict.Values.SelectMany(l => l).FirstOrDefault(e => e.sId == id);
//         }

//         public static ExceptionWrapper DeepFindRelated(string id)
//         {
//             foreach (var topLevelException in ExceptionLoggerFileBased.exceptionDict.Values.SelectMany(l => l))
//             {
//                 var ew = topLevelException.GetRelated(id);

//                 if (ew != null) return ew;
//             }

//             return null;
//         }

//         public static IEnumerable<ExceptionWrapper> GetAll(string[] endpointLookup)
//         {
//             // return ALL
//             if (endpointLookup == null || endpointLookup.Length == 0) return ExceptionLoggerFileBased.exceptionDict.Values.SelectMany(l => l);
//             else
//             {
//                 // look for lists that match the specified endpoint(s) filter
//                 var matchingKeys = ExceptionLoggerFileBased.exceptionDict.Keys
//                         .Where(k => endpointLookup.FirstOrDefault(ep => ep.Equals(k, StringComparison.OrdinalIgnoreCase)) != null);


//                 if (matchingKeys.Count() == 0) // none of the requested list exist
//                 {
//                     return new List<ExceptionWrapper>();
//                 }

//                 var matchingLists = ExceptionLoggerFileBased.exceptionDict.Where(kv => matchingKeys.Contains(kv.Key)).Select(kv => kv.Value).SelectMany(l => l);
//                 return matchingLists;
//             }
//         }


//         public static void ClearAll()
//         {
//             ExceptionLoggerFileBased.exceptionDict.Clear();
//             SaveToFile();
//         }

//         public static int TotalCnt
//         {
//             get
//             {
//                 if (ExceptionLoggerFileBased.exceptionDict == null) return 0;
//                 return ExceptionLoggerFileBased.exceptionDict.Values.SelectMany(l => l).Count();
//             }
//         }

//         public static string LogException(Exception ex, string additionalInfo = null, string appTitle = null, string appVersion = null)
//         {
//             return AddException("Global", ex, null, additionalInfo, appTitle, appVersion);
//         }

//         public static string LogException(Exception ex, Controllers.ExecController.ExecOptions execOptions, string additionalInfo = null, string appTitle = null, string appVersion = null)
//         {
//             string endpointKey = "Global";

//             if (execOptions != null)
//             {
//                 endpointKey = $"{execOptions.project}/{execOptions.application}/{execOptions.endpoint}".ToUpper();
//             }

//             return AddException(endpointKey, ex, execOptions, additionalInfo, appTitle, appVersion);
//         }

//         private static string AddException(string listKey, Exception ex, Controllers.ExecController.ExecOptions execOptions, string additionalInfo, string appTitle, string appVersion = null)
//         {
//             lock (exceptionDict)
//             {
//                 if (!exceptionDict.ContainsKey(listKey)) exceptionDict.Add(listKey, new List<ExceptionWrapper>());
//             }

//             lock (exceptionDict[listKey])
//             {
//                 if (ExceptionLoggerFileBased.exceptionDict[listKey].Count >= ExceptionLoggerFileBased.MAX_ENTRIES_PER_ENDPOINT)
//                 {
//                     // cull from the front
//                     int count = (ExceptionLoggerFileBased.exceptionDict.Count - ExceptionLoggerFileBased.MAX_ENTRIES_PER_ENDPOINT) + 1;
//                     if (count > 0)
//                     {
//                         ExceptionLoggerFileBased.exceptionDict[listKey].RemoveRange(0, count);
//                     }
//                 }

//                 var ew = new ExceptionWrapper(ex, execOptions, additionalInfo, appTitle, appVersion);

//                 ExceptionWrapper parent = null;

//                 // TODO: think of other ways to find "related". Message might not match 100% so apply "like" search of match on ErrorType(e.g. group Timeouts)

//                 // look at last (n) exceptions for an exact match
//                 if (parent == null)
//                 {
//                     parent = exceptionDict[listKey]
//                         .TakeLast(5)
//                         .Where(e => e.server == null && ew.server == null || (e.server?.Equals(ew.server, StringComparison.OrdinalIgnoreCase) ?? false))
//                         .Where(e => e.message.Equals(ew.message, StringComparison.OrdinalIgnoreCase))
//                         .Where(e => e.innerException == null && ew.innerException == null || (e.innerException?.message.Equals(ew.innerException?.message, StringComparison.OrdinalIgnoreCase) ?? false))
//                         .FirstOrDefault();
//                 }

//                 // look for recent similiar entry, if found just tag it onto that rather than logging a new main entry
//                 if (parent == null)
//                 {
//                     DateTime thresholdDate = DateTime.Now.AddMinutes(-2.0); // look for last 2mins

//                     parent = exceptionDict[listKey]
//                                 .Where(e => e.created >= thresholdDate)
//                                 .Where(e => e.server?.Equals(ew.server, StringComparison.OrdinalIgnoreCase) ?? false)
//                                 .Where(e => e.message.Equals(ew.message, StringComparison.OrdinalIgnoreCase))
//                                 .FirstOrDefault();


//                 }

//                 // group recent timeouts (to the same server) together under common parent as a bunch of timeouts tend to occur shortly after each other
//                 if (parent == null && ew.sqlErrorType.HasValue && ew.sqlErrorType.Value == SqlErrorType.Timeout)
//                 {
//                     DateTime thresholdDate = DateTime.Now.AddMinutes(-2.0); // look for last 2mins

//                     parent = exceptionDict[listKey]
//                                               .Where(e => e.created >= thresholdDate)
//                                               .Where(e => e.server?.Equals(ew.server, StringComparison.OrdinalIgnoreCase) ?? false)
//                                               .Where(e => e.sqlErrorType.HasValue && e.sqlErrorType.Value == SqlErrorType.Timeout)
//                                               .FirstOrDefault();
//                 }

//                 if (parent == null) exceptionDict[listKey].Add(ew);
//                 else parent.AddRelated(ew);

//                 // TODO: Really save on each exception logged? Perhaps off-load the work to a BG thread ..lazy save
//                 SaveToFile();

//                 return ew.sId;
//             }

//         }
//     }




// }