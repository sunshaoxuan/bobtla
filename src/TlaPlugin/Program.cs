using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Nodes;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using TlaPlugin.Teams;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: true);

builder.Services.Configure<PluginOptions>(builder.Configuration.GetSection("Plugin"));

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
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
builder.Services.AddSingleton<ITokenBroker, TokenBroker>();
builder.Services.AddSingleton<ModelProviderFactory>();
builder.Services.AddSingleton<UsageMetricsService>();
builder.Services.AddSingleton<LocalizationCatalogService>();
builder.Services.AddSingleton<TranslationRouter>();
builder.Services.AddSingleton<TranslationPipeline>();
builder.Services.AddSingleton<MessageExtensionHandler>();
builder.Services.AddSingleton<ConfigurationSummaryService>();
builder.Services.AddSingleton<ProjectStatusService>();
builder.Services.AddSingleton<DevelopmentRoadmapService>();
builder.Services.AddSingleton<ReplyService>();
builder.Services.AddSingleton<RewriteService>();
builder.Services.AddSingleton<CostEstimatorService>();
builder.Services.AddSingleton<McpToolRegistry>();
builder.Services.AddSingleton<McpServer>();

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

app.MapPost("/api/offline-draft", async (OfflineDraftRequest request, MessageExtensionHandler handler) =>
{
    var result = await handler.HandleOfflineDraftAsync(request);
    return Results.Json(result, options: jsonOptions);
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

app.Run();

public partial class Program;
