﻿using System.Text.Json;
using FusionZone.Abstractions;
using IdGen;
using RickDotNet.Base;
using RickDotNet.Extensions.Base;
using Tenray.ZoneTree;
using Tenray.ZoneTree.Comparers;
using Tenray.ZoneTree.Serializers;

namespace FusionZone.Stores.ZoneTree;

public class ZoneStore<TKey> : DataStore<TKey>
{
    private readonly IZoneTree<TKey, string> zoneTree;
    private readonly ZoneIndex<TKey> zoneIndex;
    private readonly IIdGenerator<TKey> idGenerator;

    public ZoneStore(ZoneStoreConfig storeConfig, IIdGenerator<TKey> idGenerator)
    {
        this.idGenerator = idGenerator;
        
        zoneTree = ZoneTreeFactory.Create<TKey>(storeConfig);
        zoneIndex = ZoneIndex.Create<TKey>(storeConfig);
    }

    public override ValueTask<Result<TData>> Get<TData>(TKey id, CancellationToken token)
    {
        if (!zoneTree.TryGet(id, out var json))
            return ValueTask.FromResult(Result.Failure<TData>(new Exception("Item not found")));

        var value = JsonSerializer.Deserialize<TData>(json);
        var result = value == null
            ? Result.Failure<TData>(new Exception("Item not found"))
            : Result.Success(value);

        return ValueTask.FromResult(result);
    }

    public ValueTask<IEnumerable<Result<TData>>> Get<TData>(TKey[] ids, CancellationToken token)
    {
        var results = GetManyInternal<TData>(ids);
        return ValueTask.FromResult(results);
    }

    private IEnumerable<Result<TData>> GetManyInternal<TData>(IEnumerable<TKey> ids)
    {
        //zoneTree.
        foreach (var id in ids)
        {
            if (!zoneTree.TryGet(id, out var json)) continue;

            var result = JsonSerializer.Deserialize<TData>(json);

            if (result != null)
                yield return Result.Success(result);
            else
                yield return Result.Failure<TData>(new Exception("Item not found"));
        }
    }

    public override async ValueTask<(Result<TData> result, TKey id)> Insert<TData>(TData data, CancellationToken token)
    {
        var id = data switch { IHaveId<TKey> hasId => hasId.Id, _ => idGenerator.CreateId() };
        var getResult = await Get<TData>(id, token);
        if (getResult)
            return (Result.Failure<TData>(new Exception("Item already exists")), id);

        var result = await Save<TData>(id, data, token);
        return (result, id);
    }

    public override ValueTask<Result<TData>> Save<TData>(TKey id, TData data, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(data);
        zoneTree.Upsert(id, json);

        return ValueTask.FromResult(Result.Success(data));
    }

    public override async ValueTask<Result<TData>> Delete<TData>(TKey id, CancellationToken token)
    {
        var itemToDelete = await Get<TData>(id, token);
        var result = itemToDelete.Select(x =>
        {
            zoneTree.TryDelete(id, out _);
            return x;
        });

        return result;
    }

    protected override ValueTask<IEnumerable<TKey>> GetAllIds<TData>(CancellationToken token)
    {
        // determine strategy for this
        return ValueTask.FromResult(zoneIndex.GetAllIds<TData>(token));
    }
}