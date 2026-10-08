using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Instagram SUHBATI ↔ CRM LIDI bog'lanishining TARIXI (<see cref="IgConversation.LeadId"/> +
/// <see cref="IgConversation.PastLeadIds"/>). Sof qism testlangan (<c>AuditFixFx3Tests</c>).
///
/// <para><b>Muammo:</b> o'quvchiga AYLANTIRILGAN lid qayta ishlatilmaydi (CRM-1) — o'sha odam
/// Instagram'da yana yozsa YANGI lid ochiladi va suhbat unga ko'chadi. Ilgari ko'chirish
/// <c>conv.LeadId</c> ni shunchaki ustidan yozardi, ya'ni eski lidning izi qolmasdi:</para>
/// <list type="bullet">
///   <item>kanal tahlili (<see cref="LeadCrmOverview"/>, CRM statistikasi) PUL TO'LAGAN eski lidni
///     "Instagram" emas, "Boshqa" deb sanardi;</item>
///   <item>«Click-to-Direct» reklama atributsiyasi (<see cref="IgDmAdLeads"/> → ROI) eski liddan
///     YANGI, hali to'lamagan lidga ko'chib ketardi — reklama keltirgan tushum yo'qolardi.</item>
/// </list>
///
/// <para><b>Yechim:</b> ko'chirishda eski lid <c>"leadId|tugash"</c> qatori bilan tarixga yoziladi
/// (<see cref="RelinkAsync"/>). <c>tugash</c> — eski lid davri qachon tugagani: o'quvchiga
/// aylantirilgan payt (<c>convert</c> hodisasi), topilmasa ko'chirish payti. Shu sanagacha kelgan
/// reklama xabari ESKI lidniki, keyingisi yangisiniki (<see cref="OwnerAt"/>).</para>
///
/// <para>⚠️ Yagona yozish yo'li — <see cref="RelinkAsync"/> (<see cref="InstagramLeadBridge"/>
/// chaqiradi). <c>conv.LeadId</c> ni boshqa joyda QO'LDA almashtirmang — tarix jimgina uzilardi.
/// ISTISNO: lid o'chirilganda (<c>LeadRemoval</c>) bog'lanish TOZALANADI, tarixga yozilmaydi —
/// o'chgan lid hisobotda bo'lmasligi kerak (o'qiydigan joylar ham buni tekshiradi).</para>
/// </summary>
public static class IgLeadLinks
{
    /// <summary>Tarix chegarasi — cheksiz o'smasin (amalda bitta suhbatda 1–2 ta bo'ladi).
    /// Chegaraga yetganda YANGI qator qo'shilmaydi: eski (birinchi teginish) qatorlar qadrliroq.</summary>
    public const int MaxPastLeads = 20;

    private const char Sep = '|';
    private const string IsoFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>Tarix qatori: <c>"leadId|tugash"</c>.</summary>
    public static string Entry(string leadId, string untilIso) => leadId.Trim() + Sep + untilIso.Trim();

    /// <summary>Tarix qatorini o'qiydi. Buzuq qator (bo'sh id yoki vaqt) — <c>false</c>.</summary>
    public static bool TryParse(string? entry, out string leadId, out string untilIso)
    {
        leadId = "";
        untilIso = "";
        if (string.IsNullOrWhiteSpace(entry)) return false;
        var i = entry.IndexOf(Sep);
        if (i <= 0 || i >= entry.Length - 1) return false;
        leadId = entry[..i].Trim();
        untilIso = entry[(i + 1)..].Trim();
        return leadId.Length > 0 && untilIso.Length > 0;
    }

    /// <summary>Suhbat BIRORTA bo'lsa ham bog'langan barcha lidlar (joriy + tarix), takrorsiz.</summary>
    public static IEnumerable<string> AllLeadIds(string? currentLeadId, IEnumerable<string>? pastLeadIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(currentLeadId) && seen.Add(currentLeadId.Trim()))
            yield return currentLeadId.Trim();
        foreach (var p in pastLeadIds ?? [])
            if (TryParse(p, out var id, out _) && seen.Add(id))
                yield return id;
    }

    /// <summary>
    /// <paramref name="atIso"/> paytida kelgan xabar QAYSI lidniki. Tarix tugash vaqti bo'yicha
    /// tartiblanadi: xabar birinchi <c>tugash</c> dan OLDIN kelgan bo'lsa — o'sha lidniki, hammasidan
    /// keyin kelgan bo'lsa — joriy lidniki (u bo'sh bo'lsa <c>""</c>: egasi yo'q).
    /// <para>⚠️ Chegara QAT'IY (<c>&lt;</c>): aylantirish soniyasida kelgan xabar endi yangi
    /// murojaat.</para>
    /// <para>Tarix bo'sh bo'lsa natija har doim joriy lid — eski xatti-harakat AYNAN saqlanadi.</para>
    /// </summary>
    public static string OwnerAt(string? currentLeadId, IEnumerable<string>? pastLeadIds, string? atIso)
    {
        var at = atIso ?? "";
        var past = (pastLeadIds ?? [])
            .Select(p => TryParse(p, out var id, out var until) ? (Id: id, Until: until) : (Id: "", Until: ""))
            .Where(p => p.Id.Length > 0)
            .OrderBy(p => p.Until, StringComparer.Ordinal);
        foreach (var p in past)
            if (string.CompareOrdinal(at, p.Until) < 0) return p.Id;
        return (currentLeadId ?? "").Trim();
    }

    /// <summary>
    /// Tarixga qator qo'shadi (sof — bazaga bormaydi). Shu lid allaqachon tarixda bo'lsa yoki
    /// chegara to'lgan bo'lsa — o'zgarmaydi (idempotent).
    /// <para>⚠️ Ro'yxat YANGI nusxa bilan almashtiriladi (joyida o'zgartirilmaydi) — EF primitiv
    /// to'plamdagi o'zgarishni aniq sezsin.</para>
    /// </summary>
    public static void Remember(IgConversation conv, string leadId, string untilIso)
    {
        if (string.IsNullOrWhiteSpace(leadId) || string.IsNullOrWhiteSpace(untilIso)) return;
        var list = conv.PastLeadIds ?? [];
        if (list.Count >= MaxPastLeads) return;
        if (list.Any(p => TryParse(p, out var id, out _) && id == leadId.Trim())) return;
        conv.PastLeadIds = [.. list, Entry(leadId, untilIso)];
    }

    /// <summary>
    /// Suhbatni <paramref name="newLeadId"/> ga bog'laydi. Avval BOSHQA (bazada hali bor) lidga
    /// bog'langan bo'lsa — u tarixga yoziladi, tugash vaqti = o'sha lidning o'quvchiga aylantirilgan
    /// payti (<c>convert</c> hodisasi), topilmasa hozir. <c>SaveChanges</c> QILMAYDI.
    /// </summary>
    public static async Task RelinkAsync(
        IAppDbContext db, IgConversation conv, string newLeadId, CancellationToken ct = default)
    {
        var old = (conv.LeadId ?? "").Trim();
        if (old.Length > 0 && !string.Equals(old, newLeadId, StringComparison.Ordinal)
            && await db.Leads.AnyAsync(l => l.Id == old, ct))
        {
            var convertedAt = await db.LeadEvents.AsNoTracking()
                .Where(e => e.LeadId == old && e.Type == LeadAnalytics.TypeConvert)
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => e.CreatedAt)
                .FirstOrDefaultAsync(ct);
            Remember(conv, old, NormalizeIso(convertedAt) ?? AppClock.Iso());
        }
        conv.LeadId = newLeadId;
    }

    /// <summary>
    /// Suhbatning JONLI lidi bormi: bog'lanish bor, lid bazada bor va o'quvchiga AYLANTIRILMAGAN.
    /// <para>🔴 Pipeline va Inbox'dagi «Lidga aylantirish» "BIRINCHI bog'lanish" ni shu bilan hal
    /// qiladi. Ilgari faqat "lid bormi" so'ralardi: aylantirilgan lidga bog'langan suhbatda qaytgan
    /// o'quvchi uchun YANGI lid ochilsa ham, bayroq "birinchi emas" bo'lib qolar va yangi lid
    /// Telegram guruhiga UMUMAN tushmasdi (karta yo'q lidga <c>SyncCardAsync</c> hech narsa
    /// yubormaydi).</para>
    /// </summary>
    public static async Task<bool> HasLiveLeadAsync(IAppDbContext db, string? leadId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(leadId)) return false;
        var id = leadId.Trim();
        return await db.Leads.AnyAsync(l => l.Id == id && l.ConvertedStudentId == null, ct);
    }

    /// <summary>
    /// "Instagram (izoh/DM)" kanaliga tegishli BARCHA lid id'lari — joriy bog'lanish VA tarix.
    /// <para>⚠️ Kanal tahlilining ikkala sahifasi (<see cref="LeadCrmOverview"/> va CRM statistikasi)
    /// SHU funksiyani chaqiradi — ikki sahifa bir xil son ko'rsatsin. Bazada yo'q (o'chirilgan) id
    /// qaytishi mumkin: chaqiruvchilar uni o'z lidlar ro'yxati bilan kesishtiradi.</para>
    /// </summary>
    public static async Task<HashSet<string>> LinkedLeadIdsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var rows = await db.IgConversations.AsNoTracking()
            .Where(c => c.LeadId != null || c.PastLeadIds.Count > 0)
            .Select(c => new { c.LeadId, c.PastLeadIds })
            .ToListAsync(ct);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rows)
            foreach (var id in AllLeadIds(r.LeadId, r.PastLeadIds))
                ids.Add(id);
        return ids;
    }

    /// <summary>
    /// Aylantirilgan (oldingi) lid bo'yicha "Avval o'quvchi bo'lgan: &lt;ism&gt;" matni — telefon
    /// bo'lmagan holat uchun (<see cref="LeadIntake.FormerStudentNoteAsync"/> telefon bo'yicha
    /// qidiradi, Instagram'dagi qaytgan o'quvchi esa raqamini qayta yozmasligi mumkin).
    /// Oldingi lid aylantirilmagan yoki topilmasa <c>""</c>.
    /// </summary>
    public static async Task<string> FormerStudentNoteAsync(IAppDbContext db, string? previousLeadId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(previousLeadId)) return "";
        var id = previousLeadId.Trim();
        var prior = await db.Leads.AsNoTracking()
            .Where(l => l.Id == id && l.ConvertedStudentId != null)
            .Select(l => new { l.FullName, l.ConvertedStudentId })
            .FirstOrDefaultAsync(ct);
        if (prior is null) return "";
        var studentName = await db.Students.AsNoTracking()
            .Where(s => s.Id == prior.ConvertedStudentId)
            .Select(s => s.FullName)
            .FirstOrDefaultAsync(ct);
        var name = !string.IsNullOrWhiteSpace(studentName) ? studentName.Trim() : (prior.FullName ?? "").Trim();
        return $"Avval o'quvchi bo'lgan: {name}";
    }

    /// <summary>Hodisa vaqtini tarix formatiga (<c>yyyy-MM-ddTHH:mm:ss</c>) keltiradi; buzuq — <c>null</c>.</summary>
    private static string? NormalizeIso(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var head = value.Trim();
        if (head.Length > 19) head = head[..19];
        return DateTime.TryParseExact(head, IsoFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString(IsoFormat, CultureInfo.InvariantCulture)
            : null;
    }
}
