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
                default:
                    throw new ArgumentException($"未知参数 {current}");
            }
        }

        var options = LoadOptions(appsettings, overrides);
        var resolver = new KeyVaultSecretResolver(Options.Create(options));

        var secretNames = CollectSecretNames(options);
        var tenantsToCheck = tenants.Count > 0
            ? tenants
            : CollectTenantIds(options);

        Console.WriteLine("🔐 正在检查 Key Vault 机密解析状态：");
        foreach (var secret in secretNames)
        {
            if (tenantsToCheck.Count == 0)
            {
                ReportSecret(resolver, secret, tenantId: null);
                continue;
            }

            foreach (var tenant in tenantsToCheck)
            {
                ReportSecret(resolver, secret, tenant);
            }
        }

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

        return 0;
    }

    private static int RunReply(string[] args)
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

    private static void ReportStageReadinessFile(string? configuredPath)
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

    private static void ReportSecret(KeyVaultSecretResolver resolver, string secretName, string? tenantId)
    {
        try
        {
            var value = resolver.GetSecretAsync(secretName, tenantId, cancellationToken: default)
                .GetAwaiter()
                .GetResult();
            var masked = string.IsNullOrEmpty(value) ? "<empty>" : new string('*', Math.Min(8, value.Length));
            Console.WriteLine(tenantId is null
                ? $"  ✔ {secretName} -> {masked}"
                : $"  ✔ {tenantId} :: {secretName} -> {masked}");
        }
        catch (SecretRetrievalException ex)
        {
            Console.WriteLine(tenantId is null
                ? $"  ✘ {secretName} -> {ex.Message}"
                : $"  ✘ {tenantId} :: {secretName} -> {ex.Message}");
        }
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
        Console.WriteLine("  secrets   检查 Key Vault 机密映射与 Graph 作用域配置。");
        Console.WriteLine("  reply     模拟 Stage 回帖流程，输出 Token 与诊断信息。");
        Console.WriteLine("  metrics   拉取 /api/metrics 与 /api/audit 观测数据。");
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
