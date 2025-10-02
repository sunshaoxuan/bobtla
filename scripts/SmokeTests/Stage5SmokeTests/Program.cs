using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    var secretNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

    if (!string.IsNullOrWhiteSpace(options.Security.ClientSecretName))
    {
        secretNames.Add(options.Security.ClientSecretName);
    }

    foreach (var provider in options.Providers.Where(p => !string.IsNullOrWhiteSpace(p.ApiKeySecretName)))
    {
        secretNames.Add(provider.ApiKeySecretName);
    }

    foreach (var tenantOverride in options.Security.TenantOverrides.Values)
    {
        if (!string.IsNullOrWhiteSpace(tenantOverride.ClientSecretName))
        {
            secretNames.Add(tenantOverride.ClientSecretName);
        }
    }

    if (secretNames.Count == 0)
    {
        Console.WriteLine("未在配置中找到任何需要解析的密钥名称。");
        return 0;
    }

    var success = new List<string>();
    var failures = new List<string>();

    foreach (var name in secretNames)
    {
        try
        {
            var value = await resolver.GetSecretAsync(name, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(value))
            {
                failures.Add($"{name} => 解析结果为空");
            }
            else
            {
                success.Add($"{name} => 长度 {value.Length}");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{name} => {ex.Message}");
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
    Console.WriteLine($"成功: {success.Count}, 失败: {failures.Count}");
    return failures.Count == 0 ? 0 : 2;
}

static async Task<int> RunReplySmokeAsync(PluginOptions options, IReadOnlyDictionary<string, string?> parameters, CancellationToken cancellationToken)
{
    var tenantId = GetValue(parameters, "tenant", "contoso.onmicrosoft.com");
    var userId = GetValue(parameters, "user", "user1");
    var threadId = GetValue(parameters, "thread", "message-id");
    var channelId = GetOptionalValue(parameters, "channel");
    var language = GetValue(parameters, "language", "ja");
    var tone = GetValue(parameters, "tone", TranslationRequest.DefaultTone);
    var text = GetValue(parameters, "text", "ステージ 5 連携テスト");

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

    var stubProvider = new StubModelProvider(providerOptions);
    var router = new TranslationRouter(
        new ModelProviderFactory(Options.Create(options)),
        compliance,
        budget,
        audit,
        tones,
        tokenBroker,
        metrics,
        localization,
        Options.Create(options),
        new[] { stubProvider });
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
        AdditionalTargetLanguages = additionalTargets
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

    var handler = new FakeGraphHandler();
    var httpClient = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://graph.microsoft.com/v1.0/", UriKind.Absolute)
    };
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

    ReplyResult replyResult;
    try
    {
        replyResult = await replyService.SendReplyAsync(replyRequest, translation.TranslatedText, tone, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("回帖阶段失败: " + ex.Message);
        return 4;
    }

    var metricsReport = metrics.GetReport();
    var auditLogs = audit.Export();

    Console.WriteLine("[TeamsReplyClient] 调用成功:");
    Console.WriteLine($"  MessageId: {replyResult.MessageId}");
    Console.WriteLine($"  Status:    {replyResult.Status}");
    Console.WriteLine($"  Language:  {replyResult.Language}");
    Console.WriteLine();
    Console.WriteLine("Graph 请求回显:");
    Console.WriteLine($"  Path:        {handler.LastPath}");
    Console.WriteLine($"  Authorization: {handler.LastAuthorization}");
    Console.WriteLine($"  Payload:     {handler.LastBody}");
    Console.WriteLine();

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

        if (!string.IsNullOrWhiteSpace(handler.LastBody))
        {
            try
            {
                using var payload = JsonDocument.Parse(handler.LastBody);
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

    if (handler.CallCount == 0)
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

static string GetValue(IReadOnlyDictionary<string, string?> parameters, string key, string fallback)
    => parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value!
        : fallback;

static string? GetOptionalValue(IReadOnlyDictionary<string, string?> parameters, string key)
    => parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

sealed class FakeGraphHandler : HttpMessageHandler
{
    public int CallCount { get; private set; }
    public string? LastPath { get; private set; }
    public string? LastAuthorization { get; private set; }
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastPath = request.RequestUri?.ToString();
        LastAuthorization = request.Headers.Authorization?.ToString();
        LastBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent($"{{\"id\":\"smoke-{Guid.NewGuid():N}\",\"createdDateTime\":\"{DateTimeOffset.UtcNow:O}\"}}", Encoding.UTF8, "application/json")
        };
        return response;
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
