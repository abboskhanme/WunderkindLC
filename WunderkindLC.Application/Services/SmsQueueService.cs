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
/// hisoblagichini saqlaydi. Ya'ni ulanish uzilsa ham yozuvlar yo'qolmaydi (ilgari barcha SmsLog
/// faqat siklning OXIRIDA saqlanardi — sikl uzilsa tarix umuman bo'sh qolardi).</para>
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

    /// <summary>Bitta oluvchi: raqam, ism (jurnalga) va ALLAQACHON shaxsiylashtirilgan matn.
    /// <paramref name="LeadId"/> berilsa — yuborilgandan keyin lid tarixiga (timeline) yozuv qo'shiladi.</summary>
    public record Target(string Phone, string Name, string Message, string? LeadId = null);

    /// <summary>Bitta partiya ishi. <paramref name="BatchId"/> — allaqachon saqlangan
    /// <see cref="SmsBatch"/> identifikatori (uning <c>SentCount</c>i yuborish davomida yangilanadi).</summary>
    public record Job(
        string BatchId, string Provider, string? AgentId, string? CallbackUrl,
        IReadOnlyList<Target> Targets, string? LeadNote = null, string ActorName = "");

    /// <summary>Jonli holat: jami / ishlangan / muvaffaqiyatli va tugaganmi.</summary>
    public record Progress(int Total, int Done, int Sent, bool Finished);

    private sealed class Entry
    {
        public int Total;
        public int Done;
        public int Sent;
        public bool Finished;
        public DateTime UpdatedUtc = DateTime.UtcNow;
    }

    private readonly Channel<Job> _queue =
        Channel.CreateUnbounded<Job>(new UnboundedChannelOptions { SingleReader = true });

    // Jonli progress — XOTIRADA (bazaga ustun qo'shilmadi). Ilova qayta ishga tushsa yozuv yo'qoladi
    // va progress bazadagi holatdan (SmsLog soni) o'qiladi — qarang: MessagesController "sms/{id}/progress".
    private readonly ConcurrentDictionary<string, Entry> _progress = new();

    /// <summary>Partiya fonda yuborilishi kerakmi (oluvchilar soniga qarab).</summary>
    public static bool ShouldQueue(int recipientCount) => recipientCount > InlineLimit;

    /// <summary>Partiyani navbatga qo'yadi (so'rov darhol tugaydi).</summary>
    public void Enqueue(Job job)
    {
        Prune();
        _progress[job.BatchId] = new Entry { Total = job.Targets.Count };
        _queue.Writer.TryWrite(job);
    }

    /// <summary>Navbatdagi/yaqinda tugagan partiyaning holati; xotirada bo'lmasa null.</summary>
    public Progress? Get(string batchId) =>
        _progress.TryGetValue(batchId, out var e) ? new Progress(e.Total, e.Done, e.Sent, e.Finished) : null;

    /// <summary>Partiyani SHU YERDA (chaqiruvchining tranzaksiyasida) yuboradi — kichik partiyalar uchun.
    /// Yuborilgan (muvaffaqiyatli) SMS sonini qaytaradi.</summary>
    public Task<int> RunInlineAsync(IAppDbContext db, Job job) =>
        // ATAYIN CancellationToken.None: mijoz ulanishni uzsa ham yuborilgan SMS jurnalga tushsin
        // (aks holda pul ketib, tarixda iz qolmasdi).
        SendJobAsync(db, job, null, CancellationToken.None);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try { await RunQueuedAsync(job, stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { logger.LogError(ex, "SMS navbatida xatolik (partiya {BatchId})", job.BatchId); }
            }
        }
        catch (OperationCanceledException) { /* ilova to'xtadi */ }
    }

    private async Task RunQueuedAsync(Job job, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var entry = _progress.GetOrAdd(job.BatchId, _ => new Entry { Total = job.Targets.Count });
        await SendJobAsync(db, job, entry, ct);
    }

    private async Task<int> SendJobAsync(IAppDbContext db, Job job, Entry? entry, CancellationToken ct)
    {
        var batch = await db.SmsBatches.FirstOrDefaultAsync(b => b.Id == job.BatchId, ct);
        var sent = 0;
        foreach (var t in job.Targets)
        {
            if (ct.IsCancellationRequested) break;
            var ok = false;
            // `SendOneAsync` oxirida lid tarixiga `note` hodisasi qo'shiladi — u YOZILGANINI
            // faqat metod istisnosiz qaytganidan bilamiz (aks holda kartani bekorga yangilardik).
            var noted = false;
            try
            {
                ok = await SendOneAsync(db, job, t, ct);
                noted = true;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SMS yuborishda kutilmagan xato ({Phone})", t.Phone);
            }
            if (ok) sent++;
            if (batch is not null) batch.SentCount = sent;
            // HAR SMS'dan keyin saqlaymiz — uzilib qolsa ham yuborilganlar tarixda qoladi.
            try { await db.SaveChangesAsync(ct); }
            catch (Exception ex) { logger.LogWarning(ex, "SMS jurnalini saqlashda xato (partiya {BatchId})", job.BatchId); }
            // Lid kartasi JORIY holatga keltiriladi: yangi `note` hodisasi kartada ko'rinadi,
            // sinxronizatsiyasiz esa karta JIMGINA eskirardi. SaveChanges'dan KEYIN — karta
            // bazadagi yozilgan holatdan quriladi. Kartasi yo'q lidga yangi xabar YUBORILMAYDI.
            // ⚠️ TelegramService konstruktorga EMAS, mavjud provayderdan olinadi: u singleton,
            // ya'ni ildiz provayderdan xavfsiz chiqadi va navbat xizmatining imzosi o'zgarmaydi.
            // Sozlanmagan/ro'yxatdan o'tmagan bo'lsa (masalan testda) `null` qaytadi va SMS
            // yuborish oqimi hech qanday o'zgarishsiz davom etadi.
            if (noted && t.LeadId is { Length: > 0 } cardLeadId && job.LeadNote is { Length: > 0 }
                && services.GetService<TelegramService>() is { } telegram)
                await LeadNotifier.SyncCardAsync(db, telegram, cardLeadId, ct);
            if (entry is not null)
            {
                entry.Done++;
                entry.Sent = sent;
                entry.UpdatedUtc = DateTime.UtcNow;
            }
        }
        if (entry is not null)
        {
            entry.Finished = true;
            entry.UpdatedUtc = DateTime.UtcNow;
        }
        return sent;
    }

    /// <summary>Bitta raqamga yuboradi. Local'da SmsLog'ni <see cref="CtiSmsService"/> o'zi yozadi,
    /// Eskiz'da shu yerda yoziladi (SaveChanges — chaqiruvchida).</summary>
    private async Task<bool> SendOneAsync(IAppDbContext db, Job job, Target t, CancellationToken ct)
    {
        bool ok;
        if (job.Provider == "local")
        {
            var lr = await ctiSms.SendSmsAsync(db, job.AgentId, t.Phone, t.Message, t.Name, job.BatchId, ct);
            ok = lr.Ok;
        }
        else
        {
            var r = await eskiz.SendSmsAsync(db, t.Phone, t.Message, job.CallbackUrl, ct);
            ok = r.Ok;
            db.SmsLogs.Add(new SmsLog
            {
                BatchId = job.BatchId,
                PhoneNumber = EskizService.NormalizePhone(t.Phone),
                RecipientName = t.Name,
                Message = t.Message,
                RequestId = r.RequestId,
                Status = r.Ok ? r.Status : (r.Error ?? "error"),
            });
        }
        if (t.LeadId is { Length: > 0 } leadId && job.LeadNote is { Length: > 0 })
        {
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = leadId, Type = "note", ActorName = job.ActorName,
                CreatedAt = AppClock.Iso(), Text = job.LeadNote,
            });
        }
        return ok;
    }

    /// <summary>Eski (bir soatdan oldin tugagan) progress yozuvlarini tozalaydi — xotira o'smasin.</summary>
    private void Prune()
    {
        var limit = DateTime.UtcNow.AddHours(-1);
        foreach (var (id, e) in _progress)
            if (e.Finished && e.UpdatedUtc < limit) _progress.TryRemove(id, out _);
    }
}
