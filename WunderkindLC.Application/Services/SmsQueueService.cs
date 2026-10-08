using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// OMMAVIY SMS NAVBATI — ko'p oluvchili partiya HTTP so'rovi ichida emas, FONDA yuboriladi.
///
/// <para><b>Nega kerak?</b> SMS'lar bittalab ketadi: Eskiz'da har raqamga alohida HTTP so'rov
/// (~0.5–1.5 s), Local SMS'da esa yuborishlar orasida <see cref="CenterMeta.LocalSmsDelaySeconds"/>
/// kutish bor (agent telefoni oflayn bo'lsa yana ~6 s "uyg'otish"). 100 ta oluvchi = bir necha
/// daqiqa. Ilova oldida Cloudflare Tunnel turadi va u javobni <b>100 soniya</b> kutadi — undan
/// oshsa ulanishni uzadi va brauzerda "Yuborishda xatolik" chiqadi. SMS'lar esa aslida ketayotgan
/// bo'ladi, faqat admin buni bilmaydi (va odatda qayta yuboradi — pul ikki marta ketadi).</para>
///
/// <para><b>Yechim:</b> controller oluvchilar ro'yxatini yig'adi, <see cref="SmsBatch"/>ni DARHOL
/// yozadi (tarixda o'sha zahoti ko'rinadi) va navbatga qo'yadi — so'rov bir soniyada tugaydi.
/// Fon ishchisi esa bittalab yuboradi va HAR YUBORISHDAN KEYIN <see cref="SmsLog"/> + partiya
/// hisoblagichini saqlaydi.</para>
///
/// <para>⚠️ <b>NAVBAT BAZADA</b> (<see cref="SmsQueueJob"/> + har oluvchi <see cref="SmsQueueItem"/>).
/// Ilgari navbat faqat xotirada edi: yuborish paytida deploy/restart bo'lsa qolgan oluvchilar
/// JIMGINA tushib qolar, progress esa bazadan "tugadi 40/300" bo'lib ko'rinardi. Endi:</para>
/// <list type="bullet">
///   <item>ilova ishga tushganda tugamagan partiya DAVOM ETTIRILADI — faqat <c>pending</c>
///     oluvchilarga (allaqachon ketganlarga qayta yuborilmaydi);</item>
///   <item>har oluvchi yuborishdan OLDIN <c>sending</c> deb band qilinadi; aynan yuborish paytida
///     to'xtagan oluvchi <c>unknown</c> bo'ladi va QAYTA yuborilmaydi (SMS ketgan bo'lishi mumkin —
///     takror pul/xabardan ko'ra bitta yo'qotish arzon);</item>
///   <item><see cref="ResumeWindow"/> dan eski partiya davom ettirilmaydi (ertasi kuni kechagi
///     e'lon ketmasin) — <c>interrupted</c> deb yopiladi va progress buni OCHIQ ko'rsatadi.</item>
/// </list>
///
/// <para>Kichik partiya (<see cref="InlineLimit"/> gacha — bitta o'quvchi/lidga yuborish) avvalgidek
/// so'rov ichida ketadi: admin natijani darhol ko'radi va bu yerda kutish xavfi yo'q.</para>
/// </summary>
public class SmsQueueService(
    IServiceProvider services, EskizService eskiz, CtiSmsService ctiSms,
    ILogger<SmsQueueService> logger) : BackgroundService
{
    /// <summary>Shu songacha bo'lgan oluvchi so'rov ICHIDA yuboriladi (darhol natija ko'rsatish uchun);
    /// undan ko'pi navbatga tushadi. 3 ta eng sekin holatda ham ~20 soniya — proksi chegarasidan uzoq.</summary>
    public const int InlineLimit = 3;

    /// <summary>Restartdan keyin shu muddatdan ESKI tugamagan partiya davom ettirilmaydi (bekor qilinadi).</summary>
    public static readonly TimeSpan ResumeWindow = TimeSpan.FromHours(12);

    /// <summary>Tugagan partiyaning oluvchi qatorlari shuncha kundan keyin tozalanadi (SmsLog qoladi).</summary>
    private const int KeepFinishedDays = 7;

    /// <summary>Bitta oluvchi: raqam, ism (jurnalga) va ALLAQACHON shaxsiylashtirilgan matn.
    /// <paramref name="LeadId"/> berilsa — yuborilgandan keyin lid tarixiga (timeline) yozuv qo'shiladi.</summary>
    public record Target(string Phone, string Name, string Message, string? LeadId = null);

    /// <summary>Bitta partiya ishi. <paramref name="BatchId"/> — allaqachon saqlangan
    /// <see cref="SmsBatch"/> identifikatori (uning <c>SentCount</c>i yuborish davomida yangilanadi).</summary>
    public record Job(
        string BatchId, string Provider, string? AgentId, string? CallbackUrl,
        IReadOnlyList<Target> Targets, string? LeadNote = null, string ActorName = "");

    /// <summary>Jonli holat: jami / ishlangan / muvaffaqiyatli va tugaganmi.
    /// <paramref name="Resumed"/> — restartdan keyin DAVOM ETTIRILGAN; <paramref name="Interrupted"/> —
    /// restartda juda eski bo'lgani uchun qolgani BEKOR qilingan (Done &lt; Total — bu xato emas).</summary>
    public record Progress(int Total, int Done, int Sent, bool Finished, bool Resumed = false, bool Interrupted = false);

    private sealed class Entry
    {
        public int Total;
        public int Done;
        public int Sent;
        public bool Finished;
        public bool Resumed;
        public DateTime UpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>Navbatda faqat partiya id'si — ish (oluvchilar) bazadan o'qiladi.</summary>
    private readonly Channel<string> _queue =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    // Jonli progress — XOTIRADA (tezlik uchun). Ilova qayta ishga tushsa bazadan tiklanadi
    // (<see cref="ProgressFromDbAsync"/>).
    private readonly ConcurrentDictionary<string, Entry> _progress = new();

    /// <summary>Partiya fonda yuborilishi kerakmi (oluvchilar soniga qarab).</summary>
    public static bool ShouldQueue(int recipientCount) => recipientCount > InlineLimit;

    /// <summary>
    /// Partiyani BAZAGA yozadi (ish + har oluvchi) va navbatga qo'yadi — so'rov darhol tugaydi.
    /// Ilova shu orada to'xtasa, keyingi ishga tushishda partiya davom ettiriladi.
    /// </summary>
    public async Task EnqueueAsync(IAppDbContext db, Job job, CancellationToken ct = default)
    {
        Prune();
        db.SmsQueueJobs.Add(new SmsQueueJob
        {
            BatchId = job.BatchId, Provider = job.Provider, AgentId = job.AgentId,
            CallbackUrl = job.CallbackUrl, LeadNote = job.LeadNote, ActorName = job.ActorName,
            State = "pending", CreatedAt = AppClock.Now,
        });
        var seq = 0;
        foreach (var t in job.Targets)
            db.SmsQueueItems.Add(new SmsQueueItem
            {
                BatchId = job.BatchId, Seq = seq++, Phone = t.Phone, Name = t.Name,
                Message = t.Message, LeadId = t.LeadId, Status = "pending", UpdatedAt = AppClock.Now,
            });
        await db.SaveChangesAsync(ct);
        _progress[job.BatchId] = new Entry { Total = job.Targets.Count };
        _queue.Writer.TryWrite(job.BatchId);
    }

    /// <summary>Navbatdagi/yaqinda tugagan partiyaning holati; xotirada bo'lmasa null.</summary>
    public Progress? Get(string batchId) =>
        _progress.TryGetValue(batchId, out var e)
            ? new Progress(e.Total, e.Done, e.Sent, e.Finished, e.Resumed)
            : null;

    /// <summary>
    /// Partiya holati BAZADAN (xotirada yo'q — ilova qayta ishga tushgan yoki partiya ancha oldin
    /// tugagan). Navbat qatorlari bo'lsa — ulardan (tugamagan bo'lsa <c>Finished=false</c>, ya'ni
    /// davom ettirilishi kutilmoqda); bo'lmasa (kichik/eski partiya) — <see cref="SmsLog"/> sonidan.
    /// Partiya topilmasa <c>null</c>.
    /// </summary>
    public static async Task<Progress?> ProgressFromDbAsync(IAppDbContext db, string batchId, CancellationToken ct = default)
    {
        var batch = await db.SmsBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null) return null;
        var job = await db.SmsQueueJobs.AsNoTracking().FirstOrDefaultAsync(j => j.BatchId == batchId, ct);
        if (job is not null)
        {
            var counts = await db.SmsQueueItems.AsNoTracking()
                .Where(i => i.BatchId == batchId)
                .GroupBy(i => i.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var total = counts.Sum(c => c.Count);
            if (total > 0)
            {
                var done = counts.Where(c => IsProcessed(c.Status)).Sum(c => c.Count);
                return new Progress(total, done, batch.SentCount, job.State != "pending",
                    job.ResumeCount > 0, job.State == "interrupted");
            }
        }
        var logged = await db.SmsLogs.CountAsync(l => l.BatchId == batchId, ct);
        return new Progress(batch.RecipientCount, logged, batch.SentCount, true);
    }

    /// <summary>Oluvchi qatori "ishlangan" hisoblanadimi (progress sanog'i uchun). ⚠️ BEKOR qilingani
    /// (<c>cancelled</c>) KIRMAYDI — to'xtatilgan partiyada "Total − Done" = yuborilmay qolganlar soni.</summary>
    private static bool IsProcessed(string status) => status is "sent" or "failed" or "unknown";

    /// <summary>Partiyani SHU YERDA (chaqiruvchining tranzaksiyasida) yuboradi — kichik partiyalar uchun.
    /// Yuborilgan (muvaffaqiyatli) SMS sonini qaytaradi.</summary>
    public async Task<int> RunInlineAsync(IAppDbContext db, Job job)
    {
        // ATAYIN CancellationToken.None: mijoz ulanishni uzsa ham yuborilgan SMS jurnalga tushsin
        // (aks holda pul ketib, tarixda iz qolmasdi).
        var ct = CancellationToken.None;
        var batch = await db.SmsBatches.FirstOrDefaultAsync(b => b.Id == job.BatchId, ct);
        var sent = 0;
        foreach (var t in job.Targets)
        {
            var ok = false;
            var noted = false;
            try
            {
                ok = await SendOneAsync(db, job.BatchId, job.Provider, job.AgentId, job.CallbackUrl, job.LeadNote, job.ActorName, t, ct);
                noted = true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SMS yuborishda kutilmagan xato (partiya {BatchId})", job.BatchId);
            }
            if (ok) sent++;
            if (batch is not null) batch.SentCount = sent;
            try { await db.SaveChangesAsync(ct); }
            catch (Exception ex) { logger.LogWarning(ex, "SMS jurnalini saqlashda xato (partiya {BatchId})", job.BatchId); }
            if (noted) await SyncLeadCardAsync(db, t.LeadId, job.LeadNote, ct);
        }
        return sent;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Avval — oldingi ishga tushishdan qolgan (tugamagan) partiyalar.
        try { await ResumePendingAsync(stoppingToken); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { logger.LogError(ex, "SMS navbati: tugamagan partiyalarni tiklashda xatolik"); }

        try
        {
            await foreach (var batchId in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try { await RunQueuedAsync(batchId, stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.LogError(ex, "SMS navbatida xatolik (partiya {BatchId})", batchId); }
            }
        }
        catch (OperationCanceledException) { /* ilova to'xtadi — partiya bazada qoladi, keyin davom etadi */ }
    }

    /// <summary>
    /// Ilova ishga tushganda: tugamagan partiyalarni topadi va navbatga qaytaradi (yoki juda eskisini
    /// bekor qiladi). Yuborish paytida to'xtagan oluvchi <c>unknown</c> bo'ladi — QAYTA yuborilmaydi.
    /// <para>⚠️ <c>sending</c> qatorlarning HAMMASI <c>unknown</c> bo'ladi (yoshidan qat'i nazar): bu metod
    /// faqat ishga tushishda, hali HECH BIR ishchi ishlamayotganda chaqiriladi — ya'ni ularning har biri
    /// oldingi jarayonda uzilgan. Ilgari faqat 2 daqiqadan eskilari olinardi: tez restartdan keyin yangi
    /// uzilgan qator abadiy <c>sending</c> bo'lib qolar va partiya sanog'i hech qachon tugamasdi.</para>
    /// </summary>
    private async Task ResumePendingAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var now = AppClock.Now;

        var jobs = await db.SmsQueueJobs.Where(j => j.State == "pending").ToListAsync(ct);
        foreach (var job in jobs)
        {
            var items = await db.SmsQueueItems.Where(i => i.BatchId == job.BatchId).ToListAsync(ct);
            foreach (var i in items.Where(i => i.Status == "sending"))
            {
                i.Status = "unknown";
                i.UpdatedAt = now;
            }

            var pending = items.Count(i => i.Status == "pending");
            if (job.CreatedAt < now - ResumeWindow)
            {
                // Juda eski — kechagi e'lonni bugun yuborib yubormaymiz. Qolgani OCHIQ bekor qilinadi.
                foreach (var i in items.Where(i => i.Status == "pending"))
                {
                    i.Status = "cancelled";
                    i.UpdatedAt = now;
                }
                job.State = "interrupted";
                job.FinishedAt = now;
                logger.LogWarning(
                    "SMS navbati: partiya {BatchId} juda eski — davom ettirilmadi, {Pending} oluvchi bekor qilindi.",
                    job.BatchId, pending);
                continue;
            }

            job.ResumeCount++;
            var batch = await db.SmsBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == job.BatchId, ct);
            _progress[job.BatchId] = new Entry
            {
                Total = items.Count,
                Done = items.Count(i => IsProcessed(i.Status)),
                Sent = batch?.SentCount ?? 0,
                Resumed = true,
            };
            _queue.Writer.TryWrite(job.BatchId);
            logger.LogInformation("SMS navbati: partiya {BatchId} davom ettirilmoqda ({Pending} oluvchi qolgan).",
                job.BatchId, pending);
        }
        await db.SaveChangesAsync(ct);

        // Ancha oldin tugagan partiyalarning oluvchi qatorlari (SmsLog o'zida qoladi).
        var cutoff = now.AddDays(-KeepFinishedDays);
        var oldJobs = await db.SmsQueueJobs
            .Where(j => j.State != "pending" && j.FinishedAt != null && j.FinishedAt < cutoff)
            .Take(200).ToListAsync(ct);
        if (oldJobs.Count > 0)
        {
            var ids = oldJobs.Select(j => j.BatchId).ToList();
            db.SmsQueueItems.RemoveRange(await db.SmsQueueItems.Where(i => ids.Contains(i.BatchId)).ToListAsync(ct));
            db.SmsQueueJobs.RemoveRange(oldJobs);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Bitta navbatdagi partiyani bazadagi oluvchilar bo'yicha yuboradi (faqat <c>pending</c>lar).</summary>
    private async Task RunQueuedAsync(string batchId, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var job = await db.SmsQueueJobs.FirstOrDefaultAsync(j => j.BatchId == batchId, ct);
        if (job is null || job.State != "pending") return;
        var batch = await db.SmsBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        var items = await db.SmsQueueItems.Where(i => i.BatchId == batchId)
            .OrderBy(i => i.Seq).ToListAsync(ct);

        var entry = _progress.GetOrAdd(batchId, _ => new Entry());
        entry.Total = items.Count;
        entry.Done = items.Count(i => IsProcessed(i.Status));
        var sent = batch?.SentCount ?? 0;
        entry.Sent = sent;

        foreach (var item in items.Where(i => i.Status == "pending").ToList())
        {
            // To'xtatilsa — qolganlari `pending` bo'lib bazada qoladi va keyingi ishga tushishda davom etadi.
            if (ct.IsCancellationRequested) return;

            // BAND QILISH (concurrency token): boshqa ishchi allaqachon olgan bo'lsa — o'tkazib yuboramiz.
            item.Status = "sending";
            item.UpdatedAt = AppClock.Now;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                Detach(db, item);
                continue;
            }
            catch (DbUpdateException ex)
            {
                // Baza vaqtincha yozmayapti — yuborishni TO'XTATAMIZ (band qilinmagan SMS ketmasin),
                // partiya `pending` bo'lib qoladi va bir daqiqadan keyin qayta navbatga tushadi.
                logger.LogWarning(ex, "SMS navbati: partiya {BatchId} bazaga yozilmadi — keyinroq davom etadi", batchId);
                Detach(db, item);
                RetryLater(batchId, ct);
                return;
            }

            var ok = false;
            var noted = false;
            try
            {
                // ATAYIN CancellationToken.None: yuborish boshlangan SMS jurnalga yozilmay qolmasin.
                ok = await SendOneAsync(db, batchId, job.Provider, job.AgentId, job.CallbackUrl, job.LeadNote, job.ActorName,
                    new Target(item.Phone, item.Name, item.Message, item.LeadId), CancellationToken.None);
                noted = true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SMS yuborishda kutilmagan xato (partiya {BatchId})", batchId);
            }
            if (ok) sent++;
            if (batch is not null) batch.SentCount = sent;
            item.Status = ok ? "sent" : "failed";
            item.UpdatedAt = AppClock.Now;
            // HAR SMS'dan keyin saqlaymiz — uzilib qolsa ham yuborilganlar tarixda qoladi.
            try { await db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SMS jurnalini saqlashda xato (partiya {BatchId})", batchId);
                // Oluvchi qatori boshqa ishchi tomonidan o'zgartirilgan bo'lsa (concurrency) — jurnal
                // baribir saqlansin.
                Detach(db, item);
                try { await db.SaveChangesAsync(CancellationToken.None); }
                catch (Exception ex2) { logger.LogWarning(ex2, "SMS jurnali qayta saqlanmadi (partiya {BatchId})", batchId); }
            }
            if (noted) await SyncLeadCardAsync(db, item.LeadId, job.LeadNote, CancellationToken.None);

            entry.Done++;
            entry.Sent = sent;
            entry.UpdatedUtc = DateTime.UtcNow;
        }

        // Hamma oluvchi ishlandi — partiya yopiladi.
        job.State = "done";
        job.FinishedAt = AppClock.Now;
        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "SMS navbati: partiya {BatchId} yopilmadi", batchId); }
        entry.Finished = true;
        entry.UpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>Lid kartasi JORIY holatga keltiriladi: yangi `note` hodisasi kartada ko'rinadi,
    /// sinxronizatsiyasiz esa karta JIMGINA eskirardi. SaveChanges'dan KEYIN — karta bazadagi yozilgan
    /// holatdan quriladi. Kartasi yo'q lidga yangi xabar YUBORILMAYDI.
    /// ⚠️ TelegramService konstruktorga EMAS, mavjud provayderdan olinadi: u singleton, ya'ni ildiz
    /// provayderdan xavfsiz chiqadi va navbat xizmatining imzosi o'zgarmaydi. Sozlanmagan/ro'yxatdan
    /// o'tmagan bo'lsa (masalan testda) `null` qaytadi va SMS oqimi o'zgarishsiz davom etadi.</summary>
    private async Task SyncLeadCardAsync(IAppDbContext db, string? leadId, string? leadNote, CancellationToken ct)
    {
        if (leadId is { Length: > 0 } && leadNote is { Length: > 0 }
            && services.GetService<TelegramService>() is { } telegram)
            await LeadNotifier.SyncCardAsync(db, telegram, leadId, ct);
    }

    /// <summary>Bitta raqamga yuboradi. Local'da SmsLog'ni <see cref="CtiSmsService"/> o'zi yozadi,
    /// Eskiz'da shu yerda yoziladi (SaveChanges — chaqiruvchida).</summary>
    private async Task<bool> SendOneAsync(
        IAppDbContext db, string batchId, string provider, string? agentId, string? callbackUrl, string? leadNote,
        string actorName, Target t, CancellationToken ct)
    {
        bool ok;
        if (provider == "local")
        {
            var lr = await ctiSms.SendSmsAsync(db, agentId, t.Phone, t.Message, t.Name, batchId, ct);
            ok = lr.Ok;
        }
        else
        {
            var r = await eskiz.SendSmsAsync(db, t.Phone, t.Message, callbackUrl, ct);
            ok = r.Ok;
            db.SmsLogs.Add(new SmsLog
            {
                BatchId = batchId,
                PhoneNumber = EskizService.NormalizePhone(t.Phone),
                RecipientName = t.Name,
                Message = t.Message,
                RequestId = r.RequestId,
                Status = r.Ok ? r.Status : (r.Error ?? "error"),
            });
        }
        if (t.LeadId is { Length: > 0 } leadId && leadNote is { Length: > 0 })
        {
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = leadId, Type = "note", ActorName = actorName,
                CreatedAt = AppClock.Iso(), Text = leadNote,
            });
        }
        return ok;
    }

    /// <summary>Partiyani bir daqiqadan keyin navbatga qaytaradi (vaqtinchalik baza xatosidan keyin).</summary>
    private void RetryLater(string batchId, CancellationToken ct) =>
        _ = Task.Delay(TimeSpan.FromMinutes(1), ct).ContinueWith(t =>
        {
            if (!t.IsCanceled) _queue.Writer.TryWrite(batchId);
        }, TaskScheduler.Default);

    private static void Detach(IAppDbContext db, SmsQueueItem item)
    {
        try { db.SmsQueueItems.Entry(item).State = EntityState.Detached; }
        catch { /* allaqachon kuzatilmayapti */ }
    }

    /// <summary>Eski (bir soatdan oldin tugagan) progress yozuvlarini tozalaydi — xotira o'smasin.</summary>
    private void Prune()
    {
        var limit = DateTime.UtcNow.AddHours(-1);
        foreach (var (id, e) in _progress)
            if (e.Finished && e.UpdatedUtc < limit) _progress.TryRemove(id, out _);
    }
}
