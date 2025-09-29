using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;
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
builder.Services.AddSingleton<TranslationRouter>();
builder.Services.AddSingleton<TranslationPipeline>();
builder.Services.AddSingleton<MessageExtensionHandler>();
builder.Services.AddSingleton<ConfigurationSummaryService>();
builder.Services.AddSingleton<ProjectStatusService>();

var app = builder.Build();

app.MapPost("/api/translate", async (TranslationRequest request, MessageExtensionHandler handler) =>
{
    var result = await handler.HandleTranslateAsync(request);
    return Results.Json(result, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

app.MapPost("/api/offline-draft", async (OfflineDraftRequest request, MessageExtensionHandler handler) =>
{
    var result = await handler.HandleOfflineDraftAsync(request);
    return Results.Json(result, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

app.MapGet("/api/configuration", (ConfigurationSummaryService service) =>
{
    var summary = service.CreateSummary();
    return Results.Json(summary, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

app.MapGet("/api/glossary", (GlossaryService glossary) =>
{
    return Results.Json(glossary.GetEntries(), options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

app.MapGet("/api/audit", (AuditLogger auditLogger) =>
{
    return Results.Json(auditLogger.Export(), options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

app.MapGet("/api/status", (ProjectStatusService statusService) =>
{
    var snapshot = statusService.GetSnapshot();
    return Results.Json(snapshot, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

app.MapGet("/api/metrics", (UsageMetricsService metrics) =>
{
    var report = metrics.GetReport();
    return Results.Json(report, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

app.Run();
