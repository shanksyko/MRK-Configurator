using System;
using Mieruka.Core.Models;

namespace Mieruka.Automation.Login;

public sealed class LoginOrchestrator
{
    public bool EnsureLoggedIn(SiteConfig site)
    {
        ArgumentNullException.ThrowIfNull(site);

        // TODO: integrar Selenium, CookieBridge e SessionVerifier respeitando UrlAllowlist.
        return false;
    }
}
