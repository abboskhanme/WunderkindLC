using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «Chegirma (kelmagan darslar)» — to'lov kiritiladigan HAR yo'lda bor (2026-10-13: Moliya → «Yangi moliyaviy amal» →
/// o'quvchi to'lovida yo'q edi). Uch kanal — o'quvchi profili (`students.list`), Kassa (`kassa`), Moliya (`finance.main`) —
/// bir xil 4 ta marshrut va mantiq BITTA joyda (`LessonDiscountHttp`); ruxsat — o'sha yo'lda to'lov kiritish ruxsati.
/// (Test loyihasi Server'ga bog'lanmagan — manba matnidan.)
/// </summary>
public class LessonDiscountChannelsTests
{
    private static string Src(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, "WunderkindLC.Server", "Controllers", .. parts]));
    }

    [Theory]
    [InlineData("StudentsController.cs", "", "[AdminPerm(\"students.list\")]")]
    [InlineData("KassaController.cs", "students/", "[AdminPerm(\"kassa\")]")]
    [InlineData("FinanceController.LessonDiscounts.cs", "students/", "[AdminPerm(\"finance.main\")]")]
    public void Har_kanalda_4_marshrut_va_yagona_mantiq(string file, string prefix, string classPerm)
    {
        var src = Src(file);
        foreach (var route in new[] { "{id}/lesson-discounts\")]", "{id}/lesson-discounts/quote\")]",
                     "{id}/lesson-discounts/{discountId}/cancel\")]" })
            Assert.Contains(prefix + route, src);
        foreach (var call in new[] { "LessonDiscountHttp.ListAsync(db, id)", "LessonDiscountHttp.QuoteAsync(db, id,",
                     "LessonDiscountHttp.ApplyAsync(db, audit, User, id, req)", "LessonDiscountHttp.CancelAsync(db, audit, User, id, discountId)" })
            Assert.Contains(call, src);
        // Class-level gate (Finance: on the partial's main file).
        var gate = file.StartsWith("FinanceController") ? Src("FinanceController.cs") : src;
        Assert.Contains(classPerm, gate);
        Assert.DoesNotContain("AllowAnonymous", src);
    }
}
