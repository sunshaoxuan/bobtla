using System;
using System.Collections.Generic;
using System.Linq;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 根据模型配置估算成本与延迟的服务。
/// </summary>
public class CostEstimatorService
{
    private readonly ModelProviderFactory _factory;

    public CostEstimatorService(ModelProviderFactory factory)
    {
        _factory = factory;
    }

    public CostLatencyEstimate Estimate(int payloadSize, string modelId)
    {
        if (payloadSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadSize), "payloadSize は 0 以上である必要があります。");
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("modelId は必須です。", nameof(modelId));
        }

        var provider = _factory.CreateProviders().FirstOrDefault(p => p.Options.Id == modelId);
        if (provider is null)
        {
            throw new KeyNotFoundException($"モデル {modelId} が見つかりません。");
        }

        var cost = decimal.Round(provider.Options.CostPerCharUsd * payloadSize, 6);
        var baseLatency = provider.Options.LatencyTargetMs <= 0 ? 200 : provider.Options.LatencyTargetMs;
        var latency = baseLatency + (int)Math.Min(Math.Max(payloadSize / 50, 0), baseLatency * 3);
        return new CostLatencyEstimate(cost, latency, provider.Options.Id);
    }
}
