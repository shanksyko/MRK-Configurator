using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OpenQA.Selenium;

namespace Mieruka.Tests.TestDoubles;

internal sealed class FakeWebDriver : IWebDriver
{
    private readonly Dictionary<string, FakeWindow> _windows = new(StringComparer.Ordinal);
    private string? _currentHandle;

    public FakeWebDriver(params FakeWindow[] windows)
    {
        foreach (var window in windows)
        {
            _windows[window.Handle] = window;
        }

        _currentHandle = _windows.Keys.FirstOrDefault();
    }

    public string Url
    {
        get => GetCurrentWindow().Url;
        set => GetCurrentWindow().Url = value;
    }

    public string Title => GetCurrentWindow().Title;

    public string PageSource => string.Empty;

    public string CurrentWindowHandle
    {
        get
        {
            if (_currentHandle is null || !_windows.ContainsKey(_currentHandle))
            {
                throw new NoSuchWindowException("No active window.");
            }

            return _currentHandle;
        }
    }

    public ReadOnlyCollection<string> WindowHandles
        => new(_windows.Keys.ToList());

    public void Close()
    {
        if (_currentHandle is null || !_windows.Remove(_currentHandle))
        {
            throw new NoSuchWindowException("The current window is not available.");
        }

        _currentHandle = _windows.Keys.FirstOrDefault();
    }

    public void Quit()
    {
        _windows.Clear();
        _currentHandle = null;
    }

    public IOptions Manage()
        => throw new NotSupportedException();

    public INavigation Navigate()
        => throw new NotSupportedException();

    public ITargetLocator SwitchTo()
        => new TargetLocator(this);

    public IWebElement FindElement(By by)
        => throw new NotSupportedException();

    public ReadOnlyCollection<IWebElement> FindElements(By by)
        => throw new NotSupportedException();

    public void Dispose()
        => Quit();

    public void SetUrl(string handle, string url)
    {
        if (!_windows.TryGetValue(handle, out var window))
        {
            throw new NoSuchWindowException($"Window '{handle}' does not exist.");
        }

        window.Url = url;
    }

    private FakeWindow GetCurrentWindow()
    {
        if (_currentHandle is null || !_windows.TryGetValue(_currentHandle, out var window))
        {
            throw new NoSuchWindowException("No active window.");
        }

        return window;
    }

    private sealed class TargetLocator : ITargetLocator
    {
        private readonly FakeWebDriver _driver;

        public TargetLocator(FakeWebDriver driver)
        {
            _driver = driver;
        }

        public IWebDriver Frame(int frameIndex)
            => throw new NotSupportedException();

        public IWebDriver Frame(string frameName)
            => throw new NotSupportedException();

        public IWebDriver Frame(IWebElement frameElement)
            => throw new NotSupportedException();

        public IWebDriver ParentFrame()
            => throw new NotSupportedException();

        public IWebDriver Window(string windowName)
        {
            if (!_driver._windows.ContainsKey(windowName))
            {
                throw new NoSuchWindowException($"Window '{windowName}' does not exist.");
            }

            _driver._currentHandle = windowName;
            return _driver;
        }

        public IWebDriver DefaultContent()
            => throw new NotSupportedException();

        public IWebElement ActiveElement()
            => throw new NotSupportedException();

        public IAlert Alert()
            => throw new NotSupportedException();

        public IWebDriver NewWindow(WindowType typeHint)
            => throw new NotSupportedException();
    }
}

internal sealed class FakeWindow
{
    public FakeWindow(string handle, string url)
    {
        Handle = handle;
        Url = url;
        Title = handle;
    }

    public string Handle { get; }

    public string Url { get; set; }

    public string Title { get; set; }
}
