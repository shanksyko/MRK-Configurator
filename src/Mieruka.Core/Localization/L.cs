using System;
using System.Globalization;
using System.Resources;

namespace Mieruka.Core.Localization;

/// <summary>
/// Provides localized string access. Falls back to the key itself when no translation is found.
/// </summary>
public static class L
{
    private static ResourceManager? _resourceManager;
    private static CultureInfo _culture = CultureInfo.CurrentUICulture;

    /// <summary>
    /// Gets or sets the current culture for localization.
    /// </summary>
    public static CultureInfo Culture
    {
        get => _culture;
        set => _culture = value ?? CultureInfo.CurrentUICulture;
    }

    /// <summary>
    /// Initializes the localization system with the resource manager for the Strings resources.
    /// </summary>
    public static void Initialize(ResourceManager resourceManager)
    {
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
    }

    /// <summary>
    /// Sets the active culture by name (e.g., "pt-BR", "en-US").
    /// </summary>
    public static void SetCulture(string cultureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName);
        _culture = new CultureInfo(cultureName);
        CultureInfo.CurrentUICulture = _culture;
    }

    /// <summary>
    /// Retrieves the localized string for the given key.
    /// Returns the key itself if no translation is found.
    /// </summary>
    public static string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key ?? string.Empty;
        }

        if (_resourceManager is null)
        {
            return key;
        }

        try
        {
            return _resourceManager.GetString(key, _culture) ?? key;
        }
        catch
        {
            return key;
        }
    }

    /// <summary>
    /// Retrieves the localized string and formats it with the given arguments.
    /// </summary>
    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(_culture, template, args);
        }
        catch
        {
            return template;
        }
    }
}
