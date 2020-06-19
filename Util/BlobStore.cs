using System;
using System.Collections.Generic;
using System.Threading;
using jsdal_plugin;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using System.Linq;

namespace jsdal_server_core
{
    public class BlobStore : BlobStoreBase
    {
        private BlobStore()
        {

        }

        private int _totalItemsInCache = 0;
        private long _totalBytesInCache = 0;

        private static MemoryCache Cache = new MemoryCache(new MemoryCacheOptions()); // new NodeCache({ stdTTL/*seconds*/: 60 * 5 }); // TODO: Make expiration configurable
        private static HashSet<string> CacheKeys = new HashSet<string>();
        private static Dictionary<string/*Ref*/, BlobStoreData> CacheHistory = new Dictionary<string, BlobStoreData>();

        private static BlobStore _instance = new BlobStore();
        public static BlobStore Instance { get { return _instance; } }

        public override bool Add(BlobStoreData data, out string key)
        {
            lock (Cache)
            {
                key = shortid.ShortId.Generate(useNumbers: true, useSpecial: false, length: 6);

                double cacheExpirationInMinutes = 120; // TODO: Make expiration configurable - global config vs Endpoint specific config

                DateTime expiryDate = DateTime.Now.AddMinutes(cacheExpirationInMinutes);

                var expirationToken = new CancellationChangeToken(
                    new CancellationTokenSource(TimeSpan.FromMinutes(cacheExpirationInMinutes + 0.1)).Token); // cancellation token is necessary to force Post Eviction Callback to call on time

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                     .SetPriority(CacheItemPriority.NeverRemove)
                     .AddExpirationToken(expirationToken)
                     .SetAbsoluteExpiration(expiryDate)
                     .RegisterPostEvictionCallback(callback: OnCacheItemExpired, state: this);

                data.ExpiryDate = expiryDate;
                data.Ref = key;

                BlobStore.Cache.Set<BlobStoreData>(key, data, cacheEntryOptions);
                CacheKeys.Add(key);

                Interlocked.Increment(ref _totalItemsInCache);

                data.Size =  (data.Data?.Length ?? 0) + (data.ContentType?.Length ?? 0) + (data.Filename?.Length ?? 0);
                _totalBytesInCache += data.Size;

                return true;
            }
        }

        private static void OnCacheItemExpired(object key, object value, EvictionReason reason, object state)
        {
            lock (Cache)
            {
                BlobStoreData data = (BlobStoreData)value;

                Interlocked.Decrement(ref _instance._totalItemsInCache);
                _instance._totalBytesInCache -= data.Size;

                CacheKeys.Remove((string)key);

                lock (CacheHistory)
                {
                    data.Data = null;
                    CacheHistory.Add((string)key, data);
                }
            }

        }

        public static bool Exists(string key)
        {
            return BlobStore.Cache.TryGetValue(key, out _);
        }

        public BlobStats GetStats()
        {
            return new BlobStats() { TotalItemsInCache = _totalItemsInCache, TotalBytesInCache = _totalBytesInCache };
        }

        public List<BlobStoreData> GetTopN(int topN)
        {
            lock (Cache)
            {
                return CacheKeys.Take(Math.Min(CacheKeys.Count, topN)).Select(k =>
                {
                    return GetBlobByRef(k);
                }).ToList();
            }
        }

        public BlobStoreData GetBlobByRef(string blobRef)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue<BlobStoreData>(blobRef, out var blob))
                {
                    lock (CacheHistory)
                    {
                        if (CacheHistory.ContainsKey(blobRef))
                        {
                            return CacheHistory[blobRef];
                        }
                    }
                }
                else
                {
                    return blob;
                }
            }

            return null;
        }

        public static BlobStoreData Get(string key)
        {
            return BlobStore.Cache.Get<BlobStoreData>(key);
        }


        // TODO: Provide an explicit "release" - so once we've uploaded the blob and the client knows it is done with it, call release/dispose
        // TODO: AutoDestroy - When referencing a blob in a sproc call the sproc can release it automatically if some switched is specified!
    }

    public class BlobStats
    {
        public int TotalItemsInCache;
        public long TotalBytesInCache;
    }
}