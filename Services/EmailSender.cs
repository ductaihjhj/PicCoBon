using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using PickleballClub.Configuration;

namespace PickleballClub.Services;

public sealed class EmailSendMessage
{
    public string ToEmail { get; init; } = string.Empty;
    public string? ToName { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string TextBody { get; init; } = string.Empty;
    public string? HtmlBody { get; init; }
}

public sealed class EmailSendResult
{
    public bool Sent { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static EmailSendResult Ok() => new() { Sent = true };

    public static EmailSendResult Fail(string code, string message) => new()
    {
        Sent = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}

public interface IEmailSender
{
    (bool IsReady, string ErrorCode, string ErrorMessage) ValidateConfiguration();
    Task<EmailSendResult> SendSingleEmailAsync(EmailSendMessage message, CancellationToken cancellationToken = default);
}

public sealed class MailKitEmailSender : IEmailSender
{
    private readonly IOptionsMonitor<EmailSmtpOptions> _optionsMonitor;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(
        IOptionsMonitor<EmailSmtpOptions> optionsMonitor,
        ILogger<MailKitEmailSender>? logger = null)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger ?? NullLogger<MailKitEmailSender>.Instance;
    }

    public (bool IsReady, string ErrorCode, string ErrorMessage) ValidateConfiguration()
    {
        return _optionsMonitor.CurrentValue.Validate();
    }

    public async Task<EmailSendResult> SendSingleEmailAsync(
        EmailSendMessage message,
        CancellationToken cancellationToken = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var (isValid, errCode, errMsg) = opts.Validate();
        if (!isValid)
        {
            return EmailSendResult.Fail(errCode, errMsg);
        }

        string recipient = (message.ToEmail ?? string.Empty).Trim();
        if (!EmailSmtpOptions.IsValidEmailFormat(recipient))
        {
            return EmailSendResult.Fail("RECIPIENT_INVALID", "Địa chỉ email người nhận không hợp lệ.");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(opts.GetEffectiveFromName(), opts.FromAddress.Trim()));
        mime.To.Clear();
        mime.To.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(message.ToName) ? recipient : message.ToName.Trim(),
            recipient));
        mime.Subject = message.Subject ?? string.Empty;

        var bodyBuilder = new BodyBuilder
        {
            TextBody = message.TextBody ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            bodyBuilder.HtmlBody = message.HtmlBody;
        }
        mime.Body = bodyBuilder.ToMessageBody();

        int timeoutSeconds = opts.GetEffectiveTimeoutSeconds();
        var secureSocketOptions = string.Equals(opts.Security.Trim(), "SslOnConnect", StringComparison.OrdinalIgnoreCase)
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        using var client = new SmtpClient
        {
            Timeout = timeoutSeconds * 1000
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await client.ConnectAsync(opts.Host.Trim(), opts.Port, secureSocketOptions, timeoutCts.Token);
            await client.AuthenticateAsync(opts.Username.Trim(), opts.Password, timeoutCts.Token);
            await client.SendAsync(mime, timeoutCts.Token);
            await client.DisconnectAsync(true, timeoutCts.Token);
            return EmailSendResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex,
                "SMTP timeout after {TimeoutSeconds}s (Host={Host}, Port={Port}, Security={Security})",
                timeoutSeconds, opts.Host.Trim(), opts.Port, secureSocketOptions);
            return EmailSendResult.Fail("SMTP_TIMEOUT",
                "Máy chủ gửi email phản hồi quá thời gian chờ. Vui lòng bấm Gửi lại mã sau ít giây.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SMTP send error (Host={Host}, Port={Port}, Security={Security}, ExceptionType={ExceptionType})",
                opts.Host.Trim(), opts.Port, secureSocketOptions, ex.GetType().Name);
            return EmailSendResult.Fail("SMTP_SEND_FAILED",
                "Tạm thời chưa thể gửi email xác minh từ máy chủ. Vui lòng thử gửi lại sau ít phút.");
        }
    }
}
