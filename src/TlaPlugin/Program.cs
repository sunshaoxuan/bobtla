using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Graph;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using TlaPlugin.Teams;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: true);

builder.Services.Configure<PluginOptions>(builder.Configuration.GetSection("Plugin"));

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IGraphRequestContextAccessor, GraphRequestContextAccessor>();
builder.Services.AddSingleton<ITokenBroker, TokenBroker>();
builder.Services.AddSingleton<IAuthenticationProvider>(provider =>
{
    var tokenBroker = provider.GetRequiredService<ITokenBroker>();
    var contextAccessor = provider.GetRequiredService<IGraphRequestContextAccessor>();
    return new BrokeredGraphAuthenticationProvider(tokenBroker, contextAccessor);
});
builder.Services.AddSingleton<GraphServiceClient>(provider =>
{
    var authenticationProvider = provider.GetRequiredService<IAuthenticationProvider>();
    return new GraphServiceClient(authenticationProvider);
});
builder.Services.AddSingleton<ITeamsMessageClient, GraphTeamsMessageClient>();
builder.Services.AddHttpClient<ITeamsReplyClient, TeamsReplyClient>((provider, client) =>
{
    var options = provider.GetService<IOptions<PluginOptions>>();
    var security = options?.Value?.Security;
    if (security is not null)
    {
        if (!string.IsNullOrWhiteSpace(security.GraphBaseUrl) && Uri.TryCreate(security.GraphBaseUrl, UriKind.Absolute, out var baseUri))
        {
            client.BaseAddress = baseUri;
        }

        if (security.GraphTimeout > TimeSpan.Zero)
        {
            client.Timeout = security.GraphTimeout;
        }
    }
})
.ConfigurePrimaryHttpMessageHandler(provider =>
{
    var options = provider.GetService<IOptions<PluginOptions>>();
    var security = options?.Value?.Security;
    if (security is not null && !string.IsNullOrWhiteSpace(security.GraphProxy))
    {
        return new HttpClientHandler
        {
            Proxy = new WebProxy(security.GraphProxy),
            UseProxy = true
        };
    }

    return new HttpClientHandler();
});
builder.Services.AddSingleton(provider =>
{
    var glossary = new GlossaryService();
    glossary.LoadEntries(new[]
    {
        new GlossaryEntry("CPU", "中央处理器", "tenant:contoso"),
        new GlossaryEntry("compliance", "合规性", "tenant:contoso"),
        new GlossaryEntry("budget", "预算", "channel:finance"),
        new GlossaryEntry("reply", "回复", "user:admin")
    });
    return glossary;
});

builder.Services.AddSingleton<ToneTemplateService>();
builder.Services.AddSingleton<LanguageDetector>();
builder.Services.AddSingleton(provider => new ComplianceGateway(provider.GetService<IOptions<PluginOptions>>()));
builder.Services.AddSingleton(provider => new BudgetGuard(provider.GetService<IOptions<PluginOptions>>()?.Value));
builder.Services.AddSingleton<AuditLogger>();
builder.Services.AddSingleton(provider => new OfflineDraftStore(provider.GetService<IOptions<PluginOptions>>()));
builder.Services.AddSingleton<TranslationCache>();
builder.Services.AddSingleton<TranslationThrottle>();
builder.Services.AddSingleton<KeyVaultSecretResolver>();
builder.Services.AddSingleton<ModelProviderFactory>();
builder.Services.AddSingleton<UsageMetricsService>();
builder.Services.AddSingleton<LocalizationCatalogService>();
builder.Services.AddSingleton<ContextRetrievalService>();
builder.Services.AddSingleton<TranslationRouter>();
builder.Services.AddSingleton<ITranslationPipeline, TranslationPipeline>();
builder.Services.AddSingleton<MessageExtensionHandler>();
builder.Services.AddSingleton<ConfigurationSummaryService>();
builder.Services.AddSingleton<ProjectStatusService>();
builder.Services.AddSingleton<DevelopmentRoadmapService>();
builder.Services.AddSingleton<ReplyService>();
builder.Services.AddSingleton<RewriteService>();
builder.Services.AddSingleton<CostEstimatorService>();
builder.Services.AddSingleton<McpToolRegistry>();
builder.Services.AddSingleton<McpServer>();
builder.Services.AddSingleton<MetadataService>();

var app = builder.Build();

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
static bool TryAuthorize(HttpRequest request, out IResult? unauthorized)
{
    if (!request.Headers.TryGetValue("Authorization", out var header) || string.IsNullOrWhiteSpace(header))
    {
        unauthorized = Results.Unauthorized();
        return false;
    }

    unauthorized = null;
    return true;
}

app.MapPost("/api/translate", async (TranslationRequest request, MessageExtensionHandler handler) =>
{
    var result = await handler.HandleTranslateAsync(request);
    return Results.Json(result, options: jsonOptions);
});

app.MapPost("/api/offline-draft", async (HttpRequest httpRequest, OfflineDraftRequest request, MessageExtensionHandler handler) =>
{
    if (!TryAuthorize(httpRequest, out var unauthorized))
    {
        return unauthorized!;
    }

    if (request is null
        || string.IsNullOrWhiteSpace(request.OriginalText)
        || string.IsNullOrWhiteSpace(request.UserId)
        || string.IsNullOrWhiteSpace(request.TenantId))
    {
        return Results.BadRequest(new { error = "OriginalText, UserId and TenantId are required." });
    }

    var result = await handler.HandleOfflineDraftAsync(request);
    return Results.Json(result, options: jsonOptions, statusCode: StatusCodes.Status201Created);
});

app.MapGet("/api/offline-draft", (HttpRequest httpRequest, OfflineDraftStore store) =>
{
    if (!TryAuthorize(httpRequest, out var unauthorized))
    {
        return unauthorized!;
    }

    if (!httpRequest.Query.TryGetValue("userId", out var userId) || string.IsNullOrWhiteSpace(userId))
    {
        return Results.BadRequest(new { error = "userId is required." });
    }

    var drafts = store.ListDrafts(userId!);
    return Results.Json(new { drafts }, options: jsonOptions);
});

app.MapPost("/api/detect", (LanguageDetectionRequest request, LanguageDetector detector, IOptions<PluginOptions> options) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "Text is required." });
    }

    if (request.Text.Length > options.Value.MaxCharactersPerRequest)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    var detection = detector.Detect(request.Text);
    return Results.Json(detection, options: jsonOptions);
});

app.MapPost("/api/rewrite", async (RewriteRequest request, RewriteService service, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.RewriteAsync(request, cancellationToken);
        return Results.Json(new
        {
            rewrittenText = result.RewrittenText,
            modelId = result.ModelId,
            cost = result.CostUsd,
            latencyMs = result.LatencyMs
        }, options: jsonOptions);
    }
    catch (BudgetExceededException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status402PaymentRequired);
    }
    catch (AuthenticationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
    }
    catch (TranslationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/apply-glossary", (GlossaryApplicationRequest request, GlossaryService glossary) =>
{
    var policy = Enum.TryParse<GlossaryPolicy>(request.Policy, true, out var parsed) ? parsed : GlossaryPolicy.Fallback;
    try
    {
        var result = glossary.ApplyDetailed(request.Text, request.TenantId, request.ChannelId, request.UserId, policy, request.GlossaryIds);
        return Results.Json(new
        {
            processedText = result.ProcessedText,
            matches = result.Matches
        }, options: jsonOptions);
    }
    catch (GlossaryApplicationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
});

app.MapGet("/mcp/tools/list", (HttpRequest request, McpServer server) =>
{
    if (!TryAuthorize(request, out var unauthorized))
    {
        return unauthorized!;
    }

    var tools = server.ListTools();
    return Results.Json(new { tools }, options: jsonOptions);
});

app.MapPost("/mcp/tools/call", async (HttpRequest request, McpServer server, CancellationToken cancellationToken) =>
{
    if (!TryAuthorize(request, out var unauthorized))
    {
        return unauthorized!;
    }

    JsonObject arguments;
    string? toolName;
    try
    {
        using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("name", out var nameProperty) || nameProperty.ValueKind != JsonValueKind.String)
        {
            return Results.BadRequest(new { error = "name is required." });
        }

        toolName = nameProperty.GetString();
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return Results.BadRequest(new { error = "name is required." });
        }

        if (root.TryGetProperty("arguments", out var argumentsProperty))
        {
            var parsed = JsonNode.Parse(argumentsProperty.GetRawText());
            arguments = parsed as JsonObject ?? new JsonObject();
        }
        else
        {
            arguments = new JsonObject();
        }
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Invalid JSON payload." });
    }

    try
    {
        var result = await server.CallToolAsync(toolName!, arguments, cancellationToken);
        return Results.Json(new { result }, options: jsonOptions);
    }
    catch (McpValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (GlossaryApplicationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (ReplyAuthorizationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (BudgetExceededException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status402PaymentRequired);
    }
    catch (AuthenticationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
    }
    catch (TranslationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "Tool not found." });
    }
});

app.MapPost("/api/reply", async (ReplyRequest request, ReplyService service, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await service.SendReplyAsync(request, cancellationToken);
        return Results.Json(result, options: jsonOptions);
    }
    catch (ReplyAuthorizationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (BudgetExceededException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status402PaymentRequired);
    }
    catch (AuthenticationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
    }
    catch (TranslationException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/metadata", (MetadataService metadataService) =>
{
    var metadata = metadataService.CreateMetadata();
    return Results.Json(metadata, options: jsonOptions);
});

app.MapGet("/api/cost-latency", (int payloadSize, string modelId, CostEstimatorService estimator) =>
{
    try
    {
        var estimate = estimator.Estimate(payloadSize, modelId);
        return Results.Json(estimate, options: jsonOptions);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapGet("/api/configuration", (ConfigurationSummaryService service) =>
{
    var summary = service.CreateSummary();
    return Results.Json(summary, options: jsonOptions);
});

app.MapGet("/api/glossary", (GlossaryService glossary) =>
{
    return Results.Json(glossary.GetEntries(), options: jsonOptions);
});

app.MapPost("/api/glossary/upload", async (HttpRequest request, GlossaryService glossary, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "需要 multipart/form-data 请求。" });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "请选择包含术语的文件。" });
    }

    var overwriteRaw = form["overwrite"].ToString();
    var overwrite = bool.TryParse(overwriteRaw, out var parsed)
        ? parsed
        : string.Equals(overwriteRaw, "on", StringComparison.OrdinalIgnoreCase);

    var scope = ResolveScope(form);
    if (string.IsNullOrWhiteSpace(scope))
    {
        return Results.BadRequest(new { error = "必须提供术语作用域。" });
    }

    List<GlossaryUploadEntry> entries;
    List<string> parseErrors;
    try
    {
        (entries, parseErrors) = await ParseGlossaryEntriesAsync(file, cancellationToken);
    }
    catch (FormatException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    if (entries.Count == 0)
    {
        return Results.BadRequest(new { error = "文件中未找到有效术语。", errors = parseErrors });
    }

    GlossaryUploadResult result;
    try
    {
        result = glossary.ImportEntries(scope, entries, overwrite);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    var combinedErrors = parseErrors.Concat(result.Errors).ToList();

    return Results.Json(new
    {
        imported = result.ImportedCount,
        updated = result.UpdatedCount,
        conflicts = result.Conflicts,
        errors = combinedErrors
    }, options: jsonOptions);
});

app.MapGet("/api/audit", (AuditLogger auditLogger) =>
{
    return Results.Json(auditLogger.Export(), options: jsonOptions);
});

app.MapGet("/api/status", (ProjectStatusService statusService) =>
{
    var snapshot = statusService.GetSnapshot();
    return Results.Json(snapshot, options: jsonOptions);
});

app.MapGet("/api/metrics", (UsageMetricsService metrics) =>
{
    var report = metrics.GetReport();
    return Results.Json(report, options: jsonOptions);
});

app.MapGet("/api/localization/locales", (LocalizationCatalogService localization) =>
{
    var locales = localization.GetAvailableLocales();
    return Results.Json(locales, options: jsonOptions);
});

app.MapGet("/api/localization/catalog/{locale?}", (string? locale, LocalizationCatalogService localization) =>
{
    var catalog = localization.GetCatalog(locale);
    return Results.Json(catalog, options: jsonOptions);
});

app.MapGet("/api/roadmap", (DevelopmentRoadmapService roadmapService) =>
{
    var roadmap = roadmapService.GetRoadmap();
    return Results.Json(roadmap, options: jsonOptions);
});


static string? ResolveScope(IFormCollection form)
{
    var scope = form["scope"].ToString();
    if (!string.IsNullOrWhiteSpace(scope))
    {
        return scope.Trim();
    }

    var scopeType = form["scopeType"].ToString();
    if (string.IsNullOrWhiteSpace(scopeType))
    {
        return null;
    }

    var identifier = form["scopeId"].ToString();
    if (string.IsNullOrWhiteSpace(identifier))
    {
        identifier = scopeType switch
        {
            "tenant" => form["tenantId"].ToString(),
            "channel" => form["channelId"].ToString(),
            "user" => form["userId"].ToString(),
            _ => identifier
        };
    }

    if (string.IsNullOrWhiteSpace(identifier))
    {
        return null;
    }

    return string.Concat(scopeType.Trim(), ":", identifier.Trim());
}

static async Task<(List<GlossaryUploadEntry> Entries, List<string> Errors)> ParseGlossaryEntriesAsync(IFormFile file, CancellationToken cancellationToken)
{
    await using var buffer = new MemoryStream();
    await file.CopyToAsync(buffer, cancellationToken);
    buffer.Position = 0;

    if (IsTermBase(file.FileName))
    {
        return ParseTermBase(buffer);
    }

    buffer.Position = 0;
    return ParseCsv(buffer);
}

static bool IsTermBase(string? fileName)
{
    if (string.IsNullOrWhiteSpace(fileName))
    {
        return false;
    }

    return fileName.EndsWith(".tbx", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
}

static (List<GlossaryUploadEntry> Entries, List<string> Errors) ParseCsv(Stream stream)
{
    var entries = new List<GlossaryUploadEntry>();
    var errors = new List<string>();
    stream.Position = 0;
    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
    string? line;
    var row = 0;
    var headerSkipped = false;

    while ((line = reader.ReadLine()) is not null)
    {
        row++;
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var cells = SplitCsvLine(line);
        if (!headerSkipped && LooksLikeHeader(cells))
        {
            headerSkipped = true;
            continue;
        }

        var source = cells.Length > 0 ? cells[0] : string.Empty;
        var target = cells.Length > 1 ? cells[1] : string.Empty;
        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(target))
        {
            errors.Add($"行 {row}: 未提供源词或译文。");
            continue;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (cells.Length > 2 && !string.IsNullOrWhiteSpace(cells[2]))
        {
            metadata["note"] = cells[2];
        }

        entries.Add(new GlossaryUploadEntry(source, target, metadata));
    }

    return (entries, errors);
}

static (List<GlossaryUploadEntry> Entries, List<string> Errors) ParseTermBase(Stream stream)
{
    var entries = new List<GlossaryUploadEntry>();
    var errors = new List<string>();
    stream.Position = 0;
    XDocument document;
    try
    {
        document = XDocument.Load(stream);
    }
    catch (Exception ex)
    {
        throw new FormatException($"TermBase 文件解析失败: {ex.Message}");
    }

    var ns = document.Root?.Name.Namespace ?? XNamespace.None;
    var xmlNs = XNamespace.Get("http://www.w3.org/XML/1998/namespace");
    var index = 0;

    foreach (var entry in document.Descendants(ns + "termEntry"))
    {
        index++;
        var langSets = entry.Elements(ns + "langSet").ToList();
        if (langSets.Count < 2)
        {
            errors.Add($"词条 {index}: 缺少目标语言集。");
            continue;
        }

        var sourceTerm = langSets[0].Descendants(ns + "term").FirstOrDefault()?.Value?.Trim();
        var targetTerm = langSets[1].Descendants(ns + "term").FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(sourceTerm) || string.IsNullOrWhiteSpace(targetTerm))
        {
            errors.Add($"词条 {index}: 缺少源词或译文。");
            continue;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceLang = langSets[0].Attribute(xmlNs + "lang")?.Value ?? langSets[0].Attribute("lang")?.Value;
        var targetLang = langSets[1].Attribute(xmlNs + "lang")?.Value ?? langSets[1].Attribute("lang")?.Value;
        if (!string.IsNullOrWhiteSpace(sourceLang))
        {
            metadata["sourceLang"] = sourceLang!;
        }
        if (!string.IsNullOrWhiteSpace(targetLang))
        {
            metadata["targetLang"] = targetLang!;
        }

        var note = entry.Descendants(ns + "note").FirstOrDefault()?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(note))
        {
            metadata["note"] = note!;
        }

        entries.Add(new GlossaryUploadEntry(sourceTerm!, targetTerm!, metadata));
    }

    return (entries, errors);
}

static string[] SplitCsvLine(string line)
{
    var values = new List<string>();
    var builder = new StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < line.Length; i++)
    {
        var current = line[i];
        if (current == '"')
        {
            if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
            {
                builder.Append('"');
                i++;
                continue;
            }

            inQuotes = !inQuotes;
            continue;
        }

        if (current == ',' && !inQuotes)
        {
            values.Add(builder.ToString().Trim());
            builder.Clear();
            continue;
        }

        builder.Append(current);
    }

    values.Add(builder.ToString().Trim());
    return values.ToArray();
}

static bool LooksLikeHeader(string[] cells)
{
    if (cells.Length < 2)
    {
        return false;
    }

    var first = cells[0].Trim().ToLowerInvariant();
    var second = cells[1].Trim().ToLowerInvariant();
    return (first.Contains("source") && second.Contains("target"))
        || (first.Contains("term") && second.Contains("translation"));
}


app.Run();

public partial class Program;
