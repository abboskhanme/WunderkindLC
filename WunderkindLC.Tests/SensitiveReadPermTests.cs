using System.Text.RegularExpressions;
using WunderkindLC.Application.Dtos;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// NOZIK HUJJAT qaytaradigan bo'limlarda O'QISH darvozalanganini qo'riqlaydi.
///
/// <para>Muammo: <c>/uploads</c> papkasi autentifikatsiyasiz beriladi — manzilni bir marta olgan
/// odam faylni ABADIY, hatto ishdan bo'shatilgandan keyin ham ola oladi. <c>AdminPerm</c> da esa
/// xodim (staff) uchun BARCHA GET so'rovlari ruxsat tekshiruvisiz o'tadi (bu ataylab — bo'limlararo
/// o'qish uchun). Natijada tor ruxsatli xodim shartnoma PDF/DOCX, nomzod CV'lari va o'quvchi ovoz
/// yozuvlarining manzillarini yig'ib olishi mumkin edi. Yechim:
/// <c>[AdminPerm("bolim", ReadRequiresPerm = true)]</c>.</para>
///
/// <para><b>NEGA MANBA MATNI TEKSHIRILADI:</b> <c>WunderkindLC.Tests</c> loyihasi
/// <c>WunderkindLC.Server</c> ga referens QILMAYDI (faqat Domain/Application/Infrastructure).
/// Ya'ni <c>AdminPermAttribute</c> ni ham, controllerlarni ham bu yerdan chaqirib bo'lmaydi, va
/// faqat shu test uchun sun'iy referens qo'shilmaydi. Shuning uchun darvoza controller manba
/// faylidan o'qib tekshiriladi — kimdir bayroqni olib tashlasa test darrov qizaradi.</para>
/// </summary>
public class SensitiveReadPermTests
{
    /// <summary>Repo ildizi — <c>WunderkindLC.slnx</c> yotgan papka (bin/ dan yuqoriga chiqib topiladi).</summary>
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo ildizi (WunderkindLC.slnx) topilmadi");
            return dir!.FullName;
        }
    }

    private static string ControllerSource(string fileName)
    {
        var yol = Path.Combine(RepoRoot, "WunderkindLC.Server", "Controllers", fileName);
        Assert.True(File.Exists(yol), $"Controller fayli topilmadi: {yol}");
        return File.ReadAllText(yol);
    }

    /// <summary>Klass darajasidagi <c>[AdminPerm("section", … ReadRequiresPerm = true …)]</c> bormi.</summary>
    private static bool ReadIsGated(string source, string section) =>
        Regex.IsMatch(
            source,
            @"\[AdminPerm\(\s*""" + Regex.Escape(section) + @"""[^\]]*\bReadRequiresPerm\s*=\s*true\b[^\]]*\)\]");

    // =============================================================================================
    //  Darvoza QO'YILGANMI
    // =============================================================================================

    /// <summary>
    /// Shartnomalar: <c>GET api/admin/contracts</c> javobida <c>PdfUrl</c>/<c>DocxUrl</c> —
    /// tayyor shartnomaning <c>/uploads</c> dagi PDF va Word nusxasi qaytadi.
    /// </summary>
    [Fact]
    public void Shartnomalar_oqishi_ruxsat_talab_qiladi()
    {
        Assert.True(
            ReadIsGated(ControllerSource("ContractsController.cs"), "contracts"),
            "ContractsController da [AdminPerm(\"contracts\", ReadRequiresPerm = true)] yo'q — " +
            "shartnoma PDF/DOCX manzillarini har qanday xodim o'qiy oladi");
    }

    /// <summary>
    /// Karyera: <c>GET api/admin/career/applications</c> javobida <c>CvUrl</c> —
    /// nomzodning <c>/uploads</c> dagi rezyume (PDF) manzili qaytadi.
    /// </summary>
    [Fact]
    public void Karyera_arizalari_oqishi_ruxsat_talab_qiladi()
    {
        Assert.True(
            ReadIsGated(ControllerSource("CareerController.cs"), "vacancies"),
            "CareerController da [AdminPerm(\"vacancies\", ReadRequiresPerm = true)] yo'q — " +
            "nomzod CV manzillarini har qanday xodim o'qiy oladi");
    }

    /// <summary>
    /// AI tekshiruv: <c>GET api/admin/ai-check/item/{id}</c> javobida <c>AudioUrl</c> —
    /// o'quvchining <c>/uploads</c> dagi OVOZ YOZUVI manzili qaytadi.
    /// </summary>
    [Fact]
    public void AiCheck_oqishi_ruxsat_talab_qiladi()
    {
        Assert.True(
            ReadIsGated(ControllerSource("AiCheckController.cs"), "app.aiCheck"),
            "AiCheckController da [AdminPerm(\"app.aiCheck\", ReadRequiresPerm = true)] yo'q — " +
            "o'quvchi ovoz yozuvlari manzilini har qanday xodim o'qiy oladi");
    }

    // =============================================================================================
    //  Darvoza NEGA kerakligi — DTO'larda hujjat manzili haqiqatan bormi
    // =============================================================================================

    /// <summary>
    /// Yuqoridagi darvozalarning SABABI: shu uchta DTO javobda <c>/uploads</c> manzilini olib
    /// chiqadi. Maydon nomi o'zgarsa yoki olib tashlansa — test qizaradi va darvoza qarori qayta
    /// ko'rib chiqiladi (ortiqcha bo'lib qolgan bo'lishi ham mumkin).
    /// </summary>
    [Theory]
    [InlineData(typeof(ContractDocDto), "PdfUrl")]
    [InlineData(typeof(ContractDocDto), "DocxUrl")]
    [InlineData(typeof(JobApplicationDto), "CvUrl")]
    [InlineData(typeof(AiCheckDto), "AudioUrl")]
    public void Nozik_dto_hujjat_manzilini_qaytaradi(Type dto, string maydon)
    {
        Assert.True(
            dto.GetProperty(maydon) is not null,
            $"{dto.Name}.{maydon} topilmadi — darvoza (ReadRequiresPerm) qarori qayta ko'rilsin");
    }
}
