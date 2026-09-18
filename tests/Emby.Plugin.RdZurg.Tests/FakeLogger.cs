using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.RdZurg.Tests;

/// <summary>
/// Emby's logger formats with <see cref="string.Format(IFormatProvider, string, object?[])"/>, so a message
/// written in the Microsoft template style ("{Count} items") throws at runtime - including inside catch blocks.
/// Every log call the tests reach goes through the real formatter here, so such a message fails the test.
/// </summary>
internal sealed class FakeLogger : ILogger
{
    public List<string> Messages { get; } = new();

    public void Info(string message, params object[] paramList) => Record(message, paramList);

    public void Error(string message, params object[] paramList) => Record(message, paramList);

    public void Warn(string message, params object[] paramList) => Record(message, paramList);

    public void Debug(string message, params object[] paramList) => Record(message, paramList);

    public void Fatal(string message, params object[] paramList) => Record(message, paramList);

    public void FatalException(string message, Exception exception, params object[] paramList) => Record(message, paramList);

    public void ErrorException(string message, Exception exception, params object[] paramList) => Record(message, paramList);

    public void LogMultiline(string message, LogSeverity severity, StringBuilder additionalContent) => Record(message, Array.Empty<object>());

    public void Log(LogSeverity severity, string message, params object[] paramList) => Record(message, paramList);

    public void Log(LogSeverity severity, ReadOnlyMemory<char> message) => Messages.Add(message.ToString());

    public void Error(ReadOnlyMemory<char> message) => Messages.Add(message.ToString());

    public void Warn(ReadOnlyMemory<char> message) => Messages.Add(message.ToString());

    public void Info(ReadOnlyMemory<char> message) => Messages.Add(message.ToString());

    public void Debug(ReadOnlyMemory<char> message) => Messages.Add(message.ToString());

    private void Record(string message, object[] paramList)
        => Messages.Add(paramList is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, message, paramList)
            : message);
}
