using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Providers;
using TlaPlugin.Services;

var cancellationToken = CancellationToken.None;
var command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
    ? args[0].ToLowerInvariant()
    : "help";
var optionsMap = ParseOptions(args.Skip(command == "help" ? 0 : 1).ToArray());

if (command is "help" or "-h" or "--help")
{
    PrintUsage();
    return 0;
}

var appsettingsPath = optionsMap.TryGetValue("appsettings", out var configPath) && !string.IsNullOrWhiteSpace(configPath)
    ? configPath!
    : Path.Combine(AppContext.BaseDirectory, "../../../../src/TlaPlugin/appsettings.json");
var additionalConfig = optionsMap.TryGetValue("override", out var overridePath) && !string.IsNullOrWhiteSpace(overridePath)
    ? overridePath
    : null;

if (!File.Exists(appsettingsPath))
{
    Console.Error.WriteLine($"配置文件 {appsettingsPath} 不存在。");
    return 1;
}

var (pluginOptions, configuration) = LoadOptions(appsettingsPath, additionalConfig);

switch (command)
{
    case "secrets":
        return await RunSecretCheckAsync(pluginOptions, cancellationToken).ConfigureAwait(false);
    case "reply":
        return await RunReplySmokeAsync(pluginOptions, optionsMap, cancellationToken).ConfigureAwait(false);
    case "metrics":
        return await RunMetricsProbeAsync(optionsMap, cancellationToken).ConfigureAwait(false);
    default:
        Console.Error.WriteLine($"未知命令: {command}");
        PrintUsage();
        return 1;
}

static Dictionary<string, string?> ParseOptions(string[] arguments)
{
    var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < arguments.Length; i++)
    {
        var argument = arguments[i];
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var trimmed = argument[2..];
        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex >= 0)
        {
            var key = trimmed[..separatorIndex];
            var value = trimmed[(separatorIndex + 1)..];
            result[key] = string.IsNullOrWhiteSpace(value) ? null : value;
            continue;
        }

        var optionKey = trimmed;
        string? optionValue = null;
        if (i + 1 < arguments.Length && !arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            optionValue = arguments[++i];
        }

        result[optionKey] = optionValue;
    }

    return result;
}

static void PrintUsage()
{
    Console.WriteLine("Stage 5 冒烟测试工具");
    Console.WriteLine();
    Console.WriteLine("命令:");
    Console.WriteLine("  secrets   校验 appsettings.json 中声明的密钥是否可由 KeyVaultSecretResolver 读取。");
    Console.WriteLine("  reply     模拟 OBO 令牌与 Teams 回帖调用，输出指标与审计快照。");
    Console.WriteLine("  metrics   访问已部署服务的 /api/metrics 与 /api/audit 端点并打印结果。");
    Console.WriteLine();
    Console.WriteLine("通用参数:");
    Console.WriteLine("  --appsettings <path>    指定要加载的 appsettings.json，默认使用 src/TlaPlugin/appsettings.json。");
    Console.WriteLine("  --override <path>       可选的额外 JSON 配置文件，后加载覆盖主配置。");
    Console.WriteLine();
    Console.WriteLine("reply 命令参数:");
    Console.WriteLine("  --tenant <id>           目标租户 ID，默认为 contoso.onmicrosoft.com。");
    Console.WriteLine("  --user <id>             模拟的用户 ID，默认为 user1。");
    Console.WriteLine("  --thread <id>           线程/消息 ID，默认为 message-id。");
    Console.WriteLine("  --channel <id>          Channel ID，可省略以模拟 1:1 聊天。");
    Console.WriteLine("  --language <code>       回帖语言，默认为 ja。");
    Console.WriteLine("  --tone <tone>           语气模板，默认为 polite。");
    Console.WriteLine("  --text <content>        待翻译并回帖的正文，默认为 'ステージ 5 連携テスト'。");
    Console.WriteLine("  --assertion <jwt>       用户断言 (JWT)。HMAC 回退模式下可省略，脚本会生成模拟值。");
    Console.WriteLine("  --use-live-graph        启用真实 Graph 调用，默认使用内置模拟响应。");
    Console.WriteLine("  --use-live-model        启用真实模型 Provider，默认使用内置 Stub 模型。");
    Console.WriteLine();
    Console.WriteLine("metrics 命令参数:");
    Console.WriteLine("  --baseUrl <url>         Stage 服务的根地址，默认为 https://localhost:5001。");
    Console.WriteLine("  --output <path>         可选，将响应写入指定文件（JSON 文本）。");
    Console.WriteLine();
    Console.WriteLine("示例:");
    Console.WriteLine("  dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- secrets");
    Console.WriteLine("  dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- reply --tenant contoso.onmicrosoft.com --user fiona --thread 19:abc --channel general");
    Console.WriteLine("  dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- metrics --baseUrl https://stage5.contoso.net");
}

static (PluginOptions Options, IConfigurationRoot Configuration) LoadOptions(string appsettingsPath, string? overridePath)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(appsettingsPath)) ?? Directory.GetCurrentDirectory();
    var fileName = Path.GetFileName(appsettingsPath);
    var builder = new ConfigurationBuilder()
        .SetBasePath(directory)
        .AddJsonFile(fileName, optional: false, reloadOnChange: false);

    if (!string.IsNullOrWhiteSpace(overridePath))
    {
        builder.AddJsonFile(overridePath, optional: false, reloadOnChange: false);
    }

    var localOverride = Path.Combine(directory, Path.GetFileNameWithoutExtension(fileName) + ".Local.json");
    if (File.Exists(localOverride))
    {
        builder.AddJsonFile(localOverride, optional: true, reloadOnChange: false);
    }

    builder.AddEnvironmentVariables(prefix: "TLA_");
    var configuration = builder.Build();
    var options = new PluginOptions();
    configuration.GetSection("Plugin").Bind(options);
    return (options, configuration);
}

static async Task<int> RunSecretCheckAsync(PluginOptions options, CancellationToken cancellationToken)
{
    var resolver = new KeyVaultSecretResolver(Options.Create(options));
    var probes = BuildSecretProbes(options);
    var probeResults = new List<SecretProbeResult>();
    var fallbackDisabled = !options.Security.UseHmacFallback
        || options.Security.FailOnSeedFallback
        || options.Security.RequireVaultSecrets;

    if (probes.Count == 0)
    {
        Console.WriteLine("未在配置中找到任何需要解析的密钥名称。");
        return 0;
    }

    var success = new List<string>();
    var failures = new List<string>();

    foreach (var probe in probes)
    {
        try
        {
            var value = await resolver.GetSecretAsync(probe.SecretName, probe.TenantId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(value))
            {
                failures.Add($"{FormatProbe(probe)} => 解析结果为空");
            }
            else
            {
                success.Add($"{FormatProbe(probe)} => 长度 {value.Length}");
                probeResults.Add(new SecretProbeResult(probe.TenantId, probe.SecretName, ComputeFingerprint(value)));
            }
        }
        catch (SecretRetrievalException ex)
        {
            var vault = string.IsNullOrWhiteSpace(ex.VaultUri) ? options.Security.KeyVaultUri : ex.VaultUri;
            var hint = fallbackDisabled
                ? "Stage 默认禁止 SeedSecrets 回退，请立即检查 Key Vault 映射与访问策略是否已配置完整。"
                : "请确认托管身份或应用主体已在对应 Key Vault 中授予 get 权限，并且密钥名称拼写正确。";
            failures.Add($"{FormatProbe(probe)} => 无法访问远程 Key Vault ({vault}): {ex.InnerException?.Message ?? ex.Message}. {hint}");
        }
        catch (Exception ex)
        {
            failures.Add($"{FormatProbe(probe)} => {ex.Message}");
        }
    }

    Console.WriteLine("[KeyVaultSecretResolver] 解析结果:");
    foreach (var line in success)
    {
        Console.WriteLine("  ✔ " + line);
    }

    foreach (var line in failures)
    {
        Console.WriteLine("  ✘ " + line);
    }

    Console.WriteLine();
    var diagnostics = PrintTenantOverrideSummary(options, probeResults);
    var totalFailures = failures.Count + diagnostics.FailureCount;
    Console.WriteLine($"成功: {success.Count}, 失败: {totalFailures}");
    if (diagnostics.MissingComparisons > 0)
    {
        Console.WriteLine($"提示: 有 {diagnostics.MissingComparisons} 个租户覆盖缺少对比数据，请确认已在 Key Vault 中创建对应密钥。");
    }
    Console.WriteLine();
    PrintGraphScopeReminder(options);
    return totalFailures == 0 ? 0 : 2;
}

static List<SecretProbe> BuildSecretProbes(PluginOptions options)
{
    var probes = new List<SecretProbe>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void AddProbe(string? tenantId, string? secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return;
        }

        var key = $"{tenantId ?? "__default__"}::{secretName}";
        if (seen.Add(key))
        {
            probes.Add(new SecretProbe(tenantId, secretName));
        }
    }

    AddProbe(null, options.Security.ClientSecretName);

    foreach (var provider in options.Providers.Where(p => !string.IsNullOrWhiteSpace(p.ApiKeySecretName)))
    {
        AddProbe(null, provider.ApiKeySecretName);
        if (!string.IsNullOrWhiteSpace(provider.ApiKeyTenantId))
        {
            AddProbe(provider.ApiKeyTenantId, provider.ApiKeySecretName);
        }
    }

    foreach (var kvp in options.Security.TenantOverrides)
    {
        var tenantSecret = !string.IsNullOrWhiteSpace(kvp.Value.ClientSecretName)
            ? kvp.Value.ClientSecretName
            : options.Security.ClientSecretName;
        AddProbe(kvp.Key, tenantSecret);
    }

    probes.Sort((left, right) => string.CompareOrdinal(FormatProbe(left), FormatProbe(right)));
    return probes;
}

static string FormatProbe(SecretProbe probe)
    => string.IsNullOrWhiteSpace(probe.TenantId) ? probe.SecretName : $"{probe.TenantId}/{probe.SecretName}";

private readonly record struct SecretProbe(string? TenantId, string SecretName);
private readonly record struct SecretProbeResult(string? TenantId, string SecretName, string Fingerprint);

static TenantOverrideDiagnostics PrintTenantOverrideSummary(PluginOptions options, IReadOnlyList<SecretProbeResult> results)
{
    var overrides = options.Security.TenantOverrides
        .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value.KeyVaultUri))
        .Select(kvp => new
        {
            TenantId = kvp.Key,
            SecretName = !string.IsNullOrWhiteSpace(kvp.Value.ClientSecretName)
                ? kvp.Value.ClientSecretName!
                : options.Security.ClientSecretName
        })
        .ToList();

    if (overrides.Count == 0 || results.Count == 0)
    {
        return new TenantOverrideDiagnostics(0, 0);
    }

    var lookup = new Dictionary<string, SecretProbeResult>(StringComparer.OrdinalIgnoreCase);
    foreach (var result in results)
    {
        var key = BuildResultKey(result.TenantId, result.SecretName);
        lookup[key] = result;
    }

    Console.WriteLine("[KeyVaultSecretResolver] 租户 KeyVaultUri 覆盖检查:");

    var failureCount = 0;
    var missingComparisons = 0;

    foreach (var group in overrides.GroupBy(o => o.SecretName, StringComparer.OrdinalIgnoreCase))
    {
        var candidates = new List<SecretProbeResult>();
        if (lookup.TryGetValue(BuildResultKey(null, group.Key), out var defaultResult))
        {
            candidates.Add(defaultResult);
        }

        foreach (var entry in group)
        {
            if (lookup.TryGetValue(BuildResultKey(entry.TenantId, entry.SecretName), out var tenantResult))
            {
                candidates.Add(tenantResult);
            }
        }

        if (candidates.Count < 2)
        {
            Console.WriteLine($"  ⚠️ {group.Key} => 缺少默认或租户特定的密钥值，无法对比。");
            missingComparisons++;
            continue;
        }

        var distinct = candidates
            .Select(result => result.Fingerprint)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinct > 1)
        {
            Console.WriteLine($"  ✔ {group.Key} => {candidates.Count} 个环境返回 {distinct} 个不同值。");
        }
        else
        {
            Console.WriteLine($"  ✘ {group.Key} => 所有环境返回相同的值，请确认 KeyVaultUri 覆盖是否生效。");
            failureCount++;
        }
    }

    Console.WriteLine();

    return new TenantOverrideDiagnostics(failureCount, missingComparisons);
}

static string BuildResultKey(string? tenantId, string secretName)
    => $"{tenantId ?? "__default__"}::{secretName}";

private readonly record struct TenantOverrideDiagnostics(int FailureCount, int MissingComparisons);

static string ComputeFingerprint(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return string.Empty;
    }

    using var sha256 = SHA256.Create();
    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
    var fingerprint = BitConverter.ToString(hash, 0, Math.Min(6, hash.Length));
    return fingerprint.Replace("-", string.Empty);
}

static void PrintGraphScopeReminder(PluginOptions options)
{
    var scopes = options.Security.GraphScopes
        .Where(scope => !string.IsNullOrWhiteSpace(scope))
        .Select(scope => scope.Trim())
        .ToList();

    Console.WriteLine("GraphScopes 配置检查:");

    if (scopes.Count == 0)
    {
        Console.WriteLine("  ⚠️ 未配置 GraphScopes。请在 Azure AD 中授权 https://graph.microsoft.com/.default 等作用域，否则 OBO 会返回 invalid_scope。");
        Console.WriteLine("  提醒：作用域需与 Azure AD 应用注册的授权列表一致。");
        Console.WriteLine();
        return;
    }

    foreach (var scope in scopes)
    {
        Console.WriteLine("  - " + scope);
    }

    var invalid = scopes
        .Where(scope => !scope.StartsWith("https://graph.microsoft.com/", StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (invalid.Count > 0)
    {
        Console.WriteLine("  ⚠️ 检测到未使用资源限定格式的 scope。请改用 https://graph.microsoft.com/.default 或 https://graph.microsoft.com/<Permission> 并与 Azure AD 管理员已授权的范围一致，避免 OBO 出现 invalid_scope。");
    }
    else
    {
        Console.WriteLine("  提醒：请确认上述作用域已完成管理员同意并保持与 Azure AD 授权同步。");
    }

    Console.WriteLine();
}

static async Task<int> RunReplySmokeAsync(PluginOptions options, IReadOnlyDictionary<string, string?> parameters, CancellationToken cancellationToken)
{
    var useLiveGraph = parameters.ContainsKey("use-live-graph");
    var useLiveModel = parameters.ContainsKey("use-live-model");
    var tenantId = GetValue(parameters, "tenant", "contoso.onmicrosoft.com");
    var userId = GetValue(parameters, "user", "user1");
    var threadId = GetValue(parameters, "thread", "message-id");
    var channelId = GetOptionalValue(parameters, "channel");
    var language = GetValue(parameters, "language", "ja");
    var tone = GetValue(parameters, "tone", TranslationRequest.DefaultTone);
    var text = GetValue(parameters, "text", "ステージ 5 連携テスト");
    var userAssertion = GetOptionalValue(parameters, "assertion");

    if (string.IsNullOrWhiteSpace(userAssertion))
    {
        if (options.Security.UseHmacFallback)
        {
            userAssertion = BuildSimulatedUserAssertion(
                tenantId,
                userId,
                options.Security.UserAssertionAudience);
            Console.WriteLine("提示：未提供用户断言，已生成模拟 JWT 以驱动 HMAC 回退流程。");
        }
        else
        {
            Console.Error.WriteLine("真实 OBO 模式需要提供用户断言 (--assertion <jwt>)。");
            return 13;
        }
    }

    if (!options.Security.UseHmacFallback)
    {
        PrintGraphScopeReminder(options);
    }

    var metrics = new UsageMetricsService();
    var audit = new AuditLogger();
    var resolver = new KeyVaultSecretResolver(Options.Create(options));
    var tokenBroker = new TokenBroker(resolver, Options.Create(options));
    var localization = new LocalizationCatalogService();
    var budget = new BudgetGuard(options);
    var compliance = new ComplianceGateway(Options.Create(options));
    var tones = new ToneTemplateService();
    var providerTemplate = options.Providers.FirstOrDefault();
    var providerOptions = providerTemplate is null
        ? new ModelProviderOptions
        {
            Id = "smoke-model",
            TranslationPrefix = "[Smoke]",
            Regions = new List<string> { "global" },
            Certifications = new List<string> { "iso27001" },
            Endpoint = string.Empty,
            Model = "smoke"
        }
        : new ModelProviderOptions
        {
            Id = providerTemplate.Id,
            Kind = providerTemplate.Kind,
            CostPerCharUsd = providerTemplate.CostPerCharUsd,
            LatencyTargetMs = providerTemplate.LatencyTargetMs,
            Reliability = providerTemplate.Reliability,
            Regions = new List<string>(providerTemplate.Regions),
            Certifications = new List<string>(providerTemplate.Certifications),
            TranslationPrefix = providerTemplate.TranslationPrefix,
            Endpoint = providerTemplate.Endpoint,
            Model = providerTemplate.Model,
            ApiKeySecretName = providerTemplate.ApiKeySecretName,
            Organization = providerTemplate.Organization,
            DefaultHeaders = new Dictionary<string, string>(providerTemplate.DefaultHeaders)
        };
    if (providerOptions.Regions.Count == 0)
    {
        providerOptions.Regions.Add("global");
    }
    if (providerOptions.Certifications.Count == 0)
    {
        providerOptions.Certifications.Add("iso27001");
    }

    var httpClientFactory = useLiveModel ? new SmokeTestHttpClientFactory() : null;
    var providerFactory = new ModelProviderFactory(Options.Create(options), httpClientFactory, useLiveModel ? resolver : null);

    IEnumerable<IModelProvider>? providerOverrides = null;
    if (!useLiveModel)
    {
        var stubProvider = new StubModelProvider(providerOptions);
        providerOverrides = new[] { stubProvider };
    }

    var router = new TranslationRouter(
        providerFactory,
        compliance,
        budget,
        audit,
        tones,
        tokenBroker,
        metrics,
        localization,
        Options.Create(options),
        providerOverrides);
    var throttle = new TranslationThrottle(Options.Create(options));
    var rewrite = new RewriteService(router, throttle);

    var additionalTargets = options.DefaultTargetLanguages
        .Where(l => !string.Equals(l, language, StringComparison.OrdinalIgnoreCase))
        .Take(3)
        .ToList();
    if (additionalTargets.Count == 0)
    {
        additionalTargets.Add("en-US");
    }

    var translationRequest = new TranslationRequest
    {
        Text = text,
        TargetLanguage = language,
        TenantId = tenantId,
        UserId = userId,
        ChannelId = channelId,
        ThreadId = threadId,
        Tone = tone,
        UiLocale = options.DefaultUiLocale,
        AdditionalTargetLanguages = additionalTargets,
        UserAssertion = userAssertion
    };

    TranslationResult translation;
    try
    {
        translation = await router.TranslateAsync(translationRequest, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("翻译阶段失败: " + ex.Message);
        return 3;
    }

    HttpMessageHandler innerHandler = !useLiveGraph || options.Security.UseHmacFallback
        ? new StubGraphHandler()
        : new HttpClientHandler();
    if (innerHandler is HttpClientHandler httpHandler
        && !string.IsNullOrWhiteSpace(options.Security.GraphProxy))
    {
        httpHandler.Proxy = new WebProxy(options.Security.GraphProxy);
        httpHandler.UseProxy = true;
    }
    var handler = new RecordingGraphHandler(innerHandler);
    var graphTrace = handler;
    var graphBase = string.IsNullOrWhiteSpace(options.Security.GraphBaseUrl)
        ? "https://graph.microsoft.com/v1.0/"
        : options.Security.GraphBaseUrl;
    var httpClient = new HttpClient(handler)
    {
        BaseAddress = new Uri(graphBase!, UriKind.Absolute)
    };
    if (options.Security.GraphTimeout > TimeSpan.Zero)
    {
        httpClient.Timeout = options.Security.GraphTimeout;
    }
    var teamsClient = new TeamsReplyClient(httpClient);
    var replyService = new ReplyService(rewrite, router, teamsClient, tokenBroker, metrics, Options.Create(options));

    var replyRequest = new ReplyRequest
    {
        ThreadId = threadId,
        ReplyText = translation.TranslatedText,
        Text = translation.TranslatedText,
        TenantId = tenantId,
        UserId = userId,
        ChannelId = channelId,
        UiLocale = translation.UiLocale,
        LanguagePolicy = new ReplyLanguagePolicy
        {
            TargetLang = translation.TargetLanguage,
            Tone = tone
        },
        AdditionalTargetLanguages = additionalTargets
    };

    replyRequest.UserAssertion = userAssertion;

    ReplyResult replyResult;
    try
    {
        var retryCount = useLiveGraph ? 3 : 1;
        replyResult = await ExecuteWithRetryAsync(
            ct => replyService.SendReplyAsync(replyRequest, translation.TranslatedText, tone, ct),
            retryCount,
            TimeSpan.FromSeconds(2),
            cancellationToken,
            ShouldRetryGraph).ConfigureAwait(false);
    }
    catch (TeamsReplyException ex)
    {
        Console.Error.WriteLine($"回帖阶段失败: {(int)ex.StatusCode} {ex.StatusCode}");
        Console.Error.WriteLine("Graph 错误信息: " + ex.Message);
        if (!string.IsNullOrWhiteSpace(ex.ErrorCode))
        {
            Console.Error.WriteLine("Graph 错误代码: " + ex.ErrorCode);
        }
        PrintGraphDiagnostics(graphTrace, useLiveGraph);
        return 4;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("回帖阶段失败: " + ex.Message);
        PrintGraphDiagnostics(graphTrace, useLiveGraph);
        return 4;
    }

    var metricsReport = metrics.GetReport();
    var auditLogs = audit.Export();

    Console.WriteLine("[TeamsReplyClient] 调用成功:");
    Console.WriteLine($"  MessageId: {replyResult.MessageId}");
    Console.WriteLine($"  Status:    {replyResult.Status}");
    Console.WriteLine($"  Language:  {replyResult.Language}");
    PrintGraphDiagnostics(graphTrace, useLiveGraph);

    if (additionalTargets.Count > 0)
    {
        if (translation.AdditionalTranslations.Count == 0)
        {
            Console.Error.WriteLine("翻译结果缺少附加语种输出。");
            return 8;
        }

        foreach (var language in additionalTargets)
        {
            if (!translation.AdditionalTranslations.ContainsKey(language))
            {
                Console.Error.WriteLine($"翻译结果未包含 {language} 的附加译文。");
                return 9;
            }
        }

        if (!string.IsNullOrWhiteSpace(graphTrace.LastBody))
        {
            try
            {
                using var payload = JsonDocument.Parse(graphTrace.LastBody);
                var metadata = payload.RootElement
                    .GetProperty("channelData")
                    .GetProperty("metadata");
                if (!metadata.TryGetProperty("additionalTranslations", out var metadataTranslations) || metadataTranslations.ValueKind != JsonValueKind.Object)
                {
                    Console.Error.WriteLine("Graph 请求缺少 additionalTranslations 字段。");
                    return 10;
                }

                foreach (var language in additionalTargets)
                {
                    if (!metadataTranslations.TryGetProperty(language, out var translationNode) || translationNode.ValueKind != JsonValueKind.String)
                    {
                        Console.Error.WriteLine($"Graph 请求未包含 {language} 的附加译文。");
                        return 11;
                    }
                }
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
            {
                Console.Error.WriteLine("无法解析 Graph 请求以验证附加译文: " + ex.Message);
                return 12;
            }
        }
    }

    Console.WriteLine("使用指标摘要:");
    Console.WriteLine(JsonSerializer.Serialize(metricsReport, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine();

    Console.WriteLine("审计记录样例:");
    foreach (var log in auditLogs)
    {
        Console.WriteLine(log.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    if (metricsReport.Tenants.Count == 0)
    {
        Console.Error.WriteLine("未捕获任何指标，请检查配置。");
        return 5;
    }

    if (auditLogs.Count == 0)
    {
        Console.Error.WriteLine("未生成审计记录，请检查翻译结果。");
        return 6;
    }

    if (graphTrace.CallCount == 0)
    {
        Console.Error.WriteLine("未触发 TeamsReplyClient 调用。");
        return 7;
    }

    return 0;
}

static async Task<int> RunMetricsProbeAsync(IReadOnlyDictionary<string, string?> parameters, CancellationToken cancellationToken)
{
    var baseUrl = GetValue(parameters, "baseUrl", "https://localhost:5001");
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
    {
        Console.Error.WriteLine($"无效的 baseUrl: {baseUrl}");
        return 1;
    }

    using var httpClient = new HttpClient
    {
        BaseAddress = baseUri
    };

    static async Task<string> ReadOrThrowAsync(HttpResponseMessage response, string name, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var reason = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"请求 {name} 失败: {(int)response.StatusCode} {response.ReasonPhrase}. {reason}");
        }

        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    HttpResponseMessage metricsResponse;
    HttpResponseMessage auditResponse;
    try
    {
        metricsResponse = await httpClient.GetAsync("/api/metrics", cancellationToken).ConfigureAwait(false);
        auditResponse = await httpClient.GetAsync("/api/audit", cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("调用阶段环境 API 失败: " + ex.Message);
        return 2;
    }

    string metricsPayload;
    string auditPayload;
    try
    {
        metricsPayload = await ReadOrThrowAsync(metricsResponse, "/api/metrics", cancellationToken).ConfigureAwait(false);
        auditPayload = await ReadOrThrowAsync(auditResponse, "/api/audit", cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 3;
    }

    Console.WriteLine("/api/metrics 响应:");
    Console.WriteLine(metricsPayload);
    Console.WriteLine();
    Console.WriteLine("/api/audit 响应:");
    Console.WriteLine(auditPayload);

    if (parameters.TryGetValue("output", out var outputPath) && !string.IsNullOrWhiteSpace(outputPath))
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            var payload = new
            {
                Metrics = metricsPayload,
                Audit = auditPayload,
                RetrievedAt = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            };
            var serialized = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputPath!, serialized, cancellationToken).ConfigureAwait(false);
            Console.WriteLine();
            Console.WriteLine($"响应已写入 {outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("写入输出文件失败: " + ex.Message);
            return 4;
        }
    }

    return 0;
}

static void PrintGraphDiagnostics(RecordingGraphHandler trace, bool useLiveGraph)
{
    Console.WriteLine("Graph 调用诊断:");
    Console.WriteLine($"  Mode:        {(useLiveGraph ? "live" : "stub")}");
    Console.WriteLine($"  CallCount:   {trace.CallCount}");
    Console.WriteLine($"  LastPath:    {trace.LastPath ?? "<none>"}");
    Console.WriteLine($"  Authorization:{(string.IsNullOrWhiteSpace(trace.LastAuthorization) ? " <none>" : " " + trace.LastAuthorization)}");
    if (!string.IsNullOrWhiteSpace(trace.LastBody))
    {
        Console.WriteLine("  Body:");
        Console.WriteLine(trace.LastBody);
    }
    else
    {
        Console.WriteLine("  Body:        <none>");
    }
}

static bool ShouldRetryGraph(Exception ex)
{
    if (ex is TeamsReplyException replyException)
    {
        return replyException.StatusCode == HttpStatusCode.TooManyRequests
            || replyException.StatusCode == HttpStatusCode.RequestTimeout
            || replyException.StatusCode == HttpStatusCode.ServiceUnavailable
            || replyException.StatusCode == HttpStatusCode.GatewayTimeout
            || replyException.StatusCode == HttpStatusCode.BadGateway;
    }

    return ex is HttpRequestException or TaskCanceledException;
}

static async Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> action, int maxAttempts, TimeSpan delay, CancellationToken cancellationToken, Func<Exception, bool> shouldRetry)
{
    if (action is null)
    {
        throw new ArgumentNullException(nameof(action));
    }
    if (shouldRetry is null)
    {
        throw new ArgumentNullException(nameof(shouldRetry));
    }
    if (maxAttempts <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(maxAttempts));
    }

    var attempt = 0;
    while (true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            attempt++;
            return await action(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (attempt < maxAttempts && shouldRetry(ex))
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

static string GetValue(IReadOnlyDictionary<string, string?> parameters, string key, string fallback)
    => parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value!
        : fallback;

static string? GetOptionalValue(IReadOnlyDictionary<string, string?> parameters, string key)
    => parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

static string BuildSimulatedUserAssertion(string tenantId, string userId, string? audience)
{
    static string Base64UrlEncode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    var effectiveAudience = string.IsNullOrWhiteSpace(audience)
        ? "api://tla-plugin"
        : audience!;
    var now = DateTimeOffset.UtcNow;
    var header = Base64UrlEncode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
    var payload = new
    {
        aud = effectiveAudience,
        tid = tenantId,
        sub = userId,
        upn = userId,
        iss = "Stage5SmokeTests",
        iat = now.ToUnixTimeSeconds(),
        exp = now.AddMinutes(30).ToUnixTimeSeconds(),
        ver = "1.0"
    };
    var payloadSegment = Base64UrlEncode(JsonSerializer.Serialize(payload));
    var signature = Base64UrlEncode("stage5-smoke");
    return $"{header}.{payloadSegment}.{signature}";
}

sealed class RecordingGraphHandler : DelegatingHandler
{
    public RecordingGraphHandler(HttpMessageHandler innerHandler)
        : base(innerHandler ?? throw new ArgumentNullException(nameof(innerHandler)))
    {
    }

    public int CallCount { get; private set; }
    public string? LastPath { get; private set; }
    public string? LastAuthorization { get; private set; }
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastPath = request.RequestUri is null
            ? null
            : (string.IsNullOrEmpty(request.RequestUri.PathAndQuery)
                ? request.RequestUri.ToString()
                : request.RequestUri.PathAndQuery);
        LastAuthorization = request.Headers.Authorization?.ToString();
        LastBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

sealed class SmokeTestHttpClientFactory : IHttpClientFactory
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public HttpClient CreateClient(string name)
    {
        var key = string.IsNullOrWhiteSpace(name) ? "default" : name;
        return _clients.GetOrAdd(key, _ => new HttpClient());
    }
}

sealed class StubGraphHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent($"{{\"id\":\"smoke-{Guid.NewGuid():N}\",\"createdDateTime\":\"{DateTimeOffset.UtcNow:O}\"}}", Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

sealed class StubModelProvider : IModelProvider
{
    public StubModelProvider(ModelProviderOptions options)
    {
        Options = options;
        if (string.IsNullOrWhiteSpace(Options.TranslationPrefix))
        {
            Options.TranslationPrefix = "[Stub]";
        }
        if (Options.CostPerCharUsd <= 0)
        {
            Options.CostPerCharUsd = 0.00002m;
        }
        if (Options.LatencyTargetMs <= 0)
        {
            Options.LatencyTargetMs = 120;
        }
    }

    public ModelProviderOptions Options { get; }

    public Task<DetectionResult> DetectAsync(string text, CancellationToken cancellationToken)
    {
        var candidates = new List<DetectionCandidate>
        {
            new("en", 0.9),
            new("ja", 0.1)
        };
        return Task.FromResult(new DetectionResult("en", 0.9, candidates));
    }

    public Task<ModelTranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, string promptPrefix, CancellationToken cancellationToken)
    {
        var rewritten = $"{Options.TranslationPrefix}{targetLanguage}:{text}";
        return Task.FromResult(new ModelTranslationResult(rewritten, Options.Id, 0.98, Options.LatencyTargetMs));
    }

    public Task<string> RewriteAsync(string translatedText, string tone, CancellationToken cancellationToken)
    {
        var normalizedTone = string.IsNullOrWhiteSpace(tone) ? ToneTemplateService.DefaultTone : tone;
        return Task.FromResult($"[{normalizedTone}] {translatedText}");
    }

    public Task<string> SummarizeAsync(string text, CancellationToken cancellationToken)
    {
        var summary = text.Length <= 120 ? text : text[..117] + "...";
        return Task.FromResult(summary);
    }
}
