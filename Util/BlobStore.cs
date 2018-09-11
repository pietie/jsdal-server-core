using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;

namespace jsdal_server_core
{
    public class BlobStore {
        private static MemoryCache Cache = new MemoryCache(new MemoryCacheOptions()); // new NodeCache({ stdTTL/*seconds*/: 60 * 5 }); // TODO: Make expiration configurable

        public static bool Add(string key, byte[] data) {
             BlobStore.Cache.Set<byte[]>(key, data, DateTime.Now.AddMinutes(5));  // TODO: Make expiration configurable
             return true;
        }

        public static bool Exists(string key)  {
            return BlobStore.Cache.TryGetValue(key, out _);
        }

        // public static stats(): NodeCache.Stats {
        //     return BlobStore.Cache.getStats();
        // }

        public static byte[] Get(string key) {
            return BlobStore.Cache.Get<byte[]>(key);
        }
    }
}