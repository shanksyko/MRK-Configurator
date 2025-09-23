using System;
using System.Collections.Generic;

namespace Mieruka.Core.Models;

/// <summary>
/// Describes automation settings used to perform scripted logins on web pages.
/// </summary>
public sealed record class LoginProfile
{
    /// <summary>
    /// Username applied to the login form.
    /// </summary>
#pragma warning disable CA1056 // Uri properties should not be strings - username is sensitive and stored securely
    public string? Username { get; init; }
#pragma warning restore CA1056

    /// <summary>
    /// Password applied to the login form.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Optional CSS/XPath selector that targets the username field.
    /// </summary>
    public string? UserSelector { get; init; }

    /// <summary>
    /// Optional CSS/XPath selector that targets the password field.
    /// </summary>
    public string? PassSelector { get; init; }

    /// <summary>
    /// Optional CSS/XPath selector that triggers the submission.
    /// </summary>
    public string? SubmitSelector { get; init; }

    /// <summary>
    /// Optional JavaScript snippet executed after the credentials are filled.
    /// The username and password are provided as <c>arguments[0]</c> and <c>arguments[1]</c>.
    /// </summary>
    public string? Script { get; init; }

    /// <summary>
    /// Maximum time, in seconds, spent looking for the login elements.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// Hint identifiers used to detect single sign-on dialogs.
    /// </summary>
    public IList<string> SsoHints { get; init; } = new List<string>();
}
