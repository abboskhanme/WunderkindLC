using System.Collections.Concurrent;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WunderkindLC.Application.Services;

/// <summary>
/// SERTIFIKAT YARATISH — FON ISHI va uning holati.
///
/// <para><b>Nega fonda?</b> Ilgari generatsiya so'rov ICHIDA bajarilardi: 30 kishilik guruhda javob
/// 30-60 soniya kutilar, foydalanuvchi esa bo'sh ekranga qarab turardi. Cloudflare esa 100 soniyadan
/// oshgan so'rovni uzib, <c>524</c> qaytaradi — ya'ni katta guruhda sertifikatlar yaratilsa ham
/// foydalanuvchi xato ko'rardi. Endi so'rov ishni BOSHLAB darhol javob beradi, UI esa holatni
/// so'rab turadi (<see cref="Status"/>).</para>
///
/// <para><b>Nega bo'laklab?</b> <see cref="TestCertificateService.ChunkSize"/> izohiga qarang:
/// har bo'lakdan keyin yozuvlar bazaga tushadi, shuning uchun tayyor sertifikatlar ish tugashini
/// kutmasdan ro'yxatda ko'rinadi va yuklab olinadi.</para>
///
/// <para><b>Holat XOTIRADA saqlanadi</b> (baza jadvali emas — ish bir necha daqiqa yashaydi).
/// Server qayta ishga tushsa holat yo'qoladi: bunda UI <c>Running=false</c> ko'radi va bazadagi
/// tayyor sertifikatlarni ko'rsatadi — chala qolgani uchun tugmani qayta bosish yetarli
/// (generatsiya IDEMPOTENT: mavjudlari yangilanadi, nusxa chiqmaydi).</para>
/// </summary>
public class TestCertificateJobs(IServiceProvider services, ILogger<TestCertificateJobs> logger)
{
    /// <summary>Tugagan ish holati shuncha vaqt saqlanadi — UI oxirgi natijani (xato ham) ko'rsin.</summary>
    private static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Ish holati. Maydonlarni FON oqimi yozadi, so'rov oqimlari o'qiydi — shuning uchun
    /// <c>volatile</c> (aks holda o'qigan oqim eskirgan qiymatni ko'rib qolishi mumkin).
    /// </summary>
    private sealed class JobState
    {
        public int Total;
        public volatile int Done;
        public volatile bool Running = true;
        public volatile string? Error;
        /// <summary>FAQAT <see cref="Running"/> false bo'lgach o'qiladi (undan OLDIN yoziladi).</summary>
        public DateTime FinishedAt;
    }

    private readonly ConcurrentDictionary<string, JobState> _jobs = new();

    /// <summary>
    /// Test bo'yicha generatsiyani BOSHLAYDI va darhol qaytadi.
    /// </summary>
    /// <param name="db">So'rovning DbContext'i — FAQAT tekshirish uchun (fon ishi o'ziniki ochadi).</param>
    /// <param name="certs">So'rovning servisi — shuningdek faqat tekshirish uchun.</param>
    /// <returns>Boshlang'ich holat; tekshiruvda xato bo'lsa <c>Error</c> (ish boshlanmaydi).</returns>
    public async Task<(TestCertificateJobDto Job, string? Error)> StartAsync(
        IAppDbContext db, TestCertificateService certs, string testId, string actor,
        CancellationToken ct = default)
    {
        Prune();

        // O'RINNI ATOMIK EGALLAYMIZ. "Avval tekshirib, keyin qo'yish" YARAMAYDI: ikki foydalanuvchi
        // (yoki ikki marta bosilgan tugma) tekshiruvdan BIRGA o'tib ketardi va bir xil test uchun
        // ikkita fon ishi bir xil fayllarni yozib, raqamlarni chalkashtirib yuborardi.
        // `AddOrUpdate` — lug'at ustidagi atomik amal: ish ketayotgan bo'lsa mavjudini QOLDIRADI,
        // aks holda bizniki qo'yiladi. G'olib kim ekani havola (reference) bo'yicha aniqlanadi.
        var mine = new JobState { Total = 0, Done = 0, Running = true };
        var winner = _jobs.AddOrUpdate(testId, mine, (_, existing) => existing.Running ? existing : mine);
        if (!ReferenceEquals(winner, mine))
            return (Snapshot(winner), null);   // boshqasi allaqachon boshlagan — joriy holatni qaytaramiz

        // Tekshiruv SO'ROV ichida: xato bo'lsa (shablon yo'q, ball kiritilmagan) foydalanuvchi uni
        // darhol ko'radi, "fonda boshlandi" deb aldanib qolmaydi.
        var (total, error) = await certs.ExpectedCountAsync(db, testId, ct);
        if (error != null)
        {
            // Egallagan o'rinni bo'shatamiz — aks holda test "abadiy yaratilmoqda" bo'lib qolardi.
            _jobs.TryRemove(new KeyValuePair<string, JobState>(testId, mine));
            return (new TestCertificateJobDto(false, 0, 0, DocxToPdfConverter.IsAvailable, error, null, []), error);
        }

        mine.Total = total;

        // DIQQAT: so'rovning `ct` si BERILMAYDI — javob yuborilgach u bekor qilinadi va ish
        // yarim yo'lda to'xtab qolardi. Fon ishi o'z hayotiga ega.
        _ = Task.Run(() => RunAsync(testId, actor, mine), CancellationToken.None);

        return (Snapshot(mine), null);
    }

    private async Task RunAsync(string testId, string actor, JobState state)
    {
        try
        {
            // Fon ishi so'rov qamrovidan tashqarida — o'z DbContext'i bo'lishi SHART
            // (so'rovniki javob yuborilgach yo'q qilinadi).
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var certs = scope.ServiceProvider.GetRequiredService<TestCertificateService>();

            var (_, error) = await certs.GenerateForTestAsync(
                db, testId, actor, CancellationToken.None, onProgress: done => state.Done = done);
            state.Error = error;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sertifikat fon ishi xato bilan tugadi (test {TestId})", testId);
            state.Error = "Sertifikat yaratishda kutilmagan xatolik — qaytadan urinib ko'ring.";
        }
        finally
        {
            // TARTIB MUHIM: avval vaqt, KEYIN `Running=false`. Teskarisida `Prune()` ishni
            // `FinishedAt` hali bo'sh (0001-yil) holatida ko'rib, "ancha oldin tugagan" deb darhol
            // o'chirib yuborardi — foydalanuvchi esa xato xabarini umuman ko'rmay qolardi.
            state.FinishedAt = AppClock.Now;
            state.Running = false;
        }
    }

    /// <summary>Joriy holat — ish topilmasa <c>null</c> (chaqiruvchi bazadagi ro'yxatga tayanadi).</summary>
    public TestCertificateJobDto? Status(string testId)
    {
        Prune();
        return _jobs.TryGetValue(testId, out var state) ? Snapshot(state) : null;
    }

    /// <summary>
    /// UI SO'RAB TURADIGAN javob: holat + SHU DAQIQADA tayyor sertifikatlar — BITTA so'rovda
    /// (ro'yxat va holat alohida so'ralsa ular bir-biriga mos kelmay qolishi mumkin edi).
    ///
    /// <para>Ish topilmasa (hech qachon boshlanmagan yoki server qayta yuklangan) —
    /// <c>Running=false</c> va bazadagi mavjud sertifikatlar "hammasi tayyor" deb qaytariladi.</para>
    /// </summary>
    public async Task<TestCertificateJobDto> StatusWithItemsAsync(
        IAppDbContext db, string testId, CancellationToken ct = default)
    {
        // TARTIB MUHIM: avval HOLAT, keyin RO'YXAT.
        // Teskarisida shunday poyga bo'lardi: ro'yxat o'qildi (10 ta) → fon ishi oxirgi bo'lakni
        // saqlab tugadi → holat "tugadi" deb o'qildi. UI so'rashni to'xtatib, 12 ta o'rniga 10 tani
        // ko'rsatib qolardi. Shu tartibda esa "tugadi" holatidan KEYIN o'qilgan ro'yxat albatta to'liq.
        var job = Status(testId);
        var items = await TestCertificateService.ListForTestAsync(db, testId, ct);
        return job is null
            ? new TestCertificateJobDto(false, items.Count, items.Count,
                DocxToPdfConverter.IsAvailable, null, PdfWarning(), items)
            : job with { Items = items };
    }

    /// <summary>LibreOffice yo'qligi haqidagi YAGONA ogohlantirish matni (ikkala controller uchun).</summary>
    public static string? PdfWarning() =>
        DocxToPdfConverter.IsAvailable
            ? null
            : "Serverda PDF konvertori (LibreOffice) o'rnatilmagan — sertifikatlar Word (.docx) sifatida saqlanadi.";

    /// <summary>Yozuvlar ro'yxati keyin controller'da to'ldiriladi (bazadan o'qiladi).</summary>
    private static TestCertificateJobDto Snapshot(JobState s) =>
        new(s.Running, s.Total, s.Done, DocxToPdfConverter.IsAvailable, s.Error, PdfWarning(), []);

    /// <summary>Ancha oldin tugagan ishlarni tashlab yuboradi (lug'at cheksiz o'smasin).</summary>
    private void Prune()
    {
        foreach (var (key, s) in _jobs)
            if (!s.Running && AppClock.Now - s.FinishedAt > KeepFinished)
                _jobs.TryRemove(key, out _);
    }
}
