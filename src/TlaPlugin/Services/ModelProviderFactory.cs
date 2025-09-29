using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Providers;

namespace TlaPlugin.Services;

/// <summary>
/// 構成からモデル提供者を組み立てるファクトリ。
/// </summary>
public class ModelProviderFactory
{
    private readonly PluginOptions _options;

    public ModelProviderFactory(IOptions<PluginOptions>? options = null)
    {
        _options = options?.Value ?? new PluginOptions();
    }

    public IReadOnlyList<IModelProvider> CreateProviders()
    {
        if (_options.Providers.Count == 0)
        {
            _options.Providers.Add(new ModelProviderOptions { Id = "mock-primary", TranslationPrefix = "[Mock]" });
            _options.Providers.Add(new ModelProviderOptions { Id = "mock-backup", TranslationPrefix = "[Backup]", SimulatedFailures = 1 });
        }

        return _options.Providers.Select(opt => (IModelProvider)new MockModelProvider(opt)).ToList();
    }
}
