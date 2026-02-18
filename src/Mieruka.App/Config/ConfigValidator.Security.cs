using System;
using System.Collections.Generic;
using System.IO;
using Mieruka.Core.Models;
using Mieruka.Core.Security;
using Mieruka.Core.Security.Policy;

namespace Mieruka.App.Config;

internal sealed partial class ConfigValidator
{
    private void ValidateSecurity(GeneralConfig config, ICollection<ConfigValidationIssue> issues)
    {
        var policy = new SecurityPolicy(SecurityProfile.Standard);
        try
        {
            policy.Validate();
        }
        catch (Exception ex)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Política de segurança inválida: {ex.Message}"));
        }

        ValidateAllowlist(config.Sites, issues);
        ValidateLoginProfiles(config.Sites, issues);
        ValidateProfileDirectories(config.Sites, issues);
        ValidateStrictTls(config.Sites, policy, issues);
    }

    private static void ValidateAllowlist(IReadOnlyList<SiteConfig> sites, ICollection<ConfigValidationIssue> issues)
    {
        if (sites is null)
        {
            return;
        }

        foreach (var site in sites)
        {
            if (site?.AllowedTabHosts is null)
            {
                continue;
            }

            foreach (var host in site.AllowedTabHosts)
            {
                if (string.IsNullOrWhiteSpace(host))
                {
                    continue;
                }

                try
                {
                    InputSanitizer.SanitizeHost(host);
                }
                catch (Exception ex)
                {
                    issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Host inválido '{host}' no site '{site.Id}': {ex.Message}", site.Id));
                }
            }
        }
    }

    private static void ValidateLoginProfiles(IReadOnlyList<SiteConfig> sites, ICollection<ConfigValidationIssue> issues)
    {
        if (sites is null)
        {
            return;
        }

        foreach (var site in sites)
        {
            if (site?.Login is not { } login)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(login.Username))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"O site '{site.Id}' contém um usuário armazenado no JSON. Migrar para o Cofre de Credenciais.", site.Id));
            }

            if (!string.IsNullOrWhiteSpace(login.Password))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"O site '{site.Id}' contém uma senha em texto claro. Migrar para o Cofre de Credenciais.", site.Id));
            }

            ValidateSelector(site.Id, login.UserSelector, nameof(login.UserSelector), issues);
            ValidateSelector(site.Id, login.PassSelector, nameof(login.PassSelector), issues);
            ValidateSelector(site.Id, login.SubmitSelector, nameof(login.SubmitSelector), issues);

            try
            {
                InputSanitizer.EnsureSafeAscii(login.Script ?? string.Empty, 2048, nameof(login.Script));
            }
            catch (Exception ex)
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Script de login inválido no site '{site.Id}': {ex.Message}", site.Id));
            }
        }
    }

    private static void ValidateSelector(string siteId, string? selector, string field, ICollection<ConfigValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return;
        }

        try
        {
            InputSanitizer.SanitizeSelector(selector, 512);
        }
        catch (Exception ex)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Selector '{field}' inválido no site '{siteId}': {ex.Message}", siteId));
        }
    }

    private void ValidateProfileDirectories(IReadOnlyList<SiteConfig> sites, ICollection<ConfigValidationIssue> issues)
    {
        if (sites is null)
        {
            return;
        }

        foreach (var site in sites)
        {
            if (site is null)
            {
                continue;
            }

            ValidateDirectory(site.Id, site.UserDataDirectory, nameof(site.UserDataDirectory), issues);
            ValidateDirectory(site.Id, site.ProfileDirectory, nameof(site.ProfileDirectory), issues);
        }
    }

    private void ValidateDirectory(string siteId, string? path, string field, ICollection<ConfigValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            InputSanitizer.SanitizePath(path, _baseDirectory);
        }
        catch (Exception ex)
        {
            issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Diretório inválido em '{field}' para o site '{siteId}': {ex.Message}", siteId));
        }
    }

    private static void ValidateStrictTls(IReadOnlyList<SiteConfig> sites, SecurityPolicy policy, ICollection<ConfigValidationIssue> issues)
    {
        if (!policy.StrictTls || sites is null)
        {
            return;
        }

        foreach (var site in sites)
        {
            if (site is null || string.IsNullOrWhiteSpace(site.Url))
            {
                continue;
            }

            if (Uri.TryCreate(site.Url, UriKind.Absolute, out var uri) && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ConfigValidationIssue(ConfigValidationSeverity.Warning, $"TLS estrito ativo: o site '{site.Id}' utiliza '{uri.Scheme}'.", site.Id));
            }
        }
    }
}
