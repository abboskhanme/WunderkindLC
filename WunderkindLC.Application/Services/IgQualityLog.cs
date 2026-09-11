using System.Text;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// E6.6 — <b>JAVOB SIFATI JURNALI</b>: "AI shunday dedi → operator shunday yozdi".
///
/// <para><b>Nima uchun:</b> operator AI javobini tuzatib yuborsa, bu promptni va bilim bazasini
/// yaxshilash uchun eng qimmatli ma'lumot. Ilgari u HECH QAYERDA qolmasdi: AI javobi ham,
/// operator javobi ham lentadagi ikki mustaqil qator edi va ular orasidagi bog'liqlik faqat
/// operatorning boshida turardi.</para>
///
/// <para><b>Taklif nima hisoblanadi:</b> suhbatdagi ENG OXIRGI chiquvchi xabar AI yozgan bo'lsa
/// (<c>IsAi</c>) va u yaqinda (<see cref="SuggestionWindowMinutes"/>) yozilgan bo'lsa — operator
/// keyin yozgan javob o'sha taklifning O'RNIGA ketgan hisoblanadi.</para>
///
/// <para>⚠️ Oxirgi chiquvchi xabar OPERATORNIKI bo'lsa taklif YO'Q: bu shunchaki suhbatning
/// davomi, tahrir emas. Aks holda operatorning ketma-ket ikki xabari "AI javobini tahrirladi"
/// bo'lib sanalib, hisobot yolg'on chiqardi.</para>
///
/// <para>⚠️ AI javobi YUBORILMAGAN bo'lsa ham (<c>Error</c> to'la) u baribir taklif hisoblanadi —
/// aynan shu holat eng foydalisi: AI matn yozdi, mijozga ketmadi, odam qaytadan yozdi.</para>
///
/// <para>🔴 <b>MAXFIYLIK:</b> bu yerda faqat BIZNING chiquvchi matnlarimiz solishtiriladi.
/// Mijozning ismi, telefoni yoki kiruvchi xabari saqlanmaydi va hisobotga chiqmaydi.</para>
/// </summary>
public static class IgQualityLog
{
    /* ═════════════════════════ Sozlamalar ═════════════════════════ */

    /// <summary>Taklif shuncha DAQIQAdan eski bo'lsa "tahrirlangan" deb sanalmaydi.
    /// <para>Sabab: mijoz ertasi kuni qayta yozganda operator bir kun oldingi bot javobini
    /// tahrirlagan bo'lmaydi — bu yangi suhbat qadami. 3 soat "o'sha o'tirishda" degani.</para></summary>
    public const int SuggestionWindowMinutes = 180;

    /// <summary>Solishtirishga kiradigan matn uzunligi. <c>IgConst.MaxReplyLength</c> (900) dan
    /// katta — Levenshtein O(n·m) bo'lgani uchun chegara SHART, lekin amaldagi javoblar
    /// bundan qisqa, ya'ni kesish deyarli hech qachon ishlamaydi.</summary>
    public const int CompareLimit = 2000;

    /* ═════════════════════════ Matnni solishtirish (sof) ═════════════════════════ */

    /// <summary>
    /// Solishtirish uchun normallashtirish: kichik harf, apostroflar bir ko'rinishga, ketma-ket
    /// bo'shliqlar bitta bo'shliqqa.
    ///
    /// <para>⚠️ Apostrof birxillashtirish — loyihadagi <c>ContactService.TopWords</c> bilan
    /// AYNAN bir sabab: matn turli klaviaturalardan kiritiladi va "to'lov" bilan "toʻlov"
    /// aks holda ikki xil matn bo'lib chiqardi (operator faqat apostrofni almashtirsa ham
    /// "tahrirladi" deb sanalardi).</para>
    /// </summary>
    public static string Normalize(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return "";

        var sb = new StringBuilder(t.Length);
        var space = false;
        foreach (var raw in t)
        {
            var ch = raw switch
            {
                'ʻ' or 'ʼ' or '’' or '‘' or '`' or '´' => '\'',
                _ => char.ToLowerInvariant(raw),
            };

            if (char.IsWhiteSpace(ch))
            {
                space = true;
                continue;
            }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Levenshtein masofasi (qo'shish/o'chirish/almashtirish). Ikki qatorli DP — butun matritsa
    /// xotirada saqlanmaydi.
    /// </summary>
    public static int EditDistance(string? a, string? b)
    {
        var s = a ?? "";
        var t = b ?? "";
        if (s.Length == 0) return t.Length;
        if (t.Length == 0) return s.Length;

        var prev = new int[t.Length + 1];
        var cur = new int[t.Length + 1];
        for (var j = 0; j <= t.Length; j++) prev[j] = j;

        for (var i = 1; i <= s.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= t.Length; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[t.Length];
    }

    /// <summary>
    /// Ikki matn o'xshashligi — <b>0..1</b> (1 = ayni bir xil). Normallashtirilgan Levenshtein:
    /// <c>1 − masofa / uzunroq matn</c>.
    ///
    /// <para>Ikkalasi ham bo'sh bo'lsa <c>1</c>, faqat bittasi bo'sh bo'lsa <c>0</c>.</para>
    /// </summary>
    public static double Similarity(string? a, string? b)
    {
        var s = InstagramContract.Trim(Normalize(a), CompareLimit);
        var t = InstagramContract.Trim(Normalize(b), CompareLimit);
        if (s.Length == 0 && t.Length == 0) return 1;
        if (s.Length == 0 || t.Length == 0) return 0;
        if (string.Equals(s, t, StringComparison.Ordinal)) return 1;

        var max = Math.Max(s.Length, t.Length);
        return Math.Clamp(1.0 - (double)EditDistance(s, t) / max, 0, 1);
    }

    /// <summary>Hisobot uchun foizga aylantirilgan o'xshashlik (0..100).</summary>
    public static int SimilarityPercent(string? a, string? b) =>
        (int)Math.Round(Similarity(a, b) * 100, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Operator taklifni o'zgartirdimi. Solishtirish NORMALLASHTIRILGAN matnlar ustida:
    /// faqat bosh harf yoki ortiqcha bo'shliq farq qilsa bu tahrir emas.
    /// </summary>
    public static bool IsEdited(string? suggested, string? sent) =>
        !string.Equals(Normalize(suggested), Normalize(sent), StringComparison.Ordinal);

    /* ═════════════════════════ Taklifni tanlash (sof) ═════════════════════════ */

    /// <summary>
    /// Berilgan xabar "javobsiz qolgan AI taklifi" bo'la oladimi.
    ///
    /// <para>Shartlar: chiquvchi · AI yozgan · matni bor · <paramref name="windowMinutes"/>
    /// ichida. Vaqtni o'qib bo'lmasa (buzuq ISO) — <b>false</b>: noaniq qiymat asosida
    /// hisobotga qator yozishdan ko'ra yozmagan yaxshi.</para>
    /// </summary>
    public static bool IsSuggestionCandidate(IgMessage? m, DateTime now, int windowMinutes = SuggestionWindowMinutes)
    {
        if (m is null) return false;
        if (m.Direction != IgConst.DirOut) return false;
        if (!m.IsAi) return false;
        if (string.IsNullOrWhiteSpace(m.Text)) return false;
        if (!InstagramContract.TryIso(m.CreatedAt, out var at)) return false;

        var age = (now - at).TotalMinutes;
        return age >= -1 && age <= windowMinutes;   // −1: soat sakrashi/yaxlitlash zaxirasi
    }

    /* ═════════════════════════ Yozish (DB) ═════════════════════════ */

    /// <summary>
    /// Operator yozgan chiquvchi xabarga AI taklifini biriktiradi (topilsa).
    ///
    /// <para><b>SAQLAMAYDI</b> — chaqiruvchining <c>SaveChangesAsync</c>i bilan birga ketadi
    /// (loyihadagi <c>AuditService.Record</c> bilan bir xil siyosat).</para>
    ///
    /// <para>⚠️ <paramref name="message"/> hali <c>Add</c> qilinmagan bo'lishi kerak (yoki
    /// saqlanmagan bo'lsa ham bo'ladi) — so'rov bazaga ketadi va yangi qatorni ko'rmaydi.</para>
    ///
    /// <para>⚠️ Xato JIM YUTILADI: sifat jurnali — ichki tahlil ma'lumoti, uning tufayli
    /// operatorning javobi yuborilmay qolishi mumkin emas.</para>
    /// </summary>
    public static async Task AttachSuggestionAsync(
        IAppDbContext db, string conversationId, IgMessage message, DateTime now, CancellationToken ct = default)
    {
        if (message is null || string.IsNullOrWhiteSpace(conversationId)) return;

        try
        {
            var last = await db.IgMessages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId && m.Direction == IgConst.DirOut)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (!IsSuggestionCandidate(last, now)) return;

            message.AiSuggestedText = InstagramContract.Trim(last!.Text, IgConst.MaxReplyLength);
            message.AiSuggestedIntent = last.AiIntent;
            message.WasEdited = IsEdited(last.Text, message.Text);
        }
        catch (Exception)
        {
            // Sifat jurnali yozilmadi — javob baribir ketadi.
        }
    }
}
