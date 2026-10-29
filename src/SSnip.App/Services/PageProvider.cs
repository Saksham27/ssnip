using Wpf.Ui.Abstractions;

namespace SSnip.App.Services;

/// <summary>Lets the navigation view resolve pages from the DI container.</summary>
public sealed class PageProvider(IServiceProvider services) : INavigationViewPageProvider
{
    public object? GetPage(Type pageType) => services.GetService(pageType);
}
