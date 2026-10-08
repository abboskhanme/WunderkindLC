using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// OMMAVIY DAVOMAT XABARLARI ISHCHISI — bazadagi navbatni (<see cref="AttendanceNotifyItem"/>) bittalab
/// yuboradi. Navbatga yozuvchi — <see cref="AttendanceNotify.EnqueueBulkAsync"/> (jurnal yozuvi bilan
/// bitta SaveChanges da), controller esa saqlagach <see cref="Wake"/> ni chaqiradi.
///
/// <para><b>Restartga chidamli:</b> ishga tushganda (<see cref="RecoverAsync"/>) avvalgi jarayondan
/// qolgan <c>pending</c> qatorlar yuboriladi; aynan yuborish paytida uzilgan (<c>sending</c>) qator
/// <c>unknown</c> bo'ladi va QAYTA yuborilmaydi (xabar ketgan bo'lishi mumkin). Har oluvchi yuborishdan
/// OLDIN band qilinadi (concurrency token) — bir xabar ikki marta ketmaydi.</para>
///
/// <para>Uyg'otish yo'qolsa ham (masalan saqlash va <see cref="Wake"/> orasida restart) ishchi har
/// <see cref="PollInterval"/> da navbatni o'zi tekshiradi.</para>
///
/// <para>⚠️ Har oluvchi YANGI DI scope'da (o'z DbContext'i bilan) yuboriladi, band qilish/yakunlash esa
/// alohida kontekstda: dispatcher o'z saqlash xatosini yutadi va kontekstda "buzuq" qatorlar qolishi
/// mumkin — ular keyingi oluvchining band qilinishini yiqitmasin (JobRunMarker'dagi saboq).</para>
/// </summary>
public class AttendanceNotifyWorker(
    IServiceProvider services, AutoMessageService autoMsg, ILogger<AttendanceNotifyWorker> logger) : BackgroundService
{
    /// <summary>Shundan ESKI navbat qatori yuborilmaydi (<c>expired</c>): uzoq to'xtashdan keyin
    /// kechagi "darsga kelmadi" ertasiga ketib, ota-onani chalg'itmasin.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    /// <summary>Yakunlangan qatorlar shuncha kundan keyin tozalanadi (SMS jurnali o'z joyida qoladi).</summary>
    public const int KeepDays = 7;

    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 200;

    private readonly SemaphoreSlim _signal = new(0, 1);
    private DateOnly _prunedOn = DateOnly.MinValue;

    /// <summary>Navbatga yangi qator yozildi — ishchini darhol uyg'otadi (kutmasdan).</summary>
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
        var stuck = await db.AttendanceNotifyItems.Where(i => i.Status == "sending").ToListAsync(ct);
        foreach (var i in stuck)
        {
            i.Status = "unknown";
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

    /// <summary>Navbatdagi barcha <c>pending</c> qatorlarni (eskisidan boshlab) yuboradi.</summary>
    internal async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        var processed = 0;
        while (!ct.IsCancellationRequested)
        {
            List<string> ids;
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                ids = await db.AttendanceNotifyItems.AsNoTracking()
                    .Where(i => i.Status == "pending")
                    .OrderBy(i => i.CreatedAt)
                    .Select(i => i.Id)
                    .Take(BatchSize)
                    .ToListAsync(ct);
            }
            if (ids.Count == 0) break;
            foreach (var id in ids)
            {
                // To'xtatilsa — qolganlari `pending` bo'lib bazada qoladi, keyingi ishga tushishda yuboriladi.
                if (ct.IsCancellationRequested) return processed;
                if (await ProcessOneAsync(id, ct)) processed++;
            }
        }

        var today = AppClock.Today;
        if (_prunedOn != today)
        {
            using var scope = services.CreateScope();
            await PruneAsync(scope.ServiceProvider.GetRequiredService<IAppDbContext>(), AppClock.Now, ct);
        }
        return processed;
    }

    /// <summary>
    /// Bitta qator: band qilish → yuborish → yakunlash. <c>true</c> — shu chaqiruv qatorni ishladi.
    /// Band qilish bazaga yozilmasa (vaqtinchalik xato) xato TASHQARIGA chiqadi: hech narsa yuborilmaydi,
    /// qator <c>pending</c> bo'lib qoladi va keyingi aylanishda qayta uriniladi.
    /// </summary>
    private async Task<bool> ProcessOneAsync(string id, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var item = await db.AttendanceNotifyItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null || item.Status != "pending") return false;

        var now = AppClock.Now;
        if (item.CreatedAt < now - MaxAge)
        {
            item.Status = "expired";
            item.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Davomat xabari {Id} juda kech ({Created}) — yuborilmadi.", id, item.CreatedAt);
            return true;
        }

        // BAND QILISH (concurrency token): boshqa ishchi allaqachon olgan bo'lsa — o'tkazib yuboramiz.
        item.Status = "sending";
        item.UpdatedAt = now;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return false; }

        var status = "done";
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
            status = "failed";
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
        var old = await db.AttendanceNotifyItems
            .Where(i => i.Status != "pending" && i.Status != "sending" && i.UpdatedAt < cutoff)
            .Take(5000)
            .ToListAsync(ct);
        if (old.Count == 0) return;
        db.AttendanceNotifyItems.RemoveRange(old);
        await db.SaveChangesAsync(ct);
    }
}
