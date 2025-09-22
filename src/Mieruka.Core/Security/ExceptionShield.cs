using System;
using System.IO;
using System.Security.Cryptography;
using OpenQA.Selenium;
using Serilog;

namespace Mieruka.Core.Security;

/// <summary>
/// Provides sanitized exception messages that can be safely surfaced to operators.
/// </summary>
public sealed class ExceptionShield
{
    private readonly ILogger? _logger;
    private readonly AuditLog? _auditLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExceptionShield"/> class.
    /// </summary>
    public ExceptionShield(ILogger? logger = null, AuditLog? auditLog = null)
    {
        _logger = logger;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Maps the supplied exception to a sanitized message and optionally logs it.
    /// </summary>
    public ShieldedException Shield(Exception exception, string context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = exception switch
        {
            CryptographicException => "Falha ao acessar segredo protegido.",
            IOException => "Não foi possível acessar o recurso solicitado.",
            WebDriverException => "O WebDriver encontrou uma condição inesperada.",
            _ => "Ocorreu um erro inesperado.",
        };

        var sanitizedDetails = Redaction.Redact(exception.Message);
        _logger?.Error(exception, "Erro protegido ({Context}): {Message}", context, sanitizedDetails);
        _auditLog?.WriteEvent(new AuditLog.AuditEvent("exception")
        {
            SiteId = context,
            Result = "shielded",
        });

        return new ShieldedException(message, exception);
    }
}

/// <summary>
/// Represents a sanitized exception wrapper.
/// </summary>
public sealed class ShieldedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShieldedException"/> class.
    /// </summary>
    public ShieldedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
