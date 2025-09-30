using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class LocalizationCatalogServiceTests
{
    [Fact]
    public void ReturnsDefaultJapaneseCatalogWhenLocaleMissing()
    {
        var service = new LocalizationCatalogService();

        var catalog = service.GetCatalog(null);

        Assert.Equal("ja-JP", catalog.Locale);
        Assert.Equal("ja-JP", catalog.DefaultLocale);
        Assert.Equal("翻訳結果", catalog.Strings["tla.ui.card.title"]);
        Assert.Equal("日本語 (日本)", catalog.DisplayName);
    }

    [Fact]
    public void FallbacksToDefaultWhenLocaleUnknown()
    {
        var service = new LocalizationCatalogService();

        var catalog = service.GetCatalog("es-ES");

        Assert.Equal("ja-JP", catalog.Locale);
        Assert.Equal("翻訳結果", catalog.Strings["tla.ui.card.title"]);
        Assert.Equal("日本語 (日本)", catalog.DisplayName);
    }

    [Fact]
    public void ReturnsChineseOverridesWhenRequested()
    {
        var service = new LocalizationCatalogService();

        var catalog = service.GetCatalog("zh-CN");

        Assert.Equal("zh-CN", catalog.Locale);
        Assert.Equal("额外翻译", catalog.Strings["tla.ui.card.additional"]);
        Assert.Equal("翻訳結果", catalog.Strings["tla.ui.card.title"]);
        Assert.Equal("简体中文 (中国)", catalog.DisplayName);
    }

    [Fact]
    public void ListsAvailableLocalesWithDefaultFirst()
    {
        var service = new LocalizationCatalogService();

        var locales = service.GetAvailableLocales();

        Assert.Collection(locales,
            first =>
            {
                Assert.Equal("ja-JP", first.Locale);
                Assert.True(first.IsDefault);
                Assert.Equal("日本語 (日本)", first.DisplayName);
            },
            second =>
            {
                Assert.Equal("zh-CN", second.Locale);
                Assert.False(second.IsDefault);
                Assert.Equal("简体中文 (中国)", second.DisplayName);
            });
    }
}
