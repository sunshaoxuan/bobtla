using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Providers;

namespace TlaPlugin.Services;

/// <summary>
/// 根据配置组装模型提供方的工厂。
/// </summary>
public class ModelProviderFactory
{
    private readonly PluginOptions _options;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly KeyVaultSecretResolver? _secretResolver;
    private readonly ILoggerFactory? _loggerFactory;

    public ModelProviderFactory(
        IOptions<PluginOptions>? options = null,
        IHttpClientFactory? httpClientFactory = null,
        KeyVaultSecretResolver? secretResolver = null,
        ILoggerFactory? loggerFactory = null)
    {
        _options = options?.Value ?? new PluginOptions();
        _httpClientFactory = httpClientFactory;
        _secretResolver = secretResolver;
        _loggerFactory = loggerFactory;
    }

    public IReadOnlyList<IModelProvider> CreateProviders()
    {
        if (_options.Providers.Count == 0)
        {
            _options.Providers.Add(new ModelProviderOptions { Id = "mock-primary", TranslationPrefix = "[Mock]" });
            _options.Providers.Add(new ModelProviderOptions { Id = "mock-backup", TranslationPrefix = "[Backup]", SimulatedFailures = 1 });
        }

        var providers = new List<IModelProvider>();
        foreach (var opt in _options.Providers)
        {
            providers.Add(CreateProvider(opt));
        }

        return providers;
    }

    private IModelProvider CreateProvider(ModelProviderOptions options)
    {
        return options.Kind switch
        {
            ModelProviderKind.Mock => new MockModelProvider(options),
            ModelProviderKind.OpenAi or ModelProviderKind.Anthropic or ModelProviderKind.Groq or ModelProviderKind.OpenWebUi or ModelProviderKind.Ollama or ModelProviderKind.Custom
                => new ConfigurableChatModelProvider(options, _httpClientFactory, _secretResolver, _loggerFactory?.CreateLogger<ConfigurableChatModelProvider>()),
            _ => new MockModelProvider(options)
        };
    }
}
