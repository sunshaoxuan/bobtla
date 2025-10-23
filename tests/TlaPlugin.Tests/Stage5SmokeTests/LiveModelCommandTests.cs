using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Stage5Program = Program;

namespace TlaPlugin.Tests.Stage5SmokeTests;

public class LiveModelCommandTests
{
    [Fact]
    public void RunReply_WithUseLiveModel_PrintsProviderFallbackAndSuccessLogs()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var appsettingsPath = Path.Combine(repoRoot, "src", "TlaPlugin", "appsettings.json");
        var overridePath = Path.Combine(repoRoot, "tests", "TlaPlugin.Tests", "Stage5SmokeTests", "stage5-live-model.override.json");

        var handler = new StubMessageHandler();
        var httpFactory = new StaticHttpClientFactory(handler);

        var originalFactory = Stage5Program.LiveModelHarnessFactory;
        Stage5Program.LiveModelHarnessFactory = (options, resolver)
            => new LiveModelSmokeHarness(options, httpClientFactory: httpFactory, secretResolver: resolver);

        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            var exitCode = Stage5Program.RunReply(new[]
            {
                "--appsettings", appsettingsPath,
                "--override", overridePath,
                "--tenant", "contoso.onmicrosoft.com",
                "--user", "user-1",
                "--thread", "thread-1",
                "--text", "Hello world",
                "--language", "ja",
                "--use-live-model"
            });

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Stage5Program.LiveModelHarnessFactory = originalFactory;
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        var output = stdout.ToString();
        Assert.Contains("Provider incomplete 使用回退模型执行 translate", output, StringComparison.Ordinal);
        Assert.Contains("Provider primary 完成 translate 调用", output, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(stderr.ToString()), "Expected stderr to be empty for successful execution.");
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubMessageHandler : HttpMessageHandler
    {
        private int _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);

            var responsePayload = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = _callCount == 1 ? "翻訳結果" : "润色结果"
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(responsePayload);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
