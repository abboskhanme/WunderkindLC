using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// REKLAMA FORMASI to'ldirilgani haqidagi webhook hodisasi (normalizatsiyalangan ichki model).
///
/// <para>⚠️ Payloadda mijozning O'ZI YO'Q — faqat <see cref="LeadgenId"/> keladi. Ism va telefon
/// alohida so'rov bilan olinadi (<c>MetaAdsApi.FetchLeadAsync</c>), chunki Meta shaxsiy
/// ma'lumotni webhook bodysida yubormaydi. Ya'ni Page tokeni bo'lmasa lid MAZMUNSIZ qoladi.</para>
/// </summary>
/// <param name="LeadgenId">Meta bergan lid id — hamma narsa shundan olinadi.</param>
/// <param name="PageId">Qaysi Facebook Page — token aynan shu sahifaniki bo'lishi kerak.</param>
/// <param name="FormId">Instant Form id (analitika: qaysi forma qancha lid berdi).</param>
/// <param name="AdId">Reklama e'loni id. Bo'sh bo'lishi mumkin (organik forma).</param>
/// <param name="AdgroupId">Reklama guruhi (adset) id.</param>
/// <param name="CreatedTimeIso">Meta bergan yaratilish vaqti (ISO, mahalliy). Bo'sh = yo'q.</param>
/// <param name="EventKey">Deterministik dedup kaliti — <c>leadgen:{leadgen_id}</c>.</param>
public record IgLeadgenEvent(
    string LeadgenId,
    string PageId,
    string FormId,
    string AdId,
    string AdgroupId,
    string CreatedTimeIso,
    string EventKey);

/// <summary>
/// META REKLAMA LIDI (Lead Ads) webhook payloadini ichki hodisalarga aylantiradi.
///
/// <para><b>Nega alohida parser?</b> <see cref="InstagramEventParser"/> <c>instagram</c> obyektining
/// izoh (<c>changes[].field == "comments"</c>) va DM (<c>messaging[]</c>) hodisalarini o'qiydi.
/// Reklama lidi esa BOSHQA obyektdan keladi — <c>page</c>, maydon <c>leadgen</c> — va tuzilishi
/// ham boshqacha. Ikkisini bitta funksiyaga tiqish har ikkalasini ham o'qib bo'lmas qilardi,
/// shuning uchun ayri (ikkalasi ham sof, ikkalasi ham to'liq testlangan).</para>
///
/// <para><b>Kalit DETERMINISTIK</b> — <c>leadgen:{id}</c>. Meta yetkazishni "at-least-once"
/// kafolatlaydi va muvaffaqiyatsiz yetkazishni 36 soat qayta yuboradi; kalit har safar bir xil
/// chiqmasa bitta mijoz uchun bir necha lid ochilardi (<c>marketing-instagram.md</c> §5).</para>
///
/// <para><b>Buzuq JSON → BO'SH ro'yxat</b> (istisno otilmaydi): bitta noto'g'ri payload butun
/// navbatni to'xtatib qo'ymasin.</para>
/// </summary>
public static class MetaLeadgenParser
{
    /// <summary>Xom webhook JSON'i → 0..N reklama lid hodisasi.</summary>
    public static IReadOnlyList<IgLeadgenEvent> Parse(string rawJson)
    {
        var result = new List<IgLeadgenEvent>();
        if (string.IsNullOrWhiteSpace(rawJson)) return result;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(rawJson); }
        catch (JsonException) { return result; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;

            // ⚠️ `object` maydoni tekshirilmaydi (faqat `entry`/`field` bo'yicha ishlaymiz):
            // bitta callback URL'ga ikkala obyekt ham kelishi mumkin va Meta kelajakda obyekt
            // nomini o'zgartirsa hodisa JIMGINA yo'qolardi. `field == "leadgen"` sharti o'zi
            // yetarli darajada aniq.
            if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                    continue;

                var entryId = Str(entry, "id");

                foreach (var ch in changes.EnumerateArray())
                {
                    if (ch.ValueKind != JsonValueKind.Object) continue;
                    if (!string.Equals(Str(ch, "field"), IgConst.FieldLeadgen, StringComparison.Ordinal)) continue;
                    if (!ch.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Object) continue;

                    var leadgenId = Raw(v, "leadgen_id");
                    // Lid id'siz hodisadan foyda yo'q: ism ham, telefon ham AYNAN shu id bilan
                    // olinadi. Jimgina tashlanadi (navbatda `skipped` bo'lib ko'rinadi).
                    if (leadgenId.Length == 0) continue;

                    var pageId = Raw(v, "page_id");
                    if (pageId.Length == 0) pageId = entryId;

                    result.Add(new IgLeadgenEvent(
                        LeadgenId: leadgenId,
                        PageId: pageId,
                        FormId: Raw(v, "form_id"),
                        AdId: Raw(v, "ad_id"),
                        AdgroupId: Raw(v, "adgroup_id"),
                        CreatedTimeIso: InstagramEventParser.ToIso(Raw(v, "created_time")),
                        EventKey: EventKey(leadgenId)));
                }
            }
        }

        return result;
    }

    /// <summary>Dedup kaliti — <c>leadgen:{id}</c>. Sof funksiya (webhook controlleri ham,
    /// pipeline ham AYNAN shuni ishlatadi).</summary>
    public static string EventKey(string leadgenId) => "leadgen:" + (leadgenId ?? "").Trim();

    /* ---------------- yordamchilar ---------------- */

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    /// <summary>⚠️ Meta id'larni <b>ba'zan satr, ba'zan raqam</b> qilib yuboradi
    /// (<c>"leadgen_id": "123"</c> va <c>"leadgen_id": 123</c> ikkalasi ham uchraydi) —
    /// faqat satr o'qilsa lidlar jimgina tushib qolardi.</summary>
    private static string Raw(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number or JsonValueKind.String
            ? v.ToString()
            : "";
}
