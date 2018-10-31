﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by AsyncGenerator.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------


using System;
using NHibernate.Cache;
using NHibernate.Collection;
using NHibernate.Persister.Collection;
using NHibernate.Persister.Entity;
using NHibernate.Util;
using System.Collections.Generic;
using Iesi.Collections.Generic;

namespace NHibernate.Engine
{
	using System.Threading.Tasks;
	using System.Threading;
	public partial class BatchFetchQueue
	{

		/// <summary>
		/// Get a batch of uninitialized collection keys for a given role
		/// </summary>
		/// <param name="collectionPersister">The persister for the collection role.</param>
		/// <param name="id">A key that must be included in the batch fetch</param>
		/// <param name="batchSize">the maximum number of keys to return</param>
		/// <param name="cancellationToken">A cancellation token that can be used to cancel the work</param>
		/// <returns>an array of collection keys, of length batchSize (padded with nulls)</returns>
		public Task<object[]> GetCollectionBatchAsync(ICollectionPersister collectionPersister, object id, int batchSize, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<object[]>(cancellationToken);
			}
			return GetCollectionBatchAsync(collectionPersister, id, batchSize, true, null, cancellationToken);
		}

		/// <summary>
		/// Get a batch of uninitialized collection keys for a given role
		/// </summary>
		/// <param name="collectionPersister">The persister for the collection role.</param>
		/// <param name="key">A key that must be included in the batch fetch</param>
		/// <param name="batchSize">the maximum number of keys to return</param>
		/// <param name="checkCache">Whether to check the cache for uninitialized collection keys.</param>
		/// <param name="collectionEntries">An array that will be filled with collection entries if set.</param>
		/// <param name="cancellationToken">A cancellation token that can be used to cancel the work</param>
		/// <returns>An array of collection keys, of length <paramref name="batchSize"/> (padded with nulls)</returns>
		internal async Task<object[]> GetCollectionBatchAsync(ICollectionPersister collectionPersister, object key, int batchSize, bool checkCache,
		                                     CollectionEntry[] collectionEntries, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var keys = new object[batchSize];
			keys[0] = key; // The first element of array is reserved for the actual instance we are loading
			var i = 1; // The current index of keys array
			int? keyIndex = null; // The index of the demanding key in the linked hash set
			var checkForEnd = false; // Stores whether we found the demanded collection and reached the batchSize
			var index = 0; // The current index of the linked hash set iteration
			// List of collection entries that haven't been checked for their existance in the cache. Besides the collection entry,
			// the index where the entry was found is also stored in order to correctly order the returning keys.
			var collectionKeys = new List<KeyValuePair<KeyValuePair<CollectionEntry, IPersistentCollection>, int>>(batchSize);
			var batchableCache = collectionPersister.Cache?.GetCacheBase();

			if (!batchLoadableCollections.TryGetValue(collectionPersister.Role, out var map))
			{
				return keys;
			}

			foreach (KeyValuePair<CollectionEntry, IPersistentCollection> me in map)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await (ProcessKeyAsync(me)).ConfigureAwait(false))
				{
					return keys;
				}
				index++;
			}

			// If by the end of the iteration we haven't filled the whole array of keys to fetch,
			// we have to check the remaining collection keys.
			while (i != batchSize && collectionKeys.Count > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await (CheckCacheAndProcessResultAsync()).ConfigureAwait(false))
				{
					return keys;
				}
			}

			return keys; //we ran out of keys to try

			// Calls the cache to check if any of the keys is cached and continues the key processing for those
			// that are not stored in the cache.
			async Task<bool> CheckCacheAndProcessResultAsync()
			{
				var fromIndex = batchableCache != null
					? collectionKeys.Count - Math.Min(batchSize, collectionKeys.Count)
					: 0;
				var toIndex = collectionKeys.Count - 1;
				var indexes = GetSortedKeyIndexes(collectionKeys, keyIndex.Value, fromIndex, toIndex);
				if (batchableCache == null)
				{
					for (var j = 0; j < collectionKeys.Count; j++)
					{
						if (await (ProcessKeyAsync(collectionKeys[indexes[j]].Key)).ConfigureAwait(false))
						{
							return true;
						}
					}
				}
				else
				{
					var results = await (AreCachedAsync(collectionKeys, indexes, collectionPersister, batchableCache, checkCache, cancellationToken)).ConfigureAwait(false);
					var k = toIndex;
					for (var j = 0; j < results.Length; j++)
					{
						if (!results[j] && await (ProcessKeyAsync(collectionKeys[indexes[j]].Key, true)).ConfigureAwait(false))
						{
							return true;
						}
					}
				}

				for (var j = toIndex; j >= fromIndex; j--)
				{
					collectionKeys.RemoveAt(j);
				}
				return false;
			}

			Task<bool> ProcessKeyAsync(KeyValuePair<CollectionEntry, IPersistentCollection> me, bool ignoreCache = false)
			{
				var ce = me.Key;
				var collection = me.Value;
				if (ce.LoadedKey == null)
				{
					// the LoadedKey of the CollectionEntry might be null as it might have been reset to null
					// (see for example Collections.ProcessDereferencedCollection()
					// and CollectionEntry.AfterAction())
					// though we clear the queue on flush, it seems like a good idea to guard
					// against potentially null LoadedKey:s
					return Task.FromResult<bool>(false);
				}

				if (collection.WasInitialized)
				{
					log.Warn("Encountered initialized collection in BatchFetchQueue, this should not happen.");
					return Task.FromResult<bool>(false);
				}

				if (checkForEnd && (index >= keyIndex.Value + batchSize || index == map.Count))
				{
					return Task.FromResult<bool>(true);
				}
				if (collectionPersister.KeyType.IsEqual(key, ce.LoadedKey, collectionPersister.Factory))
				{
					if (collectionEntries != null)
					{
						collectionEntries[0] = ce;
					}
					keyIndex = index;
				}
				else if (!checkCache || batchableCache == null)
				{
					if (!keyIndex.HasValue || index < keyIndex.Value)
					{
						collectionKeys.Add(new KeyValuePair<KeyValuePair<CollectionEntry, IPersistentCollection>, int>(me, index));
						return Task.FromResult<bool>(false);
					}

					// No need to check "!checkCache || !IsCached(ce.LoadedKey, collectionPersister)":
					// "batchableCache == null" already means there is no cache, so IsCached can only yield false.
					// (This method is now removed.)
					if (collectionEntries != null)
					{
						collectionEntries[i] = ce;
					}
					keys[i++] = ce.LoadedKey;
				}
				else if (ignoreCache)
				{
					if (collectionEntries != null)
					{
						collectionEntries[i] = ce;
					}
					keys[i++] = ce.LoadedKey;
				}
				else
				{
					collectionKeys.Add(new KeyValuePair<KeyValuePair<CollectionEntry, IPersistentCollection>, int>(me, index));
					// Check the cache only when we have collected as many keys as are needed to fill the batch,
					// that are after the demanded key.
					if (!keyIndex.HasValue || index < keyIndex.Value + batchSize)
					{
						return Task.FromResult<bool>(false);
					}
					return CheckCacheAndProcessResultAsync();
				}
				if (i == batchSize)
				{
					i = 1; // End of array, start filling again from start
					if (keyIndex.HasValue)
					{
						checkForEnd = true;
						return Task.FromResult<bool>(index >= keyIndex.Value + batchSize || index == map.Count);
					}
				}
				return Task.FromResult<bool>(false);
			}
		}

		/// <summary>
		/// Get a batch of unloaded identifiers for this class, using a slightly
		/// complex algorithm that tries to grab keys registered immediately after
		/// the given key.
		/// </summary>
		/// <param name="persister">The persister for the entities being loaded.</param>
		/// <param name="id">The identifier of the entity currently demanding load.</param>
		/// <param name="batchSize">The maximum number of keys to return</param>
		/// <param name="cancellationToken">A cancellation token that can be used to cancel the work</param>
		/// <returns>an array of identifiers, of length batchSize (possibly padded with nulls)</returns>
		public Task<object[]> GetEntityBatchAsync(IEntityPersister persister, object id, int batchSize, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<object[]>(cancellationToken);
			}
			return GetEntityBatchAsync(persister, id, batchSize, true, cancellationToken);
		}

		/// <summary>
		/// Get a batch of unloaded identifiers for this class, using a slightly
		/// complex algorithm that tries to grab keys registered immediately after
		/// the given key.
		/// </summary>
		/// <param name="persister">The persister for the entities being loaded.</param>
		/// <param name="id">The identifier of the entity currently demanding load.</param>
		/// <param name="batchSize">The maximum number of keys to return</param>
		/// <param name="checkCache">Whether to check the cache for uninitialized keys.</param>
		/// <param name="cancellationToken">A cancellation token that can be used to cancel the work</param>
		/// <returns>An array of identifiers, of length <paramref name="batchSize"/> (possibly padded with nulls)</returns>
		internal async Task<object[]> GetEntityBatchAsync(IEntityPersister persister, object id, int batchSize, bool checkCache, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var ids = new object[batchSize];
			ids[0] = id; // The first element of array is reserved for the actual instance we are loading
			var i = 1; // The current index of ids array
			int? idIndex = null; // The index of the demanding id in the linked hash set
			var checkForEnd = false; // Stores whether we found the demanded id and reached the batchSize
			var index = 0; // The current index of the linked hash set iteration
			// List of entity keys that haven't been checked for their existance in the cache. Besides the entity key,
			// the index where the key was found is also stored in order to correctly order the returning keys.
			var entityKeys = new List<KeyValuePair<EntityKey, int>>(batchSize);
			// If there is a cache, obsolete or not, batchableCache will not be null.
			var batchableCache = persister.Cache?.GetCacheBase();

			if (!batchLoadableEntityKeys.TryGetValue(persister.EntityName, out var set))
			{
				return ids;
			}

			foreach (var key in set)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await (ProcessKeyAsync(key)).ConfigureAwait(false))
				{
					return ids;
				}
				index++;
			}

			// If by the end of the iteration we haven't filled the whole array of ids to fetch,
			// we have to check the remaining entity keys.
			while (i != batchSize && entityKeys.Count > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await (CheckCacheAndProcessResultAsync()).ConfigureAwait(false))
				{
					return ids;
				}
			}

			return ids;

			// Calls the cache to check if any of the keys is cached and continues the key processing for those
			// that are not stored in the cache.
			async Task<bool> CheckCacheAndProcessResultAsync()
			{
				var fromIndex = batchableCache != null
					? entityKeys.Count - Math.Min(batchSize, entityKeys.Count)
					: 0;
				var toIndex = entityKeys.Count - 1;
				var indexes = GetSortedKeyIndexes(entityKeys, idIndex.Value, fromIndex, toIndex);
				if (batchableCache == null)
				{
					for (var j = 0; j < entityKeys.Count; j++)
					{
						if (await (ProcessKeyAsync(entityKeys[indexes[j]].Key)).ConfigureAwait(false))
						{
							return true;
						}
					}
				}
				else
				{
					var results = await (AreCachedAsync(entityKeys, indexes, persister, batchableCache, checkCache, cancellationToken)).ConfigureAwait(false);
					var k = toIndex;
					for (var j = 0; j < results.Length; j++)
					{
						if (!results[j] && await (ProcessKeyAsync(entityKeys[indexes[j]].Key, true)).ConfigureAwait(false))
						{
							return true;
						}
					}
				}

				for (var j = toIndex; j >= fromIndex; j--)
				{
					entityKeys.RemoveAt(j);
				}
				return false;
			}

			Task<bool> ProcessKeyAsync(EntityKey key, bool ignoreCache = false)
			{
				//TODO: this needn't exclude subclasses...
				if (checkForEnd && (index >= idIndex.Value + batchSize || index == set.Count))
				{
					return Task.FromResult<bool>(true);
				}
				if (persister.IdentifierType.IsEqual(id, key.Identifier))
				{
					idIndex = index;
				}
				else if (!checkCache || batchableCache == null)
				{
					if (!idIndex.HasValue || index < idIndex.Value)
					{
						entityKeys.Add(new KeyValuePair<EntityKey, int>(key, index));
						return Task.FromResult<bool>(false);
					}

					// No need to check "!checkCache || !IsCached(key, persister)": "batchableCache == null"
					// already means there is no cache, so IsCached can only yield false. (This method is now
					// removed.)
					ids[i++] = key.Identifier;
				}
				else if (ignoreCache)
				{
					ids[i++] = key.Identifier;
				}
				else
				{
					entityKeys.Add(new KeyValuePair<EntityKey, int>(key, index));
					// Check the cache only when we have collected as many keys as are needed to fill the batch,
					// that are after the demanded key.
					if (!idIndex.HasValue || index < idIndex.Value + batchSize)
					{
						return Task.FromResult<bool>(false);
					}
					return CheckCacheAndProcessResultAsync();
				}
				if (i == batchSize)
				{
					i = 1; // End of array, start filling again from start
					if (idIndex.HasValue)
					{
						checkForEnd = true;
						return Task.FromResult<bool>(index >= idIndex.Value + batchSize || index == set.Count);
					}
				}
				return Task.FromResult<bool>(false);
			}
		}

		/// <summary>
		/// Checks whether the given entity key indexes are cached.
		/// </summary>
		/// <param name="entityKeys">The list of pairs of entity keys and thier indexes.</param>
		/// <param name="keyIndexes">The array of indexes of <paramref name="entityKeys"/> that have to be checked.</param>
		/// <param name="persister">The entity persister.</param>
		/// <param name="batchableCache">The batchable cache.</param>
		/// <param name="checkCache">Whether to check the cache or just return <see langword="false" /> for all keys.</param>
		/// <param name="cancellationToken">A cancellation token that can be used to cancel the work</param>
		/// <returns>An array of booleans that contains the result for each key.</returns>
		private async Task<bool[]> AreCachedAsync(List<KeyValuePair<EntityKey, int>> entityKeys, int[] keyIndexes, IEntityPersister persister,
		                         CacheBase batchableCache, bool checkCache, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var result = new bool[keyIndexes.Length];
			if (!checkCache || !persister.HasCache || !context.Session.CacheMode.HasFlag(CacheMode.Get))
			{
				return result;
			}
			var cacheKeys = new object[keyIndexes.Length];
			var i = 0;
			foreach (var index in keyIndexes)
			{
				var entityKey = entityKeys[index].Key;
				cacheKeys[i++] = context.Session.GenerateCacheKey(
					entityKey.Identifier,
					persister.IdentifierType,
					entityKey.EntityName);
			}
			var cacheResult = await (batchableCache.GetManyAsync(cacheKeys, cancellationToken)).ConfigureAwait(false);
			for (var j = 0; j < result.Length; j++)
			{
				result[j] = cacheResult[j] != null;
			}

			return result;
		}

		/// <summary>
		/// Checks whether the given collection key indexes are cached.
		/// </summary>
		/// <param name="collectionKeys">The list of pairs of collection entries and thier indexes.</param>
		/// <param name="keyIndexes">The array of indexes of <paramref name="collectionKeys"/> that have to be checked.</param>
		/// <param name="persister">The collection persister.</param>
		/// <param name="batchableCache">The batchable cache.</param>
		/// <param name="checkCache">Whether to check the cache or just return <see langword="false" /> for all keys.</param>
		/// <param name="cancellationToken">A cancellation token that can be used to cancel the work</param>
		/// <returns>An array of booleans that contains the result for each key.</returns>
		private async Task<bool[]> AreCachedAsync(List<KeyValuePair<KeyValuePair<CollectionEntry, IPersistentCollection>, int>> collectionKeys,
		                         int[] keyIndexes, ICollectionPersister persister, CacheBase batchableCache,
		                         bool checkCache, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var result = new bool[keyIndexes.Length];
			if (!checkCache || !persister.HasCache || !context.Session.CacheMode.HasFlag(CacheMode.Get))
			{
				return result;
			}
			var cacheKeys = new object[keyIndexes.Length];
			var i = 0;
			foreach (var index in keyIndexes)
			{
				var collectionKey = collectionKeys[index].Key;
				cacheKeys[i++] = context.Session.GenerateCacheKey(
					collectionKey.Key.LoadedKey,
					persister.KeyType,
					persister.Role);
			}
			var cacheResult = await (batchableCache.GetManyAsync(cacheKeys, cancellationToken)).ConfigureAwait(false);
			for (var j = 0; j < result.Length; j++)
			{
				result[j] = cacheResult[j] != null;
			}

			return result;
		}
	}
}
