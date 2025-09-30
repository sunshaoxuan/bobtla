using System;

namespace TlaPlugin.Services;

/// <summary>
/// 表示术语应用阶段的业务异常。
/// </summary>
public class GlossaryApplicationException : Exception
{
    public GlossaryApplicationException(string message) : base(message)
    {
    }
}
