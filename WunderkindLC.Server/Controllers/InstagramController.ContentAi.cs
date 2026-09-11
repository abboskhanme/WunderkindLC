using Microsoft.AspNetCore.Mvc;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// MARKETING → INSTAGRAM KONTENT: <b>AI BILAN CAPTION YOZISH</b> (§5.10).
///
/// <para>Bu <see cref="InstagramController"/> ning DAVOMI (<c>partial</c>): marshrut prefiksi
/// (<c>api/admin/instagram</c>) va sinf darajasidagi <c>[AdminPerm("marketing",
/// ReadRequiresPerm = true)]</c> asosiy fayldan MEROS bo'ladi. Yozish amali esa kontent
/// sahifasining kaliti bilan — <c>marketing.content</c> (<see cref="ContentPerm"/>).</para>
///
/// <para><b>Nima uchun kerak:</b> post matnini har safar noldan yozish SMM ishining eng ko'p
/// vaqt oladigan qismi. Markazning bilim bazasi (<c>IgKnowledge</c>) allaqachon to'ldirilgan —
/// kurslar, narxlar, uslub — ya'ni AI matnni AYNAN shu markaz haqida yoza oladi.</para>
///
/// <para><b>Butun mantiq <see cref="InstagramCaptionService"/> da</b> (Application qatlami,
/// sof funksiyalar testlangan). Controller faqat HTTP tarjimasi — bu yerda "yagona manba"
/// qoidasi: chegaralarni qo'llash kodini controllerga ko'chirish uni testdan chiqarib
/// yuborardi.</para>
///
/// <para><b>⚠️ AUDITGA YOZILMAYDI:</b> endpoint hech qanday ma'lumotni o'zgartirmaydi, faqat
/// matn taklif qiladi (<c>audit.md</c> §3.5 dagi "AI tahlili" istisnosi bilan bir xil).
/// Matn haqiqatan ishlatilsa, u post SAQLANGANDA auditga tushadi.</para>
///
/// <para><b>⚠️ CHEGARALAR AI'DAN KEYIN QO'LLANADI:</b> caption ≤2200 belgi, ≤30 hashtag,
/// ≤20 mention (<see cref="InstagramPublishContract.ValidateCaption"/>). Aks holda
/// foydalanuvchi AI matnini maydonga qo'yib, saqlashda Meta'ning <c>2207010</c> xatosini
/// olardi — ya'ni "yordamchi" tugma muammo yasab bergan bo'lardi.</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>
    /// Mavzudan post matnini (caption) va hashtaglarni yozdiradi.
    ///
    /// <para><b>⚠️ Javob HAR DOIM 200 bo'ladi</b>, muvaffaqiyat esa <c>ok</c> bayrog'ida
    /// (<c>ok=false</c> bo'lsa sabab <c>error</c> da, o'zbek tilida). Sabab: bu yerda
    /// muvaffaqiyatsizlikning aksariyati TASHQI va vaqtinchalik (kalit sozlanmagan, Gemini
    /// timeout, format buzuq) — ularni 4xx/5xx qilib yuborish klientda "so'rov xato ketdi"
    /// degan umumiy matnni chiqarardi, foydalanuvchiga esa AYNAN sabab kerak. Shakl
    /// <c>IgAgent*</c> javoblari bilan ham bir xil.</para>
    ///
    /// <para><b>Tarmoqqa chiqish darvozasi:</b> Gemini kaliti sozlanmagan bo'lsa so'rov
    /// UMUMAN yuborilmaydi — tekshiruv <see cref="InstagramCaptionService.GenerateAsync"/>
    /// ichida, ya'ni chaqiruvchi (kelajakdagi boshqa joy ham) uni o'tkazib yubora olmaydi.</para>
    /// </summary>
    [HttpPost("content/caption")]
    [AdminPerm(ContentPerm)]
    public async Task<ActionResult<IgCaptionResultDto>> GenerateContentCaption(
        IgCaptionPayload? payload, CancellationToken ct)
    {
        var (ok, caption, hashtags, error) = await InstagramCaptionService.GenerateAsync(
            db, config,
            payload?.PostType, payload?.Topic, payload?.Language, payload?.Tone, ct);

        // Xato MATNI log'ga tushadi (kalit, prompt va mavzu emas) — "AI ishlamayapti" murojaati
        // kelganda sabab qidirish uchun. Muvaffaqiyat jimgina o'tadi (kunda o'nlab chaqiruv).
        if (!ok) logger.LogWarning("[instagram] caption yozilmadi: {Error}", error);

        return new IgCaptionResultDto(ok, caption, hashtags, error);
    }

    /// <summary>
    /// AI uchun mavjud uslublar va tillar — frontend tanlash ro'yxatini SHU YERDAN oladi.
    ///
    /// <para>⚠️ ATAYIN alohida GET: kalitlar (<c>friendly</c>, <c>uz-Latn</c> …) ikki joyda
    /// qo'lda yozilsa DRIFT bo'ladi va "tanlash ro'yxati bo'sh" kabi jimgina nosozlik
    /// chiqadi (<c>contacts.md</c> §6 dagi bir xil saboq). Bu yerda YORLIQLAR ham beriladi,
    /// frontend faqat ko'rsatadi.</para>
    ///
    /// <para>O'qish amali — sinf darajasidagi <c>marketing</c> ruxsati yetarli (javobda
    /// maxfiy narsa yo'q, faqat kalit va yorliq).</para>
    /// </summary>
    [HttpGet("content/caption/meta")]
    public ActionResult<IgCaptionMetaDto> ContentCaptionMeta() => new IgCaptionMetaDto(
        Tones: InstagramCaptionService.Tones
            .Select(t => new IgCaptionOptionDto(t, ToneLabel(t)))
            .ToList(),
        Languages: IgConst.Languages
            .Select(l => new IgCaptionOptionDto(l, LanguageLabel(l)))
            .ToList(),
        DefaultTone: InstagramCaptionService.DefaultTone,
        DefaultLanguage: IgConst.DefaultLanguage,
        GeminiConfigured: AppSecrets.GeminiConfigured);

    private static string ToneLabel(string tone) => tone switch
    {
        InstagramCaptionService.ToneExpert => "Ishonchli (ekspert)",
        InstagramCaptionService.ToneEnergetic => "Jonli",
        InstagramCaptionService.ToneSales => "Sotuvga yo'naltirilgan",
        _ => "Samimiy",
    };

    private static string LanguageLabel(string language) => language switch
    {
        "uz-Cyrl" => "Ўзбекча (кирилл)",
        "ru" => "Ruscha",
        "en" => "Inglizcha",
        _ => "O'zbekcha (lotin)",
    };
}

// =================================================================================================
//  DTO'LAR — `IgCaption*` prefiksi (boshqa Instagram partial'lari bilan to'qnashmasin).
// =================================================================================================

/// <summary>
/// Caption so'rovi.
/// </summary>
/// <param name="PostType">`image` | `video` | `reels` | `story` | `carousel` — matn shakli
/// shunga qarab (Reels'da «ilmoq», Story'da 1–2 gap). Noma'lum qiymat `image` ga keltiriladi.</param>
/// <param name="Topic">Foydalanuvchi yozgan MAVZU — yagona majburiy maydon.</param>
/// <param name="Language">`uz-Latn` (default) | `uz-Cyrl` | `ru` | `en`.</param>
/// <param name="Tone">`friendly` (default) | `expert` | `energetic` | `sales`.</param>
public record IgCaptionPayload(string? PostType, string? Topic, string? Language, string? Tone);

/// <summary>
/// Natija.
/// </summary>
/// <param name="Caption">TAYYOR matn — hashtaglar allaqachon oxiriga qo'shilgan va chegaralarga
/// solishtirilgan. Frontend uni maydonga shundoq qo'yadi.</param>
/// <param name="Hashtags">Faqat KO'RSATISH uchun (chiplar). ⚠️ Matnga QAYTA qo'shilmaydi —
/// ular <paramref name="Caption"/> ichida allaqachon bor.</param>
/// <param name="Error">`Ok=false` bo'lsa — foydalanuvchiga ko'rsatiladigan o'zbekcha sabab.</param>
public record IgCaptionResultDto(bool Ok, string Caption, List<string> Hashtags, string Error);

/// <summary>Tanlash ro'yxatining bitta qatori (kalit + o'zbekcha yorliq).</summary>
public record IgCaptionOptionDto(string Id, string Label);

/// <summary>Uslub/til ro'yxatlari + standart qiymatlar. <c>GeminiConfigured=false</c> bo'lsa
/// frontend tugmani OLDINDAN o'chirib qo'yadi (bekorga so'rov ketmasin).</summary>
public record IgCaptionMetaDto(
    List<IgCaptionOptionDto> Tones, List<IgCaptionOptionDto> Languages,
    string DefaultTone, string DefaultLanguage, bool GeminiConfigured);
