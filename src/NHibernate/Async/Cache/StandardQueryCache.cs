﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by AsyncGenerator.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------


using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NHibernate.Cfg;
using NHibernate.Engine;
using NHibernate.Type;
using NHibernate.Util;

namespace NHibernate.Cache
{
	using System.Threading.Tasks;
	using System.Threading;
	public partial class StandardQueryCache : IQueryCache, IBatchableQueryCache
	{

		#region IQueryCache Members

		public Task ClearAsync(CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<object>(cancellationToken);
			}
			return Cache.ClearAsync(cancellationToken);
		}

		/// <inheritdoc />
		public Task<bool> PutAsync(
			QueryKey key,
			QueryParameters queryParameters,
			ICacheAssembler[] returnTypes,
			IList result,
			ISessionImplementor session, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<bool>(cancellationToken);
			}
			// 6.0 TODO: inline the call.
#pragma warning disable 612
			return PutAsync(key, returnTypes, result, queryParameters.NaturalKeyLookup, session, cancellationToken);
#pragma warning restore 612
		}

		// Since 5.2
		[Obsolete]
		public async Task<bool> PutAsync(QueryKey key, ICacheAssembler[] returnTypes, IList result, bool isNaturalKeyLookup, ISessionImplementor session, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (isNaturalKeyLookup && result.Count == 0)
				return false;

			var ts = session.Factory.Settings.CacheProvider.NextTimestamp();

			Log.Debug("caching query results in region: '{0}'; {1}", _regionName, key);

			await (Cache.PutAsync(key, await (GetCacheableResultAsync(returnTypes, session, result, ts, cancellationToken)).ConfigureAwait(false), cancellationToken)).ConfigureAwait(false);

			return true;
		}

		/// <inheritdoc />
		public async Task<IList> GetAsync(
			QueryKey key,
			QueryParameters queryParameters,
			ICacheAssembler[] returnTypes,
			ISet<string> spaces,
			ISessionImplementor session, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var persistenceContext = session.PersistenceContext;
			var defaultReadOnlyOrig = persistenceContext.DefaultReadOnly;

			if (queryParameters.IsReadOnlyInitialized)
				persistenceContext.DefaultReadOnly = queryParameters.ReadOnly;
			else
				queryParameters.ReadOnly = persistenceContext.DefaultReadOnly;

			try
			{
				// 6.0 TODO: inline the call.
#pragma warning disable 612
				return await (GetAsync(key, returnTypes, queryParameters.NaturalKeyLookup, spaces, session, cancellationToken)).ConfigureAwait(false);
#pragma warning restore 612
			}
			finally
			{
				persistenceContext.DefaultReadOnly = defaultReadOnlyOrig;
			}
		}

		// Since 5.2
		[Obsolete]
		public async Task<IList> GetAsync(QueryKey key, ICacheAssembler[] returnTypes, bool isNaturalKeyLookup, ISet<string> spaces, ISessionImplementor session, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (Log.IsDebugEnabled())
				Log.Debug("checking cached query results in region: '{0}'; {1}", _regionName, key);

			var cacheable = (IList) await (Cache.GetAsync(key, cancellationToken)).ConfigureAwait(false);
			if (cacheable == null)
			{
				Log.Debug("query results were not found in cache: {0}", key);
				return null;
			}

			var timestamp = (long) cacheable[0];

			if (Log.IsDebugEnabled())
				Log.Debug("Checking query spaces for up-to-dateness [{0}]", StringHelper.CollectionToString(spaces));

			if (!isNaturalKeyLookup && !await (IsUpToDateAsync(spaces, timestamp, cancellationToken)).ConfigureAwait(false))
			{
				Log.Debug("cached query results were not up to date for: {0}", key);
				return null;
			}

			return await (GetResultFromCacheableAsync(key, returnTypes, isNaturalKeyLookup, session, cacheable, cancellationToken)).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<bool[]> PutManyAsync(
			QueryKey[] keys,
			QueryParameters[] queryParameters,
			ICacheAssembler[][] returnTypes,
			IList[] results,
			ISessionImplementor session, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (Log.IsDebugEnabled())
				Log.Debug("caching query results in region: '{0}'; {1}", _regionName, StringHelper.CollectionToString(keys));

			var cached = new bool[keys.Length];
			var ts = session.Factory.Settings.CacheProvider.NextTimestamp();
			var cachedKeys = new List<object>();
			var cachedResults = new List<object>();
			for (var i = 0; i < keys.Length; i++)
			{
				var result = results[i];
				if (queryParameters[i].NaturalKeyLookup && result.Count == 0)
					continue;

				cached[i] = true;
				cachedKeys.Add(keys[i]);
				cachedResults.Add(await (GetCacheableResultAsync(returnTypes[i], session, result, ts, cancellationToken)).ConfigureAwait(false));
			}

			await (_cache.PutManyAsync(cachedKeys.ToArray(), cachedResults.ToArray(), cancellationToken)).ConfigureAwait(false);

			return cached;
		}

		/// <inheritdoc />
		public async Task<IList[]> GetManyAsync(
			QueryKey[] keys,
			QueryParameters[] queryParameters,
			ICacheAssembler[][] returnTypes,
			ISet<string>[] spaces,
			ISessionImplementor session, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (Log.IsDebugEnabled())
				Log.Debug("checking cached query results in region: '{0}'; {1}", _regionName, StringHelper.CollectionToString(keys));

			var cacheables = (await (_cache.GetManyAsync(keys, cancellationToken)).ConfigureAwait(false)).Cast<IList>().ToArray();

			var spacesToCheck = new List<ISet<string>>();
			var checkedSpacesIndexes = new HashSet<int>();
			var checkedSpacesTimestamp = new List<long>();
			for (var i = 0; i < keys.Length; i++)
			{
				var cacheable = cacheables[i];
				if (cacheable == null)
				{
					Log.Debug("query results were not found in cache: {0}", keys[i]);
					continue;
				}

				var querySpaces = spaces[i];
				if (queryParameters[i].NaturalKeyLookup || querySpaces.Count == 0)
					continue;

				spacesToCheck.Add(querySpaces);
				checkedSpacesIndexes.Add(i);
				// The timestamp is the first element of the cache result.
				checkedSpacesTimestamp.Add((long) cacheable[0]);
				if (Log.IsDebugEnabled())
					Log.Debug("Checking query spaces for up-to-dateness [{0}]", StringHelper.CollectionToString(querySpaces));
			}

			var upToDates = spacesToCheck.Count > 0
				? await (_updateTimestampsCache.AreUpToDateAsync(spacesToCheck.ToArray(), checkedSpacesTimestamp.ToArray(), cancellationToken)).ConfigureAwait(false)
				: Array.Empty<bool>();

			var upToDatesIndex = 0;
			var persistenceContext = session.PersistenceContext;
			var defaultReadOnlyOrig = persistenceContext.DefaultReadOnly;
			var results = new IList[keys.Length];
			for (var i = 0; i < keys.Length; i++)
			{
				var cacheable = cacheables[i];
				if (cacheable == null)
					continue;

				var key = keys[i];
				if (checkedSpacesIndexes.Contains(i) && !upToDates[upToDatesIndex++])
				{
					Log.Debug("cached query results were not up to date for: {0}", key);
					continue;
				}

				var queryParams = queryParameters[i];
				if (queryParams.IsReadOnlyInitialized)
					persistenceContext.DefaultReadOnly = queryParams.ReadOnly;
				else
					queryParams.ReadOnly = persistenceContext.DefaultReadOnly;

				// Adjust the session cache mode, as GetResultFromCacheable assemble types which may cause
				// entity loads, which may interact with the cache.
				using (session.SwitchCacheMode(queryParams.CacheMode))
				{
					try
					{
						results[i] = await (GetResultFromCacheableAsync(
							key,
							returnTypes[i],
							queryParams.NaturalKeyLookup,
							session,
							cacheable, cancellationToken)).ConfigureAwait(false);
					}
					finally
					{
						persistenceContext.DefaultReadOnly = defaultReadOnlyOrig;
					}
				}
			}

			return results;
		}

		#endregion

		private static async Task<List<object>> GetCacheableResultAsync(
			ICacheAssembler[] returnTypes,
			ISessionImplementor session,
			IList result,
			long ts, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var cacheable = new List<object>(result.Count + 1) { ts };
			foreach (var row in result)
			{
				if (returnTypes.Length == 1)
				{
					cacheable.Add(await (returnTypes[0].DisassembleAsync(row, session, null, cancellationToken)).ConfigureAwait(false));
				}
				else
				{
					cacheable.Add(await (TypeHelper.DisassembleAsync((object[])row, returnTypes, null, session, null, cancellationToken)).ConfigureAwait(false));
				}
			}

			return cacheable;
		}

		private async Task<IList> GetResultFromCacheableAsync(
			QueryKey key,
			ICacheAssembler[] returnTypes,
			bool isNaturalKeyLookup,
			ISessionImplementor session,
			IList cacheable, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Log.Debug("returning cached query results for: {0}", key);
			if (key.ResultTransformer?.AutoDiscoverTypes == true && cacheable.Count > 0)
			{
				returnTypes = GuessTypes(cacheable);
			}

			try
			{
				var result = new List<object>(cacheable.Count - 1);
				if (returnTypes.Length == 1)
				{
					var returnType = returnTypes[0];

					// Skip first element, it is the timestamp
					var rows = new List<object>(cacheable.Count - 1);
					for (var i = 1; i < cacheable.Count; i++)
					{
						rows.Add(cacheable[i]);
					}

					foreach (var row in rows)
					{
						await (returnType.BeforeAssembleAsync(row, session, cancellationToken)).ConfigureAwait(false);
					}

					foreach (var row in rows)
					{
						result.Add(await (returnType.AssembleAsync(row, session, null, cancellationToken)).ConfigureAwait(false));
					}
				}
				else
				{
					// Skip first element, it is the timestamp
					var rows = new List<object[]>(cacheable.Count - 1);
					for (var i = 1; i < cacheable.Count; i++)
					{
						rows.Add((object[]) cacheable[i]);
					}

					foreach (var row in rows)
					{
						await (TypeHelper.BeforeAssembleAsync(row, returnTypes, session, cancellationToken)).ConfigureAwait(false);
					}

					foreach (var row in rows)
					{
						result.Add(await (TypeHelper.AssembleAsync(row, returnTypes, session, null, cancellationToken)).ConfigureAwait(false));
					}
				}

				return result;
			}
			catch (UnresolvableObjectException ex)
			{
				if (isNaturalKeyLookup)
				{
					//TODO: not really completely correct, since
					//      the UnresolvableObjectException could occur while resolving
					//      associations, leaving the PC in an inconsistent state
					Log.Debug(ex, "could not reassemble cached result set");
					// Handling a RemoveMany here does not look worth it, as this case short-circuits
					// the result-set. So a Many could only benefit batched queries, and only if many
					// of them are natural key lookup with an unresolvable object case.
					await (Cache.RemoveAsync(key, cancellationToken)).ConfigureAwait(false);
					return null;
				}

				throw;
			}
		}

		protected virtual Task<bool> IsUpToDateAsync(ISet<string> spaces, long timestamp, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<bool>(cancellationToken);
			}
			return _updateTimestampsCache.IsUpToDateAsync(spaces, timestamp, cancellationToken);
		}
	}
}
