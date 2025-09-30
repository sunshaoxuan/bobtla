using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace TlaPlugin.Services;

/// <summary>
/// 空认证提供器，允许在本地或测试环境创建 Graph 客户端。
/// </summary>
public sealed class AnonymousAuthenticationProvider : IAuthenticationProvider
{
    public Task AuthenticateRequestAsync(
        RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
