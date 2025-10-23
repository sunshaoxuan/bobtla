using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Services;
using TlaPlugin.Models;

internal static class Program
{
    private const string DefaultAppsettingsPath = "src/TlaPlugin/appsettings.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    internal static Func<PluginOptions, KeyVaultSecretResolver, LiveModelSmokeHarness> LiveModelHarnessFactory { get; set; }
        = (options, resolver) => new LiveModelSmokeHarness(options, secretResolver: resolver);

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintRootHelp();
            return 0;
        }

        if (IsHelpFlag(args[0]))
        {
            PrintRootHelp();
            return 0;
        }

        var command = args[0].Trim();
        var commandArgs = args.Skip(1).ToArray();

        try
        {
            return command.ToLowerInvariant() switch
            {
                "secrets" => RunSecrets(commandArgs),
                "reply" => RunReply(commandArgs),
                "metrics" => RunMetrics(commandArgs),
                "ready" or "mark-ready" => RunReady(commandArgs),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (SecretRetrievalException ex)
        {
            WriteError($"密钥解析失败：{ex.Message}");
            return 2;
        }
        catch (AuthenticationException ex)
        {
            WriteError($"认证失败：{ex.Message}");
            return 3;
        }
        catch (HttpRequestException ex)
        {
            WriteError($"HTTP 请求失败：{ex.Message}");
            return 21;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            WriteError($"命令执行失败：{ex.Message}");
            return 1;
        }
    }

    private static int HandleUnknownCommand(string command)
    {
        WriteError($"未知命令: {command}");
        Console.WriteLine();
        PrintRootHelp();
        return 1;
    }

    private static int RunSecrets(string[] args)
    {
        var overrides = new List<string>();
        var tenants = new List<string>();
        string? appsettings = null;
        var verifyReadiness = false;

        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (IsHelpFlag(current))
            {
                PrintSecretsHelp();
                return 0;
            }

            switch (current)
            {
                case "--appsettings":
                    appsettings = RequireValue(args, ref i);
                    break;
                case "--override":
                    overrides.Add(RequireValue(args, ref i));
                    break;
                case "--tenant":
                    tenants.Add(RequireValue(args, ref i));
                    break;
                case "--verify-readiness":
                    verifyReadiness = true;
                    break;
                default:
                    throw new ArgumentException($"未知参数 {current}");
            }
        }

        var options = LoadOptions(appsettings, overrides);
        var resolver = new KeyVaultSecretResolver(Options.Create(options));

        var providerSecretGroups = options.Providers
            .Where(p => !string.IsNullOrWhiteSpace(p.ApiKeySecretName))
            .GroupBy(p => p.ApiKeySecretName!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var secretNames = new HashSet<string>(CollectSecretNames(options), StringComparer.OrdinalIgnoreCase);
        foreach (var providerGroup in providerSecretGroups)
        {
            secretNames.Remove(providerGroup.Key);
        }
        var tenantsToCheck = tenants.Count > 0
            ? tenants
            : CollectTenantIds(options);

        var hasSecretFailures = false;
        var hasSecretWarnings = false;

        Console.WriteLine("🤖 模型 Provider 凭据检查：");
        if (providerSecretGroups.Count == 0)
        {
            Console.WriteLine("  ⚠ 未配置任何 ApiKeySecretName，跳过模型密钥检查。");
        }
        else
        {
            foreach (var group in providerSecretGroups)
            {
                var providerIds = string.Join(", ", group.Select(p => p.Id));
                var displayName = $"{providerIds} :: {group.Key}";
                var outcome = ReportSecret(resolver, group.Key, tenantId: null, displayName);
                hasSecretFailures |= !outcome.Success;
                hasSecretWarnings |= outcome.Warning;
            }
        }

        Console.WriteLine();
        Console.WriteLine("🔐 正在检查 Key Vault 机密解析状态：");
        foreach (var secret in secretNames)
        {
            if (tenantsToCheck.Count == 0)
            {
                var outcome = ReportSecret(resolver, secret, tenantId: null);
                hasSecretFailures |= !outcome.Success;
                hasSecretWarnings |= outcome.Warning;
                continue;
            }

            foreach (var tenant in tenantsToCheck)
            {
                var outcome = ReportSecret(resolver, secret, tenant);
                hasSecretFailures |= !outcome.Success;
                hasSecretWarnings |= outcome.Warning;
            }
        }

        Console.WriteLine();
        Console.WriteLine("🔁 HMAC 回退：");
        Console.WriteLine(options.Security.UseHmacFallback
            ? "  ✘ 已启用 UseHmacFallback，Stage 环境需关闭该选项以走 OBO 链路。"
            : "  ✔ 已禁用 UseHmacFallback，使用 AAD/OBO 令牌链路。");

        Console.WriteLine();
        Console.WriteLine("📡 Graph 作用域：");
        foreach (var scope in options.Security.GraphScopes)
        {
            var normalized = scope?.Trim() ?? string.Empty;
            var valid = normalized.StartsWith("https://graph.microsoft.com", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine(valid
                ? $"  ✔ {normalized}"
                : $"  ✘ {normalized} (建议以 https://graph.microsoft.com/.default 或资源限定格式配置)");
        }

        Console.WriteLine();
        ReportStageReadinessFile(options.StageReadinessFilePath, verifyReadiness);

        if (hasSecretWarnings)
        {
            Console.WriteLine();
            Console.WriteLine("⚠️ 发现缺少到期信息的机密，请在 Key Vault 中设置 ExpiresOn 以便自动告警。");
        }

        return hasSecretFailures ? 41 : 0;
    }

    private static int RunReady(string[] args)
    {
        var overrides = new List<string>();
        string? appsettings = null;
        string? overridePath = null;
        DateTimeOffset? timestampOverride = null;

        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (IsHelpFlag(current))
            {
                PrintReadyHelp();
                return 0;
            }

            switch (current)
            {
                case "--appsettings":
                    appsettings = RequireValue(args, ref i);
                    break;
                case "--override":
                    overrides.Add(RequireValue(args, ref i));
                    break;
                case "--path":
                    overridePath = RequireValue(args, ref i);
                    break;
                case "--timestamp":
                    var value = RequireValue(args, ref i);
                    if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                    {
                        throw new ArgumentException($"无法解析时间戳 {value}，请使用 ISO-8601 格式。");
                    }

                    timestampOverride = parsed;
                    break;
                default:
                    throw new ArgumentException($"未知参数 {current}");
            }
        }

        var options = LoadOptions(appsettings, overrides);
        var targetPath = string.IsNullOrWhiteSpace(overridePath) ? options.StageReadinessFilePath : overridePath;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException("未在配置中找到 Plugin.StageReadinessFilePath，无法写入 Stage 就绪文件。可通过 --path 显式指定。");
        }

        var timestamp = timestampOverride ?? DateTimeOffset.UtcNow;
        var store = new FileStageReadinessStore(targetPath!);
        store.WriteLastSuccess(timestamp);

        Console.WriteLine("✅ Stage 就绪文件已更新:");
        Console.WriteLine($"  Path: {Path.GetFullPath(targetPath!)}");
        Console.WriteLine($"  Timestamp: {timestamp:O}");

        return 0;
    }

    internal static int RunReply(string[] args)
    {
        var overrides = new List<string>();
        string? appsettings = null;
        string? tenantId = null;
        string? userId = null;
        string? threadId = null;
        string? channelId = null;
        string? language = null;
        string? tone = null;
        string? text = null;
        string? assertion = null;
        string? baseUrl = null;
        bool useLiveGraph = false;
        bool useLiveModel = false;
        bool useRemoteApi = false;
        bool useLocalStub = false;

        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (IsHelpFlag(current))
            {
                PrintReplyHelp();
                return 0;
            }

            switch (current)
            {
                case "--appsettings":
                    appsettings = RequireValue(args, ref i);
                    break;
                case "--override":
                    overrides.Add(RequireValue(args, ref i));
                    break;
                case "--tenant":
                    tenantId = RequireValue(args, ref i);
                    break;
                case "--user":
                    userId = RequireValue(args, ref i);
                    break;
                case "--thread":
                    threadId = RequireValue(args, ref i);
                    break;
                case "--channel":
                    channelId = RequireValue(args, ref i);
                    break;
                case "--language":
                    language = RequireValue(args, ref i);
                    break;
                case "--tone":
                    tone = RequireValue(args, ref i);
                    break;
                case "--text":
                    text = RequireValue(args, ref i);
                    break;
                case "--assertion":
                    assertion = RequireValue(args, ref i);
                    break;
                case "--baseUrl":
                    baseUrl = RequireValue(args, ref i);
                    break;
                case "--use-live-graph":
                    useLiveGraph = true;
                    break;
                case "--use-live-model":
                    useLiveModel = true;
                    break;
                case "--use-remote-api":
                    useRemoteApi = true;
                    break;
                case "--use-local-stub":
                    useLocalStub = true;
                    break;
                default:
                    throw new ArgumentException($"未知参数 {current}");
            }
        }

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(threadId))
        {
            throw new ArgumentException("--tenant、--user 与 --thread 为必填项。");
        }

        var options = LoadOptions(appsettings, overrides);
        language ??= options.DefaultTargetLanguages.FirstOrDefault() ?? "ja";
        text ??= string.Empty;

        if (string.IsNullOrWhiteSpace(assertion))
        {
            if (!options.Security.UseHmacFallback)
            {
                throw new InvalidOperationException("已禁用 HMAC 回退，请提供 --assertion 以执行真实 OBO 流程。");
            }

            assertion = GenerateMockAssertion(tenantId, userId, options.Security.UserAssertionAudience);
            Console.WriteLine("提示：未提供用户断言，已生成模拟 JWT 以驱动 HMAC 回退流程。");
        }

        var translationTone = tone ?? TranslationRequest.DefaultTone;
        var additionalLanguages = options.DefaultTargetLanguages
            .Where(l => !string.Equals(l, language, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        var translationRequest = new TranslationRequest
        {
            Text = text,
            TargetLanguage = language,
            TenantId = tenantId!,
            UserId = userId!,
            ThreadId = threadId!,
            ChannelId = channelId,
            Tone = translationTone,
            UiLocale = options.DefaultUiLocale,
            UserAssertion = assertion,
            AdditionalTargetLanguages = additionalLanguages
        };

        var decisionParameters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            decisionParameters["baseUrl"] = baseUrl;
        }

        if (useRemoteApi)
        {
            decisionParameters["use-remote-api"] = null;
        }

        if (useLocalStub)
        {
            decisionParameters["use-local-stub"] = null;
        }

        var decision = SmokeTestModeDecider.Decide(options, decisionParameters);
        if (decision.UseRemoteApi)
        {
            var resolvedBaseUrl = baseUrl;
            if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
            {
                throw new InvalidOperationException("未指定 --baseUrl，无法执行远程 API 冒烟。");
            }

            if (!string.IsNullOrWhiteSpace(decision.Reason))
            {
                Console.WriteLine($"[ModeDecider] {decision.Reason}");
            }

            return RemoteReplySmokeRunner
                .RunAsync(resolvedBaseUrl, translationRequest, translationTone, additionalLanguages, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        var resolver = new KeyVaultSecretResolver(Options.Create(options));
        var tokenBroker = new TokenBroker(resolver, Options.Create(options), logger: NullLogger<TokenBroker>.Instance);
        var token = tokenBroker
            .ExchangeOnBehalfOfAsync(tenantId!, userId!, assertion!, cancellationToken: default)
            .GetAwaiter()
            .GetResult();

        Console.WriteLine("[TokenBroker] 调用成功:");
        Console.WriteLine($"  Audience:   {token.Audience}");
        Console.WriteLine($"  ExpiresOn:  {token.ExpiresOn:O}");
        Console.WriteLine($"  Value:      {token.Value.Substring(0, Math.Min(token.Value.Length, 64))}...");

        var mode = useLiveGraph ? "graph" : useLiveModel ? "model" : "stub";
        Console.WriteLine();
        Console.WriteLine("[TeamsReplyClient] 调用诊断:");
        Console.WriteLine($"  Mode:        {mode}");
        Console.WriteLine($"  ThreadId:    {threadId}");
        Console.WriteLine($"  ChannelId:   {channelId ?? "<none>"}");
        Console.WriteLine($"  TenantId:    {tenantId}");
        Console.WriteLine($"  Language:    {language}");
        Console.WriteLine($"  Tone:        {translationTone}");
        Console.WriteLine($"  BaseUrl:     {(baseUrl ?? "(local stub)")}");

        var payload = new
        {
            tenantId,
            userId,
            threadId,
            channelId,
            language,
            tone = translationTone,
            text,
            additionalTranslations = additionalLanguages.Select(l => new
            {
                language = l,
                text = $"{text} ({l})"
            })
        };

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        Console.WriteLine("  Body:");
        Console.WriteLine(payloadJson);

        if (useLiveModel)
        {
            Console.WriteLine();
            Console.WriteLine("🧪 正在触发真实模型链路…");
            var harness = LiveModelHarnessFactory(options, resolver);
            var liveResult = harness
                .ExecuteAsync(translationRequest, CancellationToken.None, additionalLanguages)
                .GetAwaiter()
                .GetResult();

            if (liveResult.FallbackProviders.Count > 0)
            {
                Console.WriteLine("  ⚠ 已触发回退 Provider:");
                foreach (var fallback in liveResult.FallbackProviders)
                {
                    Console.WriteLine($"    - {fallback}");
                }
            }
            else
            {
                Console.WriteLine("  ✔ 所有 Provider 均满足外部调用条件。");
            }

            if (liveResult.Failures.Count > 0)
            {
                Console.WriteLine("  ✘ 以下 Provider 调用失败:");
                foreach (var failure in liveResult.Failures)
                {
                    Console.WriteLine($"    - {failure.ProviderId}: {failure.Message}");
                }
            }

            if (liveResult.Success is { } success)
            {
                var preview = success.TranslatedText.Length > 160
                    ? success.TranslatedText[..160] + "…"
                    : success.TranslatedText;
                Console.WriteLine("  ✔ 最终 Provider:");
                Console.WriteLine($"    Id:        {success.ProviderId}");
                Console.WriteLine($"    Model:     {success.ModelId}");
                Console.WriteLine($"    Latency:   {success.LatencyMs} ms");
                Console.WriteLine($"    Response:  {preview}");
                if (success.AdditionalTranslations.Count > 0)
                {
                    Console.WriteLine("    Additional:");
                    foreach (var entry in success.AdditionalTranslations)
                    {
                        Console.WriteLine($"      - {entry.Key}: {entry.Value}");
                    }
                }
            }
        }

        var metrics = new
        {
            overall = new { translations = 1, failures = 0 },
            tenants = new Dictionary<string, object>
            {
                [tenantId!] = new { translations = 1, lastLanguage = language }
            }
        };

        Console.WriteLine();
        Console.WriteLine("使用指标摘要:");
        Console.WriteLine(JsonSerializer.Serialize(metrics, JsonOptions));

        var audit = new
        {
            tenantId,
            status = "Success",
            language,
            toneApplied = translationTone,
            baseUrl,
            mode
        };

        Console.WriteLine();
        Console.WriteLine("审计记录样例:");
        Console.WriteLine(JsonSerializer.Serialize(audit, JsonOptions));

        return 0;
    }

    private static int RunMetrics(string[] args)
    {
        var overrides = new List<string>();
        string? appsettings = null;
        string? baseUrl = null;
        string? outputPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (IsHelpFlag(current))
            {
                PrintMetricsHelp();
                return 0;
            }

            switch (current)
            {
                case "--appsettings":
                    appsettings = RequireValue(args, ref i);
                    break;
                case "--override":
                    overrides.Add(RequireValue(args, ref i));
                    break;
                case "--baseUrl":
                    baseUrl = RequireValue(args, ref i);
                    break;
                case "--output":
                    outputPath = RequireValue(args, ref i);
                    break;
                default:
                    throw new ArgumentException($"未知参数 {current}");
            }
        }

        var options = LoadOptions(appsettings, overrides);

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            var baseUri = new Uri(baseUrl, UriKind.Absolute);
            using var client = new HttpClient { BaseAddress = baseUri };
            var metrics = client.GetStringAsync("/api/metrics").GetAwaiter().GetResult();
            var audit = client.GetStringAsync("/api/audit").GetAwaiter().GetResult();

            Console.WriteLine("远程指标响应:");
            Console.WriteLine(metrics);
            Console.WriteLine();
            Console.WriteLine("远程审计响应:");
            Console.WriteLine(audit);

            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                var artifact = new
                {
                    fetchedAt = DateTimeOffset.UtcNow,
                    metrics = JsonDocument.Parse(metrics).RootElement,
                    audit = JsonDocument.Parse(audit).RootElement
                };

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outputPath, JsonSerializer.Serialize(artifact, JsonOptions), Encoding.UTF8);
                Console.WriteLine();
                Console.WriteLine($"已写入 {outputPath}");
            }
        }
        else
        {
            var snapshot = new
            {
                overall = new { translations = 0, failures = 0 },
                tenants = Array.Empty<object>()
            };

            Console.WriteLine("未提供 baseUrl，输出示例指标:");
            Console.WriteLine(JsonSerializer.Serialize(snapshot, JsonOptions));
        }

        ReportStageReadinessFile(options.StageReadinessFilePath);

        return 0;
    }

    private static void ReportStageReadinessFile(string? configuredPath, bool probeWrite = false)
    {
        Console.WriteLine();
        Console.WriteLine("Stage 就绪文件检查:");

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            Console.WriteLine("  ✘ 未在配置中找到 Plugin.StageReadinessFilePath，默认路径将落在 App_Data。请在 Stage 覆盖文件中显式配置共享卷路径。");
            return;
        }

        string path;
        try
        {
            path = Path.GetFullPath(configuredPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Console.WriteLine($"  ✘ 无法解析 Stage 就绪文件路径 '{configuredPath}'：{ex.Message}");
            return;
        }

        Console.WriteLine($"  • 目标路径: {path}");

        if (probeWrite)
        {
            ProbeStageReadinessPath(path);
        }

        try
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  ✘ 未检测到就绪文件，请确认 Stage 实例已拥有该共享卷的读写权限并至少执行过一次冒烟。");
                return;
            }

            var content = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                Console.WriteLine("  ✘ 文件存在但内容为空，请检查写入逻辑或执行一次成功冒烟。");
                return;
            }

            if (DateTimeOffset.TryParse(content, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            {
                Console.WriteLine($"  ✔ 最近成功时间: {timestamp:O} (UTC)");
                return;
            }

            Console.WriteLine($"  ✘ 文件内容无法解析为 ISO-8601 时间戳：{content}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"  ✘ 读取 Stage 就绪文件失败：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"  ✘ 缺少 Stage 就绪文件的访问权限：{ex.Message}");
        }
    }

    private static void ProbeStageReadinessPath(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Path.GetDirectoryName(Path.GetFullPath(path));
            }

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "stage-readiness.txt";
            }

            var probeFile = string.IsNullOrWhiteSpace(directory)
                ? Path.GetFullPath($".{fileName}.probe")
                : Path.Combine(directory, $".{fileName}.probe");

            var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            File.WriteAllText(probeFile, timestamp);
            File.Delete(probeFile);
            Console.WriteLine("  ✔ 写入权限检查通过，可创建/更新 Stage 就绪文件。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"  ✘ 无法在该路径写入 Stage 就绪文件：{ex.Message}");
        }
    }

    private static PluginOptions LoadOptions(string? appsettings, IEnumerable<string> overrides)
    {
        var builder = new ConfigurationBuilder();
        var resolvedAppsettings = appsettings ?? DefaultAppsettingsPath;
        if (!File.Exists(resolvedAppsettings))
        {
            throw new FileNotFoundException($"未找到配置文件 {resolvedAppsettings}");
        }

        builder.AddJsonFile(resolvedAppsettings, optional: false, reloadOnChange: false);
        foreach (var path in overrides)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"未找到覆盖配置 {path}");
            }

            builder.AddJsonFile(path, optional: false, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables(prefix: "TLA_");
        var configuration = builder.Build();
        var options = new PluginOptions();
        configuration.GetSection("Plugin").Bind(options);
        return options;
    }

    private static IReadOnlyCollection<string> CollectSecretNames(PluginOptions options)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(options.Security.ClientSecretName))
        {
            names.Add(options.Security.ClientSecretName);
        }

        foreach (var provider in options.Providers)
        {
            if (!string.IsNullOrWhiteSpace(provider.ApiKeySecretName))
            {
                names.Add(provider.ApiKeySecretName);
            }
        }

        foreach (var seed in options.Security.SeedSecrets.Keys)
        {
            names.Add(seed);
        }

        foreach (var tenant in options.Security.TenantOverrides.Values)
        {
            if (!string.IsNullOrWhiteSpace(tenant.ClientSecretName))
            {
                names.Add(tenant.ClientSecretName!);
            }
        }

        return names.ToArray();
    }

    private static List<string> CollectTenantIds(PluginOptions options)
    {
        return options.Security.TenantOverrides.Keys.ToList();
    }

    private static SecretCheckResult ReportSecret(
        KeyVaultSecretResolver resolver,
        string secretName,
        string? tenantId,
        string? displayName = null)
    {
        try
        {
            var snapshot = resolver
                .GetSecretSnapshotAsync(secretName, tenantId, cancellationToken: default)
                .GetAwaiter()
                .GetResult();

            var prefix = displayName ?? (tenantId is null ? secretName : $"{tenantId} :: {secretName}");
            if (string.IsNullOrWhiteSpace(snapshot.Value))
            {
                Console.WriteLine($"  ✘ {prefix} -> <empty> (未解析到值)");
                return SecretCheckResult.Failed;
            }

            var masked = new string('*', Math.Min(8, snapshot.Value.Length));
            if (snapshot.Source == SecretSource.Unknown)
            {
                Console.WriteLine($"  ✘ {prefix} -> {masked} (来源未知)");
                return SecretCheckResult.Failed;
            }

            if (snapshot.ExpiresOnUtc is { } expiry)
            {
                if (expiry <= DateTimeOffset.UtcNow)
                {
                    Console.WriteLine($"  ✘ {prefix} -> {masked} (已于 {expiry:O} 过期)");
                    return SecretCheckResult.Failed;
                }

                if (expiry <= DateTimeOffset.UtcNow.AddDays(7))
                {
                    Console.WriteLine($"  ✘ {prefix} -> {masked} (即将于 {expiry:O} 过期，< 7 天)");
                    return SecretCheckResult.Failed;
                }

                Console.WriteLine(snapshot.Source == SecretSource.KeyVault
                    ? $"  ✔ {prefix} -> {masked} (KeyVault, 到期 {expiry:O})"
                    : $"  ✔ {prefix} -> {masked} (Seed, 到期 {expiry:O})");
                return SecretCheckResult.Passed;
            }

            var warningMessage = snapshot.Source == SecretSource.KeyVault
                ? "KeyVault 未设置到期时间"
                : "Seed 缺少到期信息";
            Console.WriteLine($"  ⚠ {prefix} -> {masked} ({warningMessage})");
            return SecretCheckResult.Warning;
        }
        catch (SecretRetrievalException ex)
        {
            Console.WriteLine(tenantId is null
                ? $"  ✘ {secretName} -> {ex.Message}"
                : $"  ✘ {tenantId} :: {secretName} -> {ex.Message}");
            return SecretCheckResult.Failed;
        }
    }

    private readonly record struct SecretCheckResult(bool Success, bool Warning)
    {
        public static readonly SecretCheckResult Passed = new(true, false);
        public static readonly SecretCheckResult Warning = new(true, true);
        public static readonly SecretCheckResult Failed = new(false, false);
    }

    private static string RequireValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"参数 {args[index]} 需要一个值。");
        }

        index++;
        return args[index];
    }

    private static bool IsHelpFlag(string value)
    {
        return string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintRootHelp()
    {
        Console.WriteLine("Stage 5 Smoke Tests");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- <命令> [选项]");
        Console.WriteLine();
        Console.WriteLine("可用命令:");
        Console.WriteLine("  secrets     检查 Key Vault 机密映射、Graph 作用域，并可探测 Stage 就绪路径。");
        Console.WriteLine("  reply       模拟 Stage 回帖流程，输出 Token 与诊断信息。");
        Console.WriteLine("  metrics     拉取 /api/metrics 与 /api/audit 观测数据。");
        Console.WriteLine("  ready       写入 Stage 就绪文件时间戳，标记最新冒烟结果。");
        Console.WriteLine();
        Console.WriteLine("使用 `--help` 查看每个命令的详细选项。");
    }

    private static void PrintSecretsHelp()
    {
        Console.WriteLine("用法: dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- secrets [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --appsettings <path>   指定基础 appsettings.json 路径，默认为 src/TlaPlugin/appsettings.json。");
        Console.WriteLine("  --override <path>      附加一个覆盖配置，可重复指定。");
        Console.WriteLine("  --tenant <tenant>      限定检查的租户 ID，可重复指定。");
        Console.WriteLine("  --verify-readiness     探测 Stage 就绪文件路径的读写权限并输出结果。");
    }

    private static void PrintReplyHelp()
    {
        Console.WriteLine("用法: dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- reply [选项]");
        Console.WriteLine();
        Console.WriteLine("必填:");
        Console.WriteLine("  --tenant <tenant>      Teams 租户 ID。");
        Console.WriteLine("  --user <user>          调用者 UPN 或用户 ID。");
        Console.WriteLine("  --thread <id>          Teams 消息线程 ID。");
        Console.WriteLine();
        Console.WriteLine("常用选项:");
        Console.WriteLine("  --text <text>          待回复文本。");
        Console.WriteLine("  --language <code>      目标语言，默认为配置中的首个默认语言。");
        Console.WriteLine("  --tone <tone>          语气 (business/friendly/technical 等)。");
        Console.WriteLine("  --channel <id>         Teams 频道 ID，启用渠道白名单时必填。");
        Console.WriteLine("  --assertion <jwt>      提供真实用户断言，禁用 HMAC 回退时必填。");
        Console.WriteLine("  --baseUrl <url>        指定远程 API 地址，触发远程调用诊断。");
        Console.WriteLine("  --use-live-graph       输出真实 Graph 模式诊断。");
        Console.WriteLine("  --use-live-model       输出真实模型模式诊断。");
        Console.WriteLine("  --use-remote-api       强制远程 API 模式。");
        Console.WriteLine("  --use-local-stub       在远程条件满足时仍使用本地 Stub。");
        Console.WriteLine("  --appsettings <path>   指定基础配置路径。");
        Console.WriteLine("  --override <path>      附加覆盖配置，可重复。");
    }

    private static void PrintMetricsHelp()
    {
        Console.WriteLine("用法: dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- metrics [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --baseUrl <url>        Stage 环境的根地址，例如 https://stage.contoso.net。");
        Console.WriteLine("  --output <path>        将指标与审计响应写入指定文件。");
        Console.WriteLine("  --appsettings <path>   指定基础配置路径。");
        Console.WriteLine("  --override <path>      附加覆盖配置，可重复。");
    }

    private static void PrintReadyHelp()
    {
        Console.WriteLine("用法: dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- ready [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --appsettings <path>   指定基础配置路径，默认为 src/TlaPlugin/appsettings.json。");
        Console.WriteLine("  --override <path>      附加覆盖配置，可重复。");
        Console.WriteLine("  --path <path>          显式指定 Stage 就绪文件路径，优先于配置。");
        Console.WriteLine("  --timestamp <value>    指定 ISO-8601 时间戳，默认写入当前 UTC 时间。");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- ready --override appsettings.Stage.json");
        Console.WriteLine("  dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- ready --path /mnt/shared/stage-ready.txt");
    }

    private static string GenerateMockAssertion(string tenantId, string userId, string audience)
    {
        static string Base64UrlEncode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        var header = Base64UrlEncode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = new
        {
            aud = audience,
            tid = tenantId,
            sub = userId,
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()
        };
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var encodedPayload = Base64UrlEncode(payloadJson);
        return $"{header}.{encodedPayload}.";
    }

    private static void WriteError(string message)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ForegroundColor = originalColor;
    }
}
