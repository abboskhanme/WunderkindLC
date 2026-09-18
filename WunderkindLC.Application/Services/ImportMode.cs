namespace WunderkindLC.Application.Services;

/// <summary>
/// IMPORT REJIMI — boshqa tizimdan (edutizim) ma'lumot ko'chirilayotgan paytda tizimning
/// O'Z-O'ZIDAN ishlaydigan qismlarini to'xtatib turadi.
///
/// <para><b>Nega kerak:</b> import paytida bazaga yuzlab a'zolik, hisob va to'lov tushadi.
/// Fon xizmatlari esa ularni ko'rib DARHOL ish boshlaydi: oylik hisob yoziladi
/// (<see cref="TuitionService.AccrueDue"/> — startupda va har 12 soatda BUTUN tarixni skanerlaydi,
/// `.claude/rules/membership-periods.md` §6), har yangi hisob uchun ota-onaga avto-xabar ketadi,
/// qarzdorlik eslatmasi SMS'i yuboriladi. Ya'ni yarim ko'chirilgan ma'lumot ustida
/// <b>ota-onalarga yuzlab YOLG'ON qarz xabari</b> jo'nab ketishi mumkin.</para>
///
/// <para><b>Nima to'xtaydi:</b> oylik hisob (accrual), qarzdorlik/dars/tug'ilgan kun/sinov
/// eslatmalari va SMS yuborish (<see cref="EskizService.SendSmsAsync"/> darhol "import rejimi"
/// xatosini qaytaradi). Qolgan hamma narsa — API, panel, hisobotlar — odatdagidek ishlaydi.</para>
///
/// <para><b>Yoqish:</b> <c>IMPORT_MODE=1</c> (yoki <c>Import__Mode=true</c>) muhit o'zgaruvchisi
/// va konteynerni qayta ishga tushirish. Import tugagach O'CHIRISH ESDAN CHIQMASIN — aks holda
/// oylik hisob umuman yozilmay qoladi. Startupda ogohlantirish logi yoziladi.</para>
/// </summary>
public static class ImportMode
{
    /// <summary>Yoqilganmi. <see cref="Configure"/> chaqirilmasa — false (odatiy ish).</summary>
    public static bool Enabled { get; private set; }

    /// <summary>Startupda bir marta chaqiriladi (<c>Program.cs</c>).</summary>
    public static void Configure(IConfiguration config)
    {
        var raw = config["Import:Mode"] ?? config["IMPORT_MODE"];
        Enabled = raw is not null
            && (raw.Equals("1", StringComparison.Ordinal)
                || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Testlar uchun (qiymatni qo'lda o'rnatish).</summary>
    public static void SetForTests(bool enabled) => Enabled = enabled;
}
