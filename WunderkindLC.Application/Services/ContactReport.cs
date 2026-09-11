using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "BOG'LANISH KERAK" HISOBOTINING YAGONA HISOB-KITOBI — davr bo'yicha barcha sonlar shu yerda
/// yig'iladi.
///
/// <para><b>NEGA ALOHIDA:</b> bir xil raqamlar UCH joyda kerak — hisobot sahifasi
/// (<c>GET /contacts/stats</c>), kunlik jurnal va AI tahlili
/// (<see cref="ContactAiAnalysisService"/>). Ular ayri-ayri hisoblansa "AI boshqa raqam
/// ko'rsatyapti" holati kelib chiqardi (<c>.claude/rules/ai-analysis.md</c> dagi qoida).</para>
///
/// <para>Sanoqlar HODISALARDAN (<see cref="ContactAttempt"/>) — ya'ni "nima bo'ldi" emas,
/// "kim nima qildi" bo'yicha. "Bog'lanildi" esa faqat odam bilan HAQIQATAN gaplashilgan
/// urinishlar (<see cref="ContactService.Reached"/>) — ko'tarmagan qo'ng'iroq urinishga kiradi.</para>
/// </summary>
public static class ContactReport
{
    /// <summary>Kunlik jurnalda bir so'rovda qaytadigan eng ko'p HODISA.</summary>
    public const int MaxJournalItems = 2000;

    /// <summary>Kunlik jurnalning standart chegarasi.</summary>
    public const int DefaultJournalItems = 500;

    /// <summary>AI promptiga ketadigan javob namunalarining standart soni.</summary>
    public const int DefaultSampleCount = 120;

    /// <summary>Bitta namuna matnining eng ko'p uzunligi (prompt shishmasin).</summary>
    public const int SampleTextLength = 300;

    private static bool IsContact(ContactAttempt a) => a.Type == ContactAttemptTypes.Contact;
    private static bool Reached(ContactAttempt a) => IsContact(a) && ContactService.Reached(a.Result);

    /// <summary>
    /// Davr hisoboti. <paramref name="sampleCount"/> &gt; 0 bo'lsa javob MATNLARIDAN namunalar
    /// ham qo'shiladi (faqat AI tahlili uchun kerak — hisobot sahifasi ularni so'ramaydi).
    /// </summary>
    /// <param name="today">Bugungi kun "yyyy-MM-dd" — <c>OpenNow</c>/<c>OverdueNow</c> davrga
    /// bog'liq EMAS, joriy holatni bildiradi (navbat sanoqlari bilan bir xil bo'lsin).</param>
    public static async Task<ContactAiMetricsDto> BuildAsync(
        IAppDbContext db, string fromDate, string toDate, string today,
        int sampleCount = 0, CancellationToken ct = default)
    {
        var attempts = await db.ContactAttempts.AsNoTracking()
            .Where(a => string.Compare(a.Date, fromDate) >= 0 && string.Compare(a.Date, toDate) <= 0)
            .ToListAsync(ct);

        var daily = new List<ContactDailyRowDto>();
        if (DateOnly.TryParse(fromDate, out var f) && DateOnly.TryParse(toDate, out var t))
        {
            for (var d = f; d <= t; d = d.AddDays(1))
            {
                var key = d.ToString("yyyy-MM-dd");
                var day = attempts.Where(a => a.Date == key).ToList();
                daily.Add(new ContactDailyRowDto(
                    key,
                    Created: day.Count(a => a.Type == ContactAttemptTypes.Created),
                    Attempts: day.Count(IsContact),
                    Reached: day.Count(Reached),
                    Done: day.Count(a => a.NextStatus == ContactStatuses.Done),
                    Callback: day.Count(a => a.NextStatus == ContactStatuses.Callback && IsContact(a)),
                    Failed: day.Count(a => a.NextStatus == ContactStatuses.Failed)));
            }
        }

        var byStaff = attempts
            .Where(a => a.ActorName.Length > 0)
            .GroupBy(a => a.ActorName)
            .Select(g => new ContactStaffRowDto(
                g.Key,
                Attempts: g.Count(IsContact),
                Reached: g.Count(Reached),
                Done: g.Count(a => a.NextStatus == ContactStatuses.Done),
                Callback: g.Count(a => a.NextStatus == ContactStatuses.Callback && IsContact(a)),
                Failed: g.Count(a => a.NextStatus == ContactStatuses.Failed)))
            .Where(r => r.Attempts > 0 || r.Done > 0 || r.Failed > 0)
            .OrderByDescending(r => r.Attempts).ThenBy(r => r.ActorName)
            .ToList();

        var byResult = ContactService.Results
            .Select(r => new ContactResultRowDto(r.Key, r.Label,
                attempts.Count(a => IsContact(a) && a.Result == r.Key)))
            .Where(r => r.Count > 0)
            .ToList();

        // SABABLAR — talab OCHILGAN sana bo'yicha (urinish emas): "qaysi sabab bilan kelgan".
        var requests = await db.ContactRequests.AsNoTracking()
            .Where(c => c.CreatedAt.Length >= 10
                        && string.Compare(c.CreatedAt.Substring(0, 10), fromDate) >= 0
                        && string.Compare(c.CreatedAt.Substring(0, 10), toDate) <= 0)
            .ToListAsync(ct);

        var byReason = requests
            .GroupBy(c => c.ReasonLabel.Length > 0 ? c.ReasonLabel : "— sababsiz —")
            .Select(g => new ContactReasonRowDto(
                g.Key,
                Created: g.Count(),
                Done: g.Count(c => c.Status == ContactStatuses.Done),
                Failed: g.Count(c => c.Status == ContactStatuses.Failed),
                Open: g.Count(c => ContactService.IsOpen(c.Status))))
            .OrderByDescending(r => r.Created).ThenBy(r => r.ReasonLabel)
            .ToList();

        var openNow = await db.ContactRequests.CountAsync(
            c => c.Status == ContactStatuses.New || c.Status == ContactStatuses.Callback, ct);
        var overdueNow = await db.ContactRequests.CountAsync(
            c => c.Status == ContactStatuses.Callback && c.DueDate != ""
                 && string.Compare(c.DueDate, today) < 0, ct);

        // NAMUNALAR — faqat AI uchun. Sabab talabdan olinadi (hodisada saqlanmaydi).
        var samples = new List<ContactAiSampleDto>();
        if (sampleCount > 0)
        {
            var reasonById = requests.ToDictionary(r => r.Id, r => r.ReasonLabel);
            var missing = attempts
                .Where(a => IsContact(a) && a.Response.Length > 0 && !reasonById.ContainsKey(a.RequestId))
                .Select(a => a.RequestId).Distinct().ToList();
            if (missing.Count > 0)
            {
                // Davrdan OLDIN ochilgan talablar sabablari (urinish bugun, talab o'tgan oyda).
                var older = await db.ContactRequests.AsNoTracking()
                    .Where(c => missing.Contains(c.Id))
                    .Select(c => new { c.Id, c.ReasonLabel })
                    .ToListAsync(ct);
                foreach (var o in older) reasonById[o.Id] = o.ReasonLabel;
            }

            samples = attempts
                .Where(a => IsContact(a) && a.Response.Length > 0)
                .OrderByDescending(a => a.CreatedAt)
                .Take(sampleCount)
                .Select(a => new ContactAiSampleDto(
                    a.Date,
                    reasonById.GetValueOrDefault(a.RequestId, "") is { Length: > 0 } r ? r : "— sababsiz —",
                    ContactService.ResultLabel(a.Result),
                    ContactService.StatusLabel(a.NextStatus),
                    a.Response.Length > SampleTextLength ? a.Response[..SampleTextLength] : a.Response,
                    a.ActorName))
                .ToList();
        }

        return new ContactAiMetricsDto(
            fromDate, toDate,
            Created: attempts.Count(a => a.Type == ContactAttemptTypes.Created),
            Attempts: attempts.Count(IsContact),
            Reached: attempts.Count(Reached),
            Done: attempts.Count(a => a.NextStatus == ContactStatuses.Done),
            Callback: attempts.Count(a => a.NextStatus == ContactStatuses.Callback && IsContact(a)),
            Failed: attempts.Count(a => a.NextStatus == ContactStatuses.Failed),
            OpenNow: openNow, OverdueNow: overdueNow,
            WithResponse: attempts.Count(a => IsContact(a) && a.Response.Length > 0),
            Daily: daily, ByStaff: byStaff, ByReason: byReason, ByResult: byResult,
            TopWords: ContactService
                .TopWords(attempts.Where(IsContact).Select(a => a.Response), 25)
                .Select(w => new ContactWordDto(w.Word, w.Count)).ToList(),
            Samples: samples);
    }

    /// <summary>
    /// KUNLIK JURNAL — "kimga qo'ng'iroq qilindi, qachon, nima dedi, qaysi sabab bilan",
    /// HAR KUN ALOHIDA.
    ///
    /// <para>Yuqoridagi jadvallar "nechta" ga javob beradi, jurnal esa kunning O'ZINI ko'rsatadi:
    /// rahbar bir kunni ochib, o'sha kuni nima bo'lganini boshdan-oxir o'qiy oladi.</para>
    ///
    /// <para>Kunlar YANGISIDAN eskisiga; kun ICHIDA esa ertalabdan kechgacha (o'sish tartibida) —
    /// jurnal xronologik o'qilsin.</para>
    /// </summary>
    /// <param name="types">Faqat shu turdagi hodisalar (bo'sh — hammasi):
    /// created | contact | note | reopen.</param>
    public static async Task<List<ContactJournalDayDto>> JournalAsync(
        IAppDbContext db, string fromDate, string toDate,
        IReadOnlyCollection<string>? types = null, int limit = DefaultJournalItems,
        CancellationToken ct = default)
    {
        var query = db.ContactAttempts.AsNoTracking()
            .Where(a => string.Compare(a.Date, fromDate) >= 0 && string.Compare(a.Date, toDate) <= 0);

        if (types is { Count: > 0 })
        {
            var list = types.ToList();
            query = query.Where(a => list.Contains(a.Type));
        }

        // Chegara ENG YANGI hodisalardan olinadi — kun tanlanganda uning hammasi kiradi,
        // uzun davrda esa eng so'nggilari (UI qirqilganini ochiq yozadi).
        var rows = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(Math.Clamp(limit, 1, MaxJournalItems))
            .ToListAsync(ct);
        if (rows.Count == 0) return new List<ContactJournalDayDto>();

        var requestIds = rows.Select(r => r.RequestId).Distinct().ToList();
        var requests = await db.ContactRequests.AsNoTracking()
            .Where(c => requestIds.Contains(c.Id))
            .Select(c => new { c.Id, c.StudentName, c.ReasonLabel })
            .ToListAsync(ct);
        var reqById = requests.ToDictionary(c => c.Id);

        // Telefon raqamlari — operator jurnaldan darhol qayta qo'ng'iroq qila olsin.
        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var phones = (await db.Students.AsNoTracking()
                .Where(s => studentIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone })
                .ToListAsync(ct))
            .ToDictionary(
                r => r.Id,
                r => new[] { r.Phone, r.ParentPhone, r.FatherPhone, r.MotherPhone }
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p!.Trim()).Distinct().ToList());

        return rows
            .GroupBy(a => a.Date)
            .OrderByDescending(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ContactJournalDayDto(
                g.Key,
                Created: g.Count(a => a.Type == ContactAttemptTypes.Created),
                Attempts: g.Count(IsContact),
                Reached: g.Count(Reached),
                Done: g.Count(a => a.NextStatus == ContactStatuses.Done),
                Callback: g.Count(a => a.NextStatus == ContactStatuses.Callback && IsContact(a)),
                Failed: g.Count(a => a.NextStatus == ContactStatuses.Failed),
                g.OrderBy(a => a.CreatedAt, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
                    .Select(a =>
                    {
                        var req = reqById.GetValueOrDefault(a.RequestId);
                        return new ContactJournalItemDto(
                            a.Id, a.RequestId, a.StudentId, req?.StudentName ?? "",
                            req?.ReasonLabel ?? "", a.Type, ContactService.TypeLabel(a.Type),
                            a.Result, ContactService.ResultLabel(a.Result),
                            a.NextStatus, ContactService.StatusLabel(a.NextStatus), a.DueDate,
                            a.Response, a.ActorName,
                            // "HH:mm" — ISO ning 11..16 oralig'i; format buzuq bo'lsa bo'sh qoladi.
                            a.CreatedAt.Length >= 16 ? a.CreatedAt[11..16] : "",
                            a.CreatedAt,
                            phones.GetValueOrDefault(a.StudentId) ?? new List<string>());
                    }).ToList()))
            .ToList();
    }
}
