using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TlaPlugin.Services;

/// <summary>
/// Stores MCP tool metadata and exposes schema definitions for discovery.
/// </summary>
public class McpToolRegistry
{
    private readonly IReadOnlyDictionary<string, McpToolDefinition> _definitions;

    public McpToolRegistry()
    {
        _definitions = CreateDefinitions();
    }

    public IReadOnlyCollection<McpToolDefinition> ListTools() => _definitions.Values;

    public bool TryGetDefinition(string name, out McpToolDefinition definition)
    {
        return _definitions.TryGetValue(name, out definition!);
    }

    private static IReadOnlyDictionary<string, McpToolDefinition> CreateDefinitions()
    {
        var definitions = new[]
        {
            new McpToolDefinition(
                "tla.translate",
                "Translate text into the specified target language while applying configured glossary policies.",
                CreateTranslationSchema(),
                new[] { "text", "targetLanguage", "tenantId", "userId" }),
            new McpToolDefinition(
                "tla.detectLanguage",
                "Detect the language of supplied text for a tenant.",
                CreateDetectionSchema(),
                new[] { "text", "tenantId" }),
            new McpToolDefinition(
                "tla.applyGlossary",
                "Apply glossary substitutions and return the matches for auditing.",
                CreateGlossarySchema(),
                new[] { "text", "tenantId", "userId" }),
            new McpToolDefinition(
                "tla.replyInThread",
                "Post a reply in an existing thread applying tone or glossary policies as needed.",
                CreateReplySchema(),
                new[] { "threadId", "replyText", "tenantId", "userId" })
        };

        return definitions.ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static JsonObject CreateTranslationSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("text", "targetLanguage", "tenantId", "userId"),
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["sourceLanguage"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["targetLanguage"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 2
                },
                ["tenantId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["userId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["channelId"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["tone"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["useGlossary"] = new JsonObject
                {
                    ["type"] = "boolean"
                },
                ["uiLocale"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["glossaryDecisions"] = new JsonObject
                {
                    ["type"] = "object"
                },
                ["additionalTargetLanguages"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" }
                }
            }
        };
    }

    private static JsonObject CreateDetectionSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("text", "tenantId"),
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["tenantId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                }
            }
        };
    }

    private static JsonObject CreateGlossarySchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("text", "tenantId", "userId"),
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["tenantId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["userId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["channelId"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["policy"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["glossaryIds"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" }
                }
            }
        };
    }

    private static JsonObject CreateReplySchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("threadId", "replyText", "tenantId", "userId"),
            ["properties"] = new JsonObject
            {
                ["threadId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["replyText"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["text"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["editedText"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["tenantId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["userId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                },
                ["channelId"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["language"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["uiLocale"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["languagePolicy"] = new JsonObject
                {
                    ["type"] = "object"
                }
            }
        };
    }
}

public sealed class McpToolDefinition
{
    public McpToolDefinition(string name, string description, JsonObject inputSchema, IReadOnlyList<string> requiredProperties)
    {
        Name = name;
        Description = description;
        InputSchema = inputSchema;
        RequiredProperties = requiredProperties;
    }

    public string Name { get; }

    public string Description { get; }

    [JsonPropertyName("input_schema")]
    public JsonObject InputSchema { get; }

    [JsonIgnore]
    public IReadOnlyList<string> RequiredProperties { get; }
}
