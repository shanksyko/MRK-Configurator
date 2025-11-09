using System;
using System.Security;
using System.Windows.Forms;
using Mieruka.Core.Security;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Services;

public sealed class UiSecretsBridge
{
    private readonly SecretsProvider _provider;

    public UiSecretsBridge(SecretsProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public void Save(string siteId, WinForms.TextBoxBase? usernameBox, WinForms.TextBoxBase? passwordBox, WinForms.TextBoxBase? totpBox)
    {
        using var username = Extract(usernameBox);
        using var password = Extract(passwordBox);
        using var totp = Extract(totpBox);

        _provider.SaveCredentials(siteId, username, password);
        _provider.SetTotp(siteId, totp);
    }

    public SecureString? LoadUser(string siteId)
        => CopyOrNull(_provider.GetUsernameFor(siteId));

    public SecureString? LoadPass(string siteId)
        => CopyOrNull(_provider.GetPasswordFor(siteId));

    public SecureString? LoadTotp(string siteId)
        => CopyOrNull(_provider.GetTotpFor(siteId));

    public void Delete(string siteId)
    {
        _provider.Delete(siteId);
    }

    private static SecureString? Extract(WinForms.TextBoxBase? textBox)
    {
        if (textBox is null)
        {
            return null;
        }

        var text = textBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var chars = text.ToCharArray();
        try
        {
            var secure = new SecureString();
            foreach (var ch in chars)
            {
                secure.AppendChar(ch);
            }

            secure.MakeReadOnly();
            return secure;
        }
        finally
        {
            Array.Clear(chars, 0, chars.Length);
            textBox.Clear();
        }
    }

    private static SecureString? CopyOrNull(SecureString? secret)
    {
        if (secret is null)
        {
            return null;
        }

        try
        {
            var copy = secret.Copy();
            copy.MakeReadOnly();
            return copy;
        }
        finally
        {
            secret.Dispose();
        }
    }
}
