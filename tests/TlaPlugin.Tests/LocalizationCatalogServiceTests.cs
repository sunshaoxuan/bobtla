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
    }

    [Fact]
    public void FallbacksToDefaultWhenLocaleUnknown()
    {
        var service = new LocalizationCatalogService();

        var catalog = service.GetCatalog("es-ES");

        Assert.Equal("ja-JP", catalog.Locale);
        Assert.Equal("翻訳結果", catalog.Strings["tla.ui.card.title"]);
    }

    [Fact]
    public void ReturnsChineseOverridesWhenRequested()
    {
        var service = new LocalizationCatalogService();

        var catalog = service.GetCatalog("zh-CN");

        Assert.Equal("zh-CN", catalog.Locale);
        Assert.Equal("额外翻译", catalog.Strings["tla.ui.card.additional"]);
        Assert.Equal("翻訳結果", catalog.Strings["tla.ui.card.title"]);
    }
}
