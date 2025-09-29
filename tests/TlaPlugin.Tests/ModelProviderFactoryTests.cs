using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Providers;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class ModelProviderFactoryTests
{
    [Fact]
    public void CreatesConfigurableProviderForOpenAi()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions>
            {
                new()
                {
                    Id = "openai",
                    Kind = ModelProviderKind.OpenAi,
                    Endpoint = "https://api.openai.com/v1/chat/completions",
                    ApiKeySecretName = "openai-api-key",
                    Model = "gpt-4o-mini"
                }
            },
            Security = new SecurityOptions
            {
                SeedSecrets = new Dictionary<string, string>
                {
                    ["openai-api-key"] = "test"
                }
            }
        });

        var factory = new ModelProviderFactory(options, new StubHttpClientFactory(), new KeyVaultSecretResolver(options));
        var providers = factory.CreateProviders();
        Assert.IsType<ConfigurableChatModelProvider>(providers.Single());
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHandler());
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}")
                };
                return Task.FromResult(response);
            }
        }
    }
}
