using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;

namespace jsdal_server_core
{
    public class BlobStore : jsdal_plugin.BlobStoreBase
    {
        private BlobStore()
        {

        }

        private static MemoryCache Cache = new MemoryCache(new MemoryCacheOptions()); // new NodeCache({ stdTTL/*seconds*/: 60 * 5 }); // TODO: Make expiration configurable

        private static BlobStore _instance = new BlobStore();

        public static BlobStore Instance { get { return _instance; } }

        public override bool Add(byte[] data, out string key)
        {
            key = shortid.ShortId.Generate(useNumbers: true, useSpecial: false, length: 6);
            BlobStore.Cache.Set<byte[]>(key, data, DateTime.Now.AddMinutes(10));  // TODO: Make expiration configurable
            return true;
        }

        public static bool Exists(string key)
        {
            return BlobStore.Cache.TryGetValue(key, out _);
        }

        // public static stats(): NodeCache.Stats {
        //     return BlobStore.Cache.getStats();
        // }

        public static byte[] Get(string key)
        {
            return BlobStore.Cache.Get<byte[]>(key);
        }


        // TODO: Provide an explicit "release" - so once we've uploaded the blob and the client knows it is done with it, call release/dispose
        // TODO: AutoDestroy - When referencing a blob in a sproc call the sproc can release it automatically if some switched is specified!
    }
}