using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// E6.6 — MARKETING → <b>JAVOB SIFATI JURNALI</b> ("AI shunday dedi → operator shunday yozdi").
///
/// <para><b>Nima uchun:</b> promptni va bilim bazasini yaxshilashning eng ishonchli manbai —
/// odamning AI javobiga kiritgan TUZATISHI. Sonlar ("nechta javob") mavjud analitikada bor,
/// bu yerda esa <b>MAZMUN</b>: qaysi niyatda AI ko'proq xato qiladi va matn qanday
/// o'zgartiriladi.</para>
///
/// <para><b>Nega alohida partial fayl:</b> <c>InstagramController</c> allaqachon bir necha
/// ekranni boqadi. Marshrut prefiksi, <c>[Authorize]</c> va sinf darajasidagi
/// <c>[AdminPerm("marketing", ReadRequiresPerm = true)]</c> asosiy qismdan MEROS bo'lib
/// keladi — primary constructor parametrlari (<c>db</c>) ham shu yerda ko'rinadi.</para>
///
/// <para>🔴 <b>MAXFIYLIK:</b> javobda <b>mijozning hech qanday belgisi yo'q</b> — na username,
/// na Instagram ID, na telefon, na mijoz yozgan matn. Faqat BIZNING ikki chiquvchi matnimiz
/// (AI taklifi va operator yuborgani) hamda texnik kesimlar. Sabab: bu ICHKI SIFAT ma'lumoti,
/// unga "kim bilan yozishilgani" kerak emas — o'sha savolning joyi Inbox.</para>
///
/// <para>⚠️ <c>ConversationId</c> ham qaytmaydi: u orqali suhbatni ochib mijozni topish mumkin
/// bo'lardi, ya'ni "faqat matnlar" qoidasi bilvosita buzilardi.</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>Ro'yxatda ko'rsatiladigan juftliklar (default). Klient <c>limit</c> bilan
    /// oshirishi mumkin — <see cref="QualityMaxItems"/> gacha.</summary>
    private const int QualityDefaultItems = 50;

    /// <summary>Ro'yxat chegarasi.</summary>
    private const int QualityMaxItems = 200;

    /// <summary>Jamlanma hisoblanadigan qatorlar chegarasi.
    /// <para>⚠️ Jamlanma RO'YXATDAN emas, SHU to'plamdan hisoblanadi (`books.md` dagi bir xil
    /// saboq: sahifalangan ro'yxatdan qo'shib chiqarilgan son noto'g'ri bo'ladi). Chegaradan
    /// oshgani JIM QIRQILMAYDI — javobda <c>Truncated</c> bayrog'i bilan ochiq aytiladi.</para></summary>
    private const int QualityScanLimit = 2000;

    /// <summary>
    /// Javob sifati hisoboti.
    ///
    /// <para>Manba — <c>IgMessage.AiSuggestedText</c> to'ldirilgan chiquvchi xabarlar: ular
    /// operator AI taklifi USTIGA yozgan javoblar (<see cref="IgQualityLog"/>). Taklif AYNAN
    /// qabul qilingan holat ham kiradi (<c>WasEdited = false</c>) — "AI to'g'ri yozdi" ham
    /// o'lchov.</para>
    ///
    /// <para>Sana filtri — loyihadagi konvensiya: ISO satr ustida, <c>to</c> KUN sifatida
    /// beriladi va <c>T23:59:59</c> gacha cho'ziladi.</para>
    ///
    /// <para><b>Filtrlar va ularning QAMROVI</b> (ataylab har xil — sabablar tanadagi izohda):</para>
    /// <list type="table">
    ///   <item><term><c>from</c>/<c>to</c>, <c>channel</c></term>
    ///     <description>BUTUN to'plamga: jamlanma, niyat kesimi va lenta</description></item>
    ///   <item><term><c>intent</c></term>
    ///     <description>jamlanma va lentaga; niyat KESIMIGA emas (kesim — tanlagich)</description></item>
    ///   <item><term><c>onlyEdited</c></term>
    ///     <description>faqat lentaga (ko'rish rejimi, hisobot emas)</description></item>
    /// </list>
    /// <para>⚠️ Noma'lum <c>channel</c> qiymati JIM tashlanadi (filtrsiz qolinadi) — klientdagi
    /// xato kalit tufayli ekran bo'shab qolmasin.</para>
    /// </summary>
    [HttpGet("quality")]
    public async Task<ActionResult<IgQualityDto>> Quality(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? limit,
        [FromQuery] string? intent, [FromQuery] string? channel, [FromQuery] bool? onlyEdited,
        CancellationToken ct)
    {
        // Buzuq sana 500 bermasin — standart davr (oxirgi 30 kun).
        if (!DateOnly.TryParse(from, out var fromDay)) fromDay = AppClock.Today.AddDays(-29);
        if (!DateOnly.TryParse(to, out var toDay)) toDay = AppClock.Today;
        if (toDay < fromDay) (fromDay, toDay) = (toDay, fromDay);

        var fromIso = fromDay.ToString("yyyy-MM-dd") + "T00:00:00";
        var toIso = toDay.ToString("yyyy-MM-dd") + "T23:59:59";
        var take = Math.Clamp(limit ?? QualityDefaultItems, 1, QualityMaxItems);

        // ⚠️ NOMA'LUM kanal JIM tashlanadi (filtrsiz qoladi) — klientdagi xato kalit tufayli
        // ekran butunlay bo'shab qolmasin (jurnaldagi `type` bilan bir xil siyosat).
        var chFilter = channel is IgConst.ChannelComment or IgConst.ChannelDm
            or IgConst.ChannelPrivateReply ? channel : null;

        var q = db.IgMessages.AsNoTracking()
            .Where(m => m.AiSuggestedText != ""
                        && string.Compare(m.CreatedAt, fromIso) >= 0
                        && string.Compare(m.CreatedAt, toIso) <= 0);
        if (chFilter != null) q = q.Where(m => m.Channel == chFilter);

        var rows = await q
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id, m.Channel, m.Text, m.AiSuggestedText, m.AiSuggestedIntent,
                m.WasEdited, m.ActorName, m.CreatedAt,
            })
            .Take(QualityScanLimit)
            .ToListAsync(ct);

        // O'xshashlik SAQLANMAYDI (ustun yo'q) — ikkala matn joyida turgani uchun o'qishda
        // hisoblanadi. Yagona manba `IgQualityLog.Similarity`, ya'ni jadval, ro'yxat va
        // kelajakdagi eksport bir xil raqamni ko'rsatadi.
        var scored = rows
            .Select(r => new
            {
                r.Id, r.Channel, r.Text, r.AiSuggestedText, r.WasEdited, r.ActorName, r.CreatedAt,
                Intent = string.IsNullOrWhiteSpace(r.AiSuggestedIntent) ? IgConst.DefaultIntent : r.AiSuggestedIntent,
                Percent = IgQualityLog.SimilarityPercent(r.AiSuggestedText, r.Text),
            })
            .ToList();

        // ─────────── FILTRLAR: nima nimaga ta'sir qiladi
        // Davr va KANAL — BUTUN to'plamga (jamlanma ham, kesim ham, lenta ham).
        // NIYAT — jamlanma va lentaga, lekin KESIMGA EMAS: kesim ayni paytda TANLAGICH
        //   (chipdan niyat tanlanadi), o'zini o'zi bitta qatorga qisqartirsa tanlash yo'qolardi.
        // `onlyEdited` — FAQAT lentaga: u ro'yxatni ko'rish rejimi, hisobot emas. Aks holda
        //   "tahrir ulushi" doim 100% bo'lib, KPI ma'nosini yo'qotardi.
        var intentFilter = string.IsNullOrWhiteSpace(intent) ? null : intent.Trim().ToLowerInvariant();
        var inScope = intentFilter == null
            ? scored
            : scored.Where(x => x.Intent == intentFilter).ToList();

        var edited = inScope.Where(x => x.WasEdited).ToList();

        // ⚠️ O'rtacha farq FAQAT tahrirlanganlar bo'yicha: o'zgartirilmagan javoblar 100%
        // o'xshashlik bilan o'rtachani sun'iy ko'tarib, "AI matni deyarli aynan qoldirilgan"
        // degan yolg'on taassurot berardi.
        var avgSimilarity = edited.Count > 0 ? (int)Math.Round(edited.Average(x => x.Percent)) : 0;

        // ⚠️ ATAYIN `scored` (niyat filtridan OLDINGI to'plam) — yuqoridagi izohga qarang.
        var byIntent = scored
            .GroupBy(x => x.Intent)
            .Select(g => new IgQualityIntentDto(
                Intent: g.Key,
                Total: g.Count(),
                Edited: g.Count(x => x.WasEdited),
                AvgSimilarity: g.Any(x => x.WasEdited)
                    ? (int)Math.Round(g.Where(x => x.WasEdited).Average(x => x.Percent))
                    : 0))
            // Tartib: eng ko'p TAHRIRLANADIGAN niyat tepada — savol "AI qayerda ko'proq
            // yanglishadi", "qaysi niyat ko'p uchraydi" emas (bunisi analitikada bor).
            .OrderByDescending(x => x.Edited).ThenByDescending(x => x.Total)
            .ToList();

        var feed = (onlyEdited == true ? inScope.Where(x => x.WasEdited) : inScope).ToList();

        var items = feed
            .Take(take)
            .Select(x => new IgQualityPairDto(
                x.Id, x.Channel, x.Intent, x.AiSuggestedText, x.Text,
                x.Percent, x.WasEdited, x.ActorName, x.CreatedAt))
            .ToList();

        return new IgQualityDto(
            From: fromDay.ToString("yyyy-MM-dd"),
            To: toDay.ToString("yyyy-MM-dd"),
            Total: inScope.Count,
            Edited: edited.Count,
            Kept: inScope.Count - edited.Count,
            EditedPercent: inScope.Count > 0 ? (int)Math.Round(edited.Count * 100.0 / inScope.Count) : 0,
            AvgSimilarity: avgSimilarity,
            ByIntent: byIntent,
            Items: items,
            ItemsTotal: feed.Count,
            Truncated: rows.Count >= QualityScanLimit);
    }
}

// =================================================================================================
//  JAVOB SIFATI DTO'LARI — ⚠️ hech qaysisida mijoz nomi, ID'si, telefoni yoki KIRUVCHI matn YO'Q.
// =================================================================================================

/// <summary>Bitta juftlik: AI nima taklif qilgan va operator nima yuborgan.</summary>
/// <param name="Similarity">0..100 — 100 = matnlar aynan bir xil.</param>
/// <param name="ActorName">Javobni yozgan XODIM ismi (mijoz emas) — "kim qaysi uslubda
/// tuzatadi" savoliga javob beradi.</param>
public record IgQualityPairDto(
    string Id, string Channel, string Intent,
    string AiText, string SentText,
    int Similarity, bool WasEdited, string ActorName, string CreatedAt);

/// <summary>Niyat kesimi. <paramref name="AvgSimilarity"/> FAQAT tahrirlanganlar bo'yicha.</summary>
public record IgQualityIntentDto(string Intent, int Total, int Edited, int AvgSimilarity);

/// <summary>
/// Hisobot. <paramref name="Kept"/> — taklif AYNAN yuborilgan holatlar soni.
/// <paramref name="Truncated"/> true bo'lsa davr ichida qatorlar chegaradan oshgan
/// (ro'yxat ham, jamlanma ham eng YANGI qatorlardan olingan) — buni ekranda ochiq yozish shart.
///
/// <para><paramref name="ItemsTotal"/> — lenta filtrlariga (niyat + "faqat tahrirlanganlar")
/// MOS KELGAN barcha juftliklar soni; <paramref name="Items"/> esa ulardan <c>limit</c> tasi.
/// Ekran "N tadan M tasi" deb yozadi — jim qirqish bo'lmasin.</para>
/// </summary>
public record IgQualityDto(
    string From, string To,
    int Total, int Edited, int Kept, int EditedPercent, int AvgSimilarity,
    List<IgQualityIntentDto> ByIntent, List<IgQualityPairDto> Items, int ItemsTotal, bool Truncated);
