using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// DAVOMAT XABARLARI ISHCHISI — bazadagi navbatni (<see cref="AttendanceNotifyItem"/>) bittalab
/// yuboradi. Navbatga yozuvchi — <see cref="AttendanceNotify.EnqueueBulkAsync"/> / <see cref="AttendanceNotify.EnqueueSingleAsync"/>,
/// controller esa saqlagach <see cref="Wake"/> ni chaqiradi.
///
/// <para><b>CRM U3b (SPEC P3.3) — JADVAL va MENEJER NAZORATI:</b> qator faqat <c>pending</c> yoki <c>approved</c>
/// bo'lsa VA <see cref="AttendanceNotifyItem.SendAfter"/> (dars tugashi + kechikish) yetganda yuboriladi;
/// <c>awaiting</c>/<c>held</c> hech qachon o'zi ketmaydi va <c>SendAfter + 6 soat</c> da <c>expired</c> bo'ladi.
/// Har aylanishda ochiq qatorlar jurnal bilan solishtiriladi (<see cref="AttendanceNotify.ReconcileAsync"/>):
/// "kelmadi" «keldi»ga o'zgargan bo'lsa — <c>cancelled</c> (<c>changed</c>), arxivlangan — <c>cancelled</c>
/// (<c>archived</c>). Tasdiq kutayotgan darslar haqida menejerlarga BIR MARTA xabar (<see cref="AttendanceNotify.AlertAwaitingAsync"/>).</para>
///
/// <para><b>Restartga chidamli:</b> ishga tushganda (<see cref="RecoverAsync"/>) aynan yuborish paytida uzilgan
/// (<c>sending</c>) qator <c>unknown</c> bo'ladi va QAYTA yuborilmaydi (xabar ketgan bo'lishi mumkin); qolganlari
/// o'z vaqtida yuboriladi. Har oluvchi yuborishdan OLDIN band qilinadi (concurrency token) — bir xabar ikki marta
/// ketmaydi, menejerning «To'xtatish»i bilan poyga ham shu token bilan hal bo'ladi.</para>
///
/// <para>Uyg'otish yo'qolsa ham ishchi har <see cref="PollInterval"/> da navbatni o'zi tekshiradi (SendAfter
/// keladigan qatorlar uchun ham shu).</para>
///
/// <para>⚠️ Har oluvchi YANGI DI scope'da (o'z DbContext'i bilan) yuboriladi, band qilish/yakunlash esa
/// alohida kontekstda: dispatcher o'z saqlash xatosini yutadi va kontekstda "buzuq" qatorlar qolishi
/// mumkin — ular keyingi oluvchining band qilinishini yiqitmasin (JobRunMarker'dagi saboq).</para>
/// </summary>
public class AttendanceNotifyWorker(
    IServiceProvider services, AutoMessageService autoMsg, ILogger<AttendanceNotifyWorker> logger,
    TelegramService? telegram = null, IConfiguration? config = null) : BackgroundService
{
    /// <summary>SendAfter dan shuncha keyin ochiq qator yuborilmaydi (<c>expired</c>) — <see cref="AbsentNoticeRules.MaxAge"/>.</summary>
    public static readonly TimeSpan MaxAge = AbsentNoticeRules.MaxAge;

    /// <summary>Yakunlangan qatorlar shuncha kundan keyin tozalanadi (outbox jurnali o'z joyida qoladi).</summary>
    public const int KeepDays = 7;

    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 200;

    private readonly SemaphoreSlim _signal = new(0, 1);
    private DateOnly _prunedOn = DateOnly.MinValue;

    /// <summary>Navbatga yangi qator yozildi yoki menejer tasdiqladi — ishchini darhol uyg'otadi (kutmasdan).</summary>
    public void Wake()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* allaqachon uyg'otilgan */ }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await RecoverAsync(stoppingToken); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { logger.LogError(ex, "Davomat xabarlari navbati: tiklashda xatolik"); }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Davomat xabarlari navbatida xatolik — keyinroq davom etadi"); }

            try { await _signal.WaitAsync(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Ilova ishga tushganda: avvalgi jarayon yuborish paytida qoldirgan <c>sending</c> qatorlar —
    /// <c>unknown</c> (bu paytda hali hech bir ishchi ishlamayapti, ya'ni ularning HAMMASI uzilgan).
    /// Eski yakunlangan qatorlar tozalanadi.
    /// </summary>
    internal async Task RecoverAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var now = AppClock.Now;
        var stuck = await db.AttendanceNotifyItems.Where(i => i.Status == AbsentNoticeRules.Sending).ToListAsync(ct);
        foreach (var i in stuck)
        {
            i.Status = AbsentNoticeRules.Unknown;
            i.UpdatedAt = now;
        }
        if (stuck.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogWarning(
                "Davomat xabarlari navbati: {Count} ta xabar yuborish paytida uzilgan — qayta yuborilmaydi.", stuck.Count);
        }
        await PruneAsync(db, now, ct);
    }

    /// <summary>Hozirgi vaqt bo'yicha bitta aylanish (<see cref="ProcessDueAsync"/>).</summary>
    internal Task<int> ProcessPendingAsync(CancellationToken ct) => ProcessDueAsync(AppClock.Now, ct);

    /// <summary>
    /// Bitta aylanish <paramref name="now"/> bo'yicha (testlar vaqtni shu yerdan beradi): (1) ochiq qatorlarni
    /// jurnal bilan solishtirish va muddati o'tganlarini yopish, (2) tasdiq kutayotgan darslar haqida menejerlarga
    /// xabar, (3) vaqti kelgan <c>pending</c>/<c>approved</c> qatorlarni (eng erta SendAfter dan) yuborish.
    /// Qaytaradi: shu aylanishda ishlangan (yuborilgan/yopilgan) qatorlar soni.
    /// </summary>
    internal async Task<int> ProcessDueAsync(DateTime now, CancellationToken ct)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await AttendanceNotify.ReconcileAsync(db, now, ct: ct);
            try
            {
                var tg = telegram;
                Func<long, string, CancellationToken, Task>? send = tg is { IsConfigured: true }
                    ? (chatId, text, c) => tg.SendMessageAsync(chatId, text, ct: c)
                    : null;
                await AttendanceNotify.AlertAwaitingAsync(db, send,
                    AbsentNoticeRules.BaseUrl(config?["App:Host"] ?? config?["APP_HOST"]), now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Menejerga ogohlantirish yiqilsa ham xabarlar navbati to'xtamaydi (belgisi yo'q — keyingi aylanishda qayta).
                logger.LogWarning(ex, "Davomat xabarlari: menejerlarni ogohlantirishda xatolik");
            }
        }

        var processed = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!ct.IsCancellationRequested)
        {
            List<string> ids;
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                ids = (await db.AttendanceNotifyItems.AsNoTracking()
                        .Where(i => (i.Status == AbsentNoticeRules.Pending || i.Status == AbsentNoticeRules.Approved)
                                    && i.SendAfter <= now)
                        .OrderBy(i => i.SendAfter)
                        .Select(i => i.Id)
                        .Take(BatchSize)
                        .ToListAsync(ct))
                    .Where(seen.Add)
                    .ToList();
            }
            if (ids.Count == 0) break;
            foreach (var id in ids)
            {
                // To'xtatilsa — qolganlari bazada qoladi, keyingi ishga tushishda yuboriladi.
                if (ct.IsCancellationRequested) return processed;
                if (await ProcessOneAsync(id, now, ct)) processed++;
            }
        }

        if (_prunedOn != DateOnly.FromDateTime(now))
        {
            using var scope = services.CreateScope();
            await PruneAsync(scope.ServiceProvider.GetRequiredService<IAppDbContext>(), now, ct);
        }
        return processed;
    }

    /// <summary>
    /// Bitta qator: tekshirish → band qilish → yuborish → yakunlash. <c>true</c> — shu chaqiruv qatorni ishladi.
    /// Band qilish bazaga yozilmasa (vaqtinchalik xato) xato TASHQARIGA chiqadi: hech narsa yuborilmaydi,
    /// qator o'z holatida qoladi va keyingi aylanishda qayta uriniladi.
    /// </summary>
    private async Task<bool> ProcessOneAsync(string id, DateTime now, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var item = await db.AttendanceNotifyItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null || !AbsentNoticeRules.Sendable(item.Status, item.SendAfter, now)) return false;

        // Yuborishdan OLDIN — muddat, arxiv va jurnal (menejer/o'qituvchi shu orada o'zgartirgan bo'lishi mumkin).
        string? closeAs = null, cancelReason = null;
        if (AbsentNoticeRules.IsExpired(item.Status, item.SendAfter, now)) closeAs = AbsentNoticeRules.Expired;
        else if (await db.Students.AsNoTracking().AnyAsync(s => s.Id == item.StudentId && s.IsArchived, ct))
            (closeAs, cancelReason) = (AbsentNoticeRules.Cancelled, AbsentNoticeRules.CancelArchived);
        else
        {
            var entries = await db.JournalEntries.AsNoTracking()
                .Where(e => e.ClassId == item.ClassId && e.SubjectId == item.SubjectId && e.Date == item.Date
                            && e.Period == item.Period && e.StudentId == item.StudentId)
                .ToListAsync(ct);
            if (!AbsentNoticeRules.JournalStillValid(item.Kind, entries, await AttendanceNotify.LateReasonIdsAsync(db, ct)))
                (closeAs, cancelReason) = (AbsentNoticeRules.Cancelled, AbsentNoticeRules.CancelChanged);
        }
        if (closeAs is not null)
        {
            item.Status = closeAs;
            if (cancelReason is not null) item.CancelReason = cancelReason;
            item.UpdatedAt = now;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return false; }
            if (closeAs == AbsentNoticeRules.Expired)
                logger.LogWarning("Davomat xabari {Id} juda kech (SendAfter {SendAfter}) — yuborilmadi.", id, item.SendAfter);
            return true;
        }

        // BAND QILISH (concurrency token): boshqa ishchi allaqachon olgan yoki menejer to'xtatgan bo'lsa — o'tkazib yuboramiz.
        item.Status = AbsentNoticeRules.Sending;
        item.UpdatedAt = now;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return false; }

        var status = AbsentNoticeRules.Done;
        try
        {
            // Yuborish ALOHIDA kontekstda (yuqoridagi izoh). ATAYIN CancellationToken.None: boshlangan
            // yuborish jurnalga yozilmay qolmasin.
            using var sendScope = services.CreateScope();
            var sendDb = sendScope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await AttendanceNotify.DispatchItemAsync(sendDb, autoMsg, item, CancellationToken.None);
        }
        catch (Exception ex)
        {
            status = AbsentNoticeRules.Failed;
            logger.LogWarning(ex, "Davomat xabari ({Kind}) o'quvchi {Student} — xatolik", item.Kind, item.StudentId);
        }

        item.Status = status;
        item.UpdatedAt = AppClock.Now;
        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch (Exception ex)
        {
            // Qator `sending` bo'lib qoladi — keyingi ishga tushishda `unknown` (QAYTA yuborilmaydi).
            logger.LogWarning(ex, "Davomat xabari {Id} holati saqlanmadi", id);
        }
        return true;
    }

    private async Task PruneAsync(IAppDbContext db, DateTime now, CancellationToken ct)
    {
        _prunedOn = DateOnly.FromDateTime(now);
        var cutoff = now.AddDays(-KeepDays);
        var open = AbsentNoticeRules.OpenStatuses.Append(AbsentNoticeRules.Sending).ToList();
        var old = await db.AttendanceNotifyItems
            .Where(i => !open.Contains(i.Status) && i.UpdatedAt < cutoff)
            .Take(5000)
            .ToListAsync(ct);
        if (old.Count == 0) return;
        db.AttendanceNotifyItems.RemoveRange(old);
        await db.SaveChangesAsync(ct);
    }
}
