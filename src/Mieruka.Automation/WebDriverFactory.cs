using OpenQA.Selenium;

namespace Mieruka.Automation;

public sealed class WebDriverFactory
{
    public IWebDriver Create(Func<IWebDriver> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory();
    }
}
