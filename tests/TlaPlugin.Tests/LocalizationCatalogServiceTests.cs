using System.Linq;
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

        var catalog = service.GetCatalog("xx-YY");

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
        Assert.Equal("无法加载项目状态，仪表盘展示的是缓存数据。", catalog.Strings["tla.toast.dashboard.status"]);
    }

    [Fact]
    public void ReturnsEnglishCatalogWhenRequested()
    {
        var service = new LocalizationCatalogService();

        var catalog = service.GetCatalog("en-US");

        Assert.Equal("en-US", catalog.Locale);
        Assert.Equal("Translation Result", catalog.Strings["tla.ui.card.title"]);
        Assert.Equal("English (United States)", catalog.DisplayName);
        Assert.Equal("Unable to load project status. Showing cached dashboard data.", catalog.Strings["tla.toast.dashboard.status"]);
    }

    [Fact]
    public void ListsAvailableLocalesWithDefaultAndCoreLanguages()
    {
        var service = new LocalizationCatalogService();

        var locales = service.GetAvailableLocales();

        Assert.True(locales.Count >= 30);

        Assert.Contains(locales, locale => locale.Locale == "ja-JP" && locale.IsDefault && locale.DisplayName == "日本語 (日本)");

        var englishLocale = locales.First(locale => locale.Locale == "en-US");
        Assert.False(englishLocale.IsDefault);
        Assert.Equal("English (United States)", englishLocale.DisplayName);

        var spanishLocale = locales.First(locale => locale.Locale == "es-ES");
        Assert.Contains("Spanish", spanishLocale.DisplayName);
    }
}
