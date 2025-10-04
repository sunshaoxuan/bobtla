using System;
using System.Collections.Generic;
using TlaPlugin.Configuration;

internal readonly record struct RemoteApiDecision(
    bool UseRemoteApi,
    bool IsAutomatic,
    string? Reason,
    bool LocalStubRequested,
    bool AutoConditionMet,
    bool BaseUrlProvided);

internal static class SmokeTestModeDecider
{
    internal static RemoteApiDecision Decide(PluginOptions options, IReadOnlyDictionary<string, string?> parameters)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        var localStubRequested = parameters.ContainsKey("use-local-stub");
        var remoteRequested = parameters.ContainsKey("use-remote-api");
        _ = parameters.TryGetValue("baseUrl", out var baseUrlRaw);
        var baseUrlProvided = !string.IsNullOrWhiteSpace(baseUrlRaw);
        var autoConditionMet = baseUrlProvided || !options.Security.UseHmacFallback;

        if (localStubRequested)
        {
            return new RemoteApiDecision(
                UseRemoteApi: false,
                IsAutomatic: false,
                Reason: "--use-local-stub 已覆盖远程模式",
                LocalStubRequested: true,
                AutoConditionMet: autoConditionMet,
                BaseUrlProvided: baseUrlProvided);
        }

        if (remoteRequested)
        {
            return new RemoteApiDecision(
                UseRemoteApi: true,
                IsAutomatic: false,
                Reason: "--use-remote-api 已启用远程模式",
                LocalStubRequested: false,
                AutoConditionMet: autoConditionMet,
                BaseUrlProvided: baseUrlProvided);
        }

        if (autoConditionMet)
        {
            var reason = baseUrlProvided
                ? "检测到 --baseUrl 参数"
                : "配置中已禁用 UseHmacFallback";
            return new RemoteApiDecision(
                UseRemoteApi: true,
                IsAutomatic: true,
                Reason: reason,
                LocalStubRequested: false,
                AutoConditionMet: true,
                BaseUrlProvided: baseUrlProvided);
        }

        return new RemoteApiDecision(
            UseRemoteApi: false,
            IsAutomatic: false,
            Reason: null,
            LocalStubRequested: false,
            AutoConditionMet: false,
            BaseUrlProvided: baseUrlProvided);
    }
}
