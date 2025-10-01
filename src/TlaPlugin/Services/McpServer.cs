using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Models;
using TlaPlugin.Teams;

namespace TlaPlugin.Services;

/// <summary>
/// Handles Model Context Protocol tool discovery and invocation.
/// </summary>
public class McpServer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly McpToolRegistry _registry;
    private readonly MessageExtensionHandler _messageExtension;
    private readonly ITranslationPipeline _pipeline;
    private readonly GlossaryService _glossary;
    private readonly ReplyService _replyService;

    public McpServer(
        McpToolRegistry registry,
        MessageExtensionHandler messageExtension,
        ITranslationPipeline pipeline,
        GlossaryService glossary,
        ReplyService replyService)
    {
        _registry = registry;
        _messageExtension = messageExtension;
        _pipeline = pipeline;
        _glossary = glossary;
        _replyService = replyService;
    }

    public IReadOnlyCollection<McpToolDefinition> ListTools()
    {
        return _registry.ListTools();
    }

    public async Task<JsonNode> CallToolAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (!_registry.TryGetDefinition(name, out var definition))
        {
            throw new KeyNotFoundException($"Tool '{name}' was not found.");
        }

        ValidateArguments(definition, arguments);

        return name switch
        {
            "tla.translate" => await InvokeTranslateAsync(arguments, cancellationToken),
            "tla.detectLanguage" => await InvokeDetectAsync(arguments, cancellationToken),
            "tla.applyGlossary" => InvokeGlossary(arguments),
            "tla.replyInThread" => await InvokeReplyAsync(arguments, cancellationToken),
            _ => throw new KeyNotFoundException($"Tool '{name}' was not found.")
        };
    }

    private static void ValidateArguments(McpToolDefinition definition, JsonObject arguments)
    {
        foreach (var required in definition.RequiredProperties)
        {
            if (!arguments.TryGetPropertyValue(required, out var value) || value is null)
            {
                throw new McpValidationException($"Missing required property '{required}'.");
            }

            if (value is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<string?>(out var text))
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new McpValidationException($"Property '{required}' must be a non-empty string.");
                    }
                }
            }
        }
    }

    private async Task<JsonNode> InvokeTranslateAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var request = arguments.Deserialize<TranslationRequest>(SerializerOptions)
            ?? throw new McpValidationException("Invalid translation payload.");

        var response = await _messageExtension.HandleTranslateAsync(request);
        return response;
    }

    private async Task<JsonNode> InvokeDetectAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var request = arguments.Deserialize<LanguageDetectionRequest>(SerializerOptions)
            ?? throw new McpValidationException("Invalid detection payload.");

        var detection = await _pipeline.DetectAsync(request, cancellationToken);
        return JsonSerializer.SerializeToNode(detection, SerializerOptions)
            ?? throw new InvalidOperationException("Failed to serialize detection result.");
    }

    private JsonNode InvokeGlossary(JsonObject arguments)
    {
        var request = arguments.Deserialize<GlossaryApplicationRequest>(SerializerOptions)
            ?? throw new McpValidationException("Invalid glossary payload.");

        var policy = Enum.TryParse<GlossaryPolicy>(request.Policy, true, out var parsed)
            ? parsed
            : GlossaryPolicy.Fallback;

        var result = _glossary.ApplyDetailed(
            request.Text,
            request.TenantId,
            request.ChannelId,
            request.UserId,
            policy,
            request.GlossaryIds);

        return JsonSerializer.SerializeToNode(new
        {
            processedText = result.ProcessedText,
            matches = result.Matches
        }, SerializerOptions) ?? throw new InvalidOperationException("Failed to serialize glossary result.");
    }

    private async Task<JsonNode> InvokeReplyAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var request = arguments.Deserialize<ReplyRequest>(SerializerOptions)
            ?? throw new McpValidationException("Invalid reply payload.");

        var result = await _replyService.SendReplyAsync(request, cancellationToken);
        return JsonSerializer.SerializeToNode(result, SerializerOptions)
            ?? throw new InvalidOperationException("Failed to serialize reply result.");
    }
}

public class McpValidationException : Exception
{
    public McpValidationException(string message) : base(message)
    {
    }
}
