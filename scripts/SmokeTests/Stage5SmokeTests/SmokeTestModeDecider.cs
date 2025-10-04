using System;
using System.Collections.Generic;
using TlaPlugin.Configuration;

public static class SmokeTestModeDecider
{

    public static SmokeTestModeDecision Decide(PluginOptions options, IReadOnlyDictionary<string, string?> parameters)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        var baseUrlProvided = parameters.TryGetValue("baseUrl", out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl);
        var remoteRequested = parameters.ContainsKey("use-remote-api") || parameters.ContainsKey("use-remote");
        var localStubRequested = parameters.ContainsKey("use-local-stub") || parameters.ContainsKey("force-local");

        var autoConditionMet = !options.Security.UseHmacFallback || baseUrlProvided;
        var useRemoteApi = (remoteRequested || autoConditionMet) && !localStubRequested;
        var isAutomatic = autoConditionMet && !remoteRequested && !localStubRequested;

        string? reason = null;
        if (localStubRequested && (remoteRequested || autoConditionMet))
        {
            reason = "已指定 --use-local-stub，保持本地 Stub";
        }
        else if (remoteRequested)
        {
            reason = "检测到 --use-remote-api 参数";
        }
        else if (!options.Security.UseHmacFallback)
        {
            reason = "配置中已禁用 UseHmacFallback";
        }
        else if (baseUrlProvided)
        {
            reason = "检测到 --baseUrl 参数";
        }

        return new SmokeTestModeDecision
        {
            UseRemoteApi = useRemoteApi,
            AutoConditionMet = autoConditionMet,
            BaseUrlProvided = baseUrlProvided,
            LocalStubRequested = localStubRequested,
            RemoteFlagProvided = remoteRequested,
            IsAutomatic = isAutomatic,
            Reason = reason
        };
    }
}

public sealed record SmokeTestModeDecision
{
    public bool UseRemoteApi { get; init; }

    public bool AutoConditionMet { get; init; }

    public bool BaseUrlProvided { get; init; }

    public bool LocalStubRequested { get; init; }

    public bool RemoteFlagProvided { get; init; }

    public bool IsAutomatic { get; init; }

    public string? Reason { get; init; }
}
