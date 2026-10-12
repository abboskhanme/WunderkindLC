using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KUNLIK FON ISHLARI BELGISI (<see cref="BackgroundJobRun"/>) — "shu ish shu sana uchun bajarildimi".
///
/// <para><b>NEGA:</b> eslatma xizmatlari "bugun ishladim" belgisini XOTIRADA saqlardi — har
/// deploy/restart (09:00 dan keyin) uni o'chirib, BUTUN ro'yxatga qayta SMS/Telegram/push
/// yuborardi. Endi belgi bazada va ikki darajada ishlatiladi:</para>
/// <list type="number">
///   <item><b>Oluvchi darajasi</b> — yuborishdan OLDIN <see cref="TryClaimAsync"/>
///     (<c>"payment_debt:{studentId}"</c>). Ish yarmida yiqilsa, qayta ishga tushganda olganlarga
///     TAKROR ketmaydi, qolganlariga baribir yetadi. Yiqilish aynan yuborish paytida bo'lsa —
///     o'sha BITTA oluvchi o'tkazib yuboriladi (takror SMS'dan ko'ra arzon).</item>
///   <item><b>Ish darajasi</b> — kunlik ish TO'LIQ tugagach <see cref="TryClaimAsync"/>
///     (<c>"payment_debt"</c>), keyingi uyg'onishda <see cref="IsDoneAsync"/> bilan o'tkazib
///     yuboriladi (butun ro'yxatni qayta o'qish ham kerak emas).</item>
/// </list>
///
/// <para>⚠️ <b>Atomiklik:</b> <see cref="BackgroundJobRun.LastRunDate"/> concurrency token. Ikki
/// nusxa bir vaqtda band qilsa: yangi qatorda — PK buzilishi, mavjud qatorda —
/// <c>UPDATE ... WHERE LastRunDate = eski</c> 0 qator. Ikkala holatda ham yutqazgan tomon
/// <c>false</c> oladi va YUBORMAYDI.</para>
///
/// <para>⚠️ <b>Chaqirish sharti:</b> kontekstda SAQLANMAGAN boshqa o'zgarish bo'lmasin —
/// band qilish o'z <c>SaveChanges</c>ini chaqiradi (Application qatlamida <c>ExecuteUpdate</c> yo'q).
/// Shuning uchun xizmatlar har oluvchidan keyin saqlaydi.</para>
///
/// <para>⚠️ <b>"Band" — FAQAT haqiqiy poyga</b> (2026-10-08). Dispatcher o'z saqlash xatosini yutadi
/// (<c>AutoMessageService.DispatchStudentAsync</c>) va kontekstda saqlanmaydigan "buzuq" qatorlar
/// qoladi. Ilgari band qilishning <c>SaveChanges</c>i shu qoldiq tufayli yiqilar va bu "boshqa nusxa
/// band qildi" deb o'qilardi — qolgan HAMMA oluvchi jimgina o'tkazib yuborilar, kun esa "bajarildi"
/// deb belgilanardi. Endi: (1) band qilishdan OLDIN qoldiq hal qilinadi (<see cref="SettleLeftoversAsync"/>);
/// (2) <c>false</c> faqat concurrency/unikal kalit poygasida (qator bazada SHU sana bilan bor); boshqa
/// har qanday xato TASHQARIGA chiqadi — xizmat uni loglaydi va kunlik ish belgisi QO'YILMAYDI.</para>
/// </summary>
public static class JobRunMarker
{
    /// <summary>Sana kaliti ("yyyy-MM-dd").</summary>
    public static string DateKey(DateOnly date) => date.ToString("yyyy-MM-dd");

    /// <summary>Ish+oluvchi kaliti: <c>"{job}:{id}"</c>.</summary>
    public static string For(string job, string id) => $"{job}:{id}";

    /// <summary>Shu kalit shu sanada allaqachon band qilinganmi (FAQAT o'qish).</summary>
    public static Task<bool> IsDoneAsync(IAppDbContext db, string key, DateOnly date, CancellationToken ct = default)
    {
        var d = DateKey(date);
        return db.BackgroundJobRuns.AsNoTracking().AnyAsync(r => r.Key == key && r.LastRunDate == d, ct);
    }

    /// <summary>
    /// Berilgan kalitlardan QAYSILARI shu sanada band qilingan (FAQAT o'qish, bitta so'rov). Kalit FORMATI o'zgargan
    /// kunlik ishlar ESKI kalitlarni ham "bajarildi" deb o'qishi uchun (deploy kuni takror yubormaslik —
    /// <c>BirthdayGreetingService</c>, <c>PaymentReminderService</c>).
    /// </summary>
    public static async Task<HashSet<string>> DoneKeysAsync(
        IAppDbContext db, IEnumerable<string> keys, DateOnly date, CancellationToken ct = default)
    {
        var list = keys.Distinct(StringComparer.Ordinal).ToList();
        if (list.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
        var d = DateKey(date);
        var done = new HashSet<string>(StringComparer.Ordinal);
        // Katta ro'yxat bitta IN ga sig'maydi (parametr chegarasi) — bo'laklab.
        foreach (var chunk in list.Chunk(500))
            done.UnionWith(await db.BackgroundJobRuns.AsNoTracking()
                .Where(r => chunk.Contains(r.Key) && r.LastRunDate == d).Select(r => r.Key).ToListAsync(ct));
        return done;
    }

    /// <summary>
    /// Kalitni shu sana uchun ATOMIK band qiladi. <c>true</c> — chaqiruvchi yutdi (ishni bajarishi
    /// kerak); <c>false</c> — shu sanada allaqachon band qilingan (boshqa nusxa yoki oldingi urinish).
    /// Bazaga yozib bo'lmasa (poyga EMAS) — xato tashqariga chiqadi (jimgina "band" deb o'qilmaydi).
    /// </summary>
    public static async Task<bool> TryClaimAsync(IAppDbContext db, string key, DateOnly date, CancellationToken ct = default)
    {
        var d = DateKey(date);
        // Chaqiruvchining OLDINGI yiqilgan saqlashidan qolgan o'zgarishlar band qilishni yiqitmasin.
        await SettleLeftoversAsync(db, ct);

        var row = await db.BackgroundJobRuns.FirstOrDefaultAsync(r => r.Key == key, ct);
        if (row is null)
        {
            row = new BackgroundJobRun { Key = key, LastRunDate = d, UpdatedAt = AppClock.Now };
            db.BackgroundJobRuns.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                // Qator kuzatuvda `Added` bo'lib qolsa keyingi HAR SaveChanges yiqilardi — chiqarib tashlaymiz.
                Detach(db, row);
                // PK buzildi — boshqa nusxa shu orada AYNAN shu sana bilan qo'shdi: haqiqiy poyga.
                if (await ClaimedAsync(db, key, d, ct)) return false;
                throw;   // poyga emas (baza yozmayapti va h.k.) — jim "band" emas
            }
        }

        if (row.LastRunDate == d) return false;
        row.LastRunDate = d;
        row.UpdatedAt = AppClock.Now;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Bazadagi sana allaqachon o'zgargan — boshqa nusxa band qildi.
            Detach(db, row);
            return false;
        }
        catch (DbUpdateException)
        {
            Detach(db, row);
            throw;
        }
    }

    /// <summary>Kalit bazada AYNAN shu sana bilan bormi (kuzatuvsiz, yangi o'qish).</summary>
    private static Task<bool> ClaimedAsync(IAppDbContext db, string key, string d, CancellationToken ct) =>
        db.BackgroundJobRuns.AsNoTracking().AnyAsync(r => r.Key == key && r.LastRunDate == d, ct);

    /// <summary>
    /// Kontekstdagi SAQLANMAGAN o'zgarishlarni ("qoldiq" — chaqiruvchining oldingi, yiqilgan
    /// saqlashidan qolgan qatorlar) hal qiladi: avval yana bir bor saqlab ko'radi (xato vaqtinchalik
    /// bo'lgan bo'lsa yozuv yo'qolmaydi); yana yiqilsa — ularni kuzatuvdan CHIQARADI, aks holda ular
    /// keyingi HAR saqlashni (band qilish ham) yiqitardi. Asl xato o'sha joyda allaqachon loglangan.
    /// Qaytaradi: tashlab yuborilgan qatorlar soni. Bazaga ulanib bo'lmasa (DbUpdateException emas) —
    /// xato tashqariga chiqadi: band qilish baribir bo'lmasdi.
    /// </summary>
    public static async Task<int> SettleLeftoversAsync(IAppDbContext db, CancellationToken ct = default)
    {
        if (db is not DbContext ctx) return 0;
        if (!ctx.ChangeTracker.HasChanges()) return 0;
        try
        {
            await db.SaveChangesAsync(ct);
            return 0;
        }
        catch (DbUpdateException)
        {
            var dirty = ctx.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList();
            foreach (var e in dirty) e.State = EntityState.Detached;
            return dirty.Count;
        }
    }

    /// <summary>
    /// Oluvchi darajasidagi ESKI qatorlarni tozalaydi (<paramref name="prefix"/> bilan boshlanadigan,
    /// <paramref name="before"/> sanasidan oldingi). Qator faqat O'Z sanasida ma'noli — jadval
    /// cheksiz o'smasin. Xato yutiladi (tozalash ishning o'zini buzmasin).
    /// </summary>
    public static async Task PruneAsync(IAppDbContext db, string prefix, DateOnly before, CancellationToken ct = default)
    {
        var old = new List<BackgroundJobRun>();
        try
        {
            var cutoff = DateKey(before);
            old = await db.BackgroundJobRuns
                .Where(r => r.Key.StartsWith(prefix) && string.Compare(r.LastRunDate, cutoff) < 0)
                .Take(5000)
                .ToListAsync(ct);
            if (old.Count == 0) return;
            db.BackgroundJobRuns.RemoveRange(old);
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Tozalash — ikkilamchi ish. ⚠️ `Deleted` qatorlar kuzatuvda qolsa keyingi HAR
            // SaveChanges yana yiqilardi — chiqarib tashlaymiz.
            foreach (var r in old) Detach(db, r);
        }
    }

    private static void Detach(IAppDbContext db, BackgroundJobRun row)
    {
        try { db.BackgroundJobRuns.Entry(row).State = EntityState.Detached; }
        catch { /* allaqachon kuzatilmayapti */ }
    }
}
