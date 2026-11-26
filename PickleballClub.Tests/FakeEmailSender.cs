using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PickleballClub.Services;

namespace PickleballClub.Tests;

public sealed record CapturedEmail(
    string ToEmail,
    string? ToName,
    string Subject,
    string TextBody,
    string? HtmlBody,
    DateTime CapturedAtUtc)
{
    private static readonly Regex SixDigitOtpRegex = new(@"\b(\d{6})\b", RegexOptions.Compiled);

    public string ExtractOtpCode()
    {
        var match = SixDigitOtpRegex.Match(TextBody ?? string.Empty);
        if (!match.Success)
        {
            throw new InvalidOperationException("No 6-digit OTP code found in captured email TextBody.");
        }
        return match.Groups[1].Value;
    }
}

public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<CapturedEmail> _messages = new();
    private Func<EmailSendMessage, EmailSendResult?>? _interceptor;

    public bool IsConfigured { get; set; } = true;
    public string UnconfiguredCode { get; set; } = "SMTP_PASSWORD_MISSING";
    public string UnconfiguredMessage { get; set; } = "Chưa cấu hình Application Password cho hộp thư SMTP.";

    public IReadOnlyList<CapturedEmail> SentMessages => _messages.ToArray();

    public void Clear()
    {
        while (_messages.TryDequeue(out _))
        {
        }
    }

    public void SetInterceptor(Func<EmailSendMessage, EmailSendResult?>? interceptor)
    {
        _interceptor = interceptor;
    }

    public (bool IsReady, string ErrorCode, string ErrorMessage) ValidateConfiguration()
    {
        if (!IsConfigured)
        {
            return (false, UnconfiguredCode, UnconfiguredMessage);
        }
        return (true, string.Empty, string.Empty);
    }

    public Task<EmailSendResult> SendSingleEmailAsync(EmailSendMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConfigured)
        {
            return Task.FromResult(EmailSendResult.Fail(UnconfiguredCode, UnconfiguredMessage));
        }

        var intercept = _interceptor?.Invoke(message);
        if (intercept != null && !intercept.Sent)
        {
            return Task.FromResult(intercept);
        }

        _messages.Enqueue(new CapturedEmail(
            message.ToEmail,
            message.ToName,
            message.Subject,
            message.TextBody,
            message.HtmlBody,
            DateTime.UtcNow));

        return Task.FromResult(EmailSendResult.Ok());
    }
}

public sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public AdjustableTimeProvider(DateTimeOffset? initialUtcNow = null)
    {
        _utcNow = initialUtcNow ?? new DateTimeOffset(2026, 11, 26, 1, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta)
    {
        _utcNow = _utcNow.Add(delta);
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        _utcNow = value;
    }
}
