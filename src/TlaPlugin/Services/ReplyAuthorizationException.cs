using System;

namespace TlaPlugin.Services;

/// <summary>
/// 在回复线程时缺乏授权的异常。
/// </summary>
public class ReplyAuthorizationException : Exception
{
    public ReplyAuthorizationException(string message) : base(message)
    {
    }
}
