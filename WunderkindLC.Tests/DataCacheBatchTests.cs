using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// <see cref="DataCache"/> ning "TO'PLASH REJIMI" (<see cref="DataCache.BeginBatch"/>).
///
/// <para>Nima uchun kerak: ommaviy muzlatish/aktivlashtirish har a'zolikni ALOHIDA
/// <c>SaveChanges</c> bilan saqlaydi, ya'ni <c>CacheInvalidationInterceptor</c> har iteratsiyada
/// <see cref="DataCache.Bump"/> chaqiradi — 500 o'quvchida ~1500 versiya oshirilishi. Og'ir keshlar
/// (bosh sahifa, reyting, kurslar analitikasi) butun jarayon davomida qayta-qayta eskirar va bir
/// necha marta boshqatdan hisoblanardi. To'plash rejimi bularni BITTA bump'ga yig'adi.</para>
///
/// <para>Bu testlar BAZASIZ — <see cref="DataCache"/> ning versiya hisobi <c>IMemoryCache</c> va
/// <c>IServiceScopeFactory</c> ga umuman tegmaydi (ular faqat <c>GetOrCreateAsync</c> da kerak),
/// shuning uchun konstruktorga <c>null!</c> berish yetarli.</para>
/// </summary>
public class DataCacheBatchTests
{
    private static DataCache New() => new(null!, null!);

    [Fact]
    public void Toʻplam_ICHIDA_versiya_DARHOL_oshmaydi()
    {
        var cache = New();
        Assert.Equal(0, cache.Version("Student"));

        using (cache.BeginBatch())
        {
            cache.Bump(["Student"]);
            cache.Bump(["Student"]);
            // Blok ichida kesh ATAYIN eski qiymatida qoladi — bu kelishilgan murosa.
            Assert.Equal(0, cache.Version("Student"));
        }
    }

    [Fact]
    public void Dispose_dan_keyin_har_tur_AYNAN_BIR_marta_oshadi()
    {
        var cache = New();

        using (cache.BeginBatch())
        {
            // Bitta tur 5 marta, ikkinchisi 1 marta — 500 a'zolikli ommaviy amalning modeli.
            for (var i = 0; i < 5; i++) cache.Bump(["StudentGroup", "MonthlyCharge"]);
            cache.Bump(["AuditLog"]);
        }

        // Asosiy talab: versiya nechta bump bo'lganidan QAT'I NAZAR aynan +1 (kalit bir marta o'zgaradi).
        Assert.Equal(1, cache.Version("StudentGroup"));
        Assert.Equal(1, cache.Version("MonthlyCharge"));
        Assert.Equal(1, cache.Version("AuditLog"));
        // Umuman bump qilinmagan tur tegilmaydi.
        Assert.Equal(0, cache.Version("Group"));
    }

    [Fact]
    public void Toʻplamdan_TASHQARIDA_Bump_avvalgidek_darhol_ishlaydi()
    {
        var cache = New();
        cache.Bump(["Student"]);
        cache.Bump(["Student"]);
        // Rejim yoqilmagan bo'lsa xatti-harakat O'ZGARMAYDI — har chaqiruv +1.
        Assert.Equal(2, cache.Version("Student"));
    }

    [Fact]
    public void ICHMA_ICH_toʻplam_ichkisi_bump_qilmaydi_tashqisi_qiladi()
    {
        var cache = New();

        using (cache.BeginBatch())
        {
            using (cache.BeginBatch())
            {
                cache.Bump(["Student"]);
            }
            // Ichki blok yopildi, lekin turlar TASHQI to'plamga o'tdi — bump hali yo'q.
            Assert.Equal(0, cache.Version("Student"));

            cache.Bump(["Student"]);
        }

        // Haqiqiy bump faqat ENG TASHQI blokda va faqat BIR marta.
        Assert.Equal(1, cache.Version("Student"));
    }

    [Fact]
    public void ICHKI_blokdan_keyin_toʻplash_rejimi_TIKLANADI()
    {
        var cache = New();

        using (cache.BeginBatch())
        {
            using (cache.BeginBatch()) { }
            // Ichki Dispose tashqi to'plamni o'chirib qo'ymasligi kerak, aks holda undan keyingi
            // har bir Bump darhol versiyani oshirib, to'plashning ma'nosi qolmasdi.
            cache.Bump(["Student"]);
            Assert.Equal(0, cache.Version("Student"));
        }

        Assert.Equal(1, cache.Version("Student"));
    }

    [Fact]
    public void ISTISNO_boʻlsa_ham_bump_YOʻQOLMAYDI()
    {
        var cache = New();

        // `using` tufayli Dispose baribir ishlaydi: ommaviy amalning yarmida xato chiqsa ham
        // allaqachon saqlangan o'zgarishlar keshdan tushib qolmasligi kerak.
        try
        {
            using (cache.BeginBatch())
            {
                cache.Bump(["Student"]);
                throw new InvalidOperationException("bulk xatosi");
            }
        }
        catch (InvalidOperationException) { /* kutilgan */ }

        Assert.Equal(1, cache.Version("Student"));
    }

    [Fact]
    public async Task Toʻplam_PARALLEL_oqimga_TAʼSIR_QILMAYDI()
    {
        var cache = New();
        var batchStarted = new TaskCompletionSource();
        var otherDone = new TaskCompletionSource();

        // DataCache — SINGLETON. Agar to'plash rejimi AsyncLocal bo'lmasa, bitta so'rovning
        // to'plami boshqa foydalanuvchilarning bump'larini ham yutib yuborardi.
        var bulk = Task.Run(async () =>
        {
            using (cache.BeginBatch())
            {
                cache.Bump(["Student"]);
                batchStarted.SetResult();
                await otherDone.Task;
            }
        });

        await batchStarted.Task;
        await Task.Run(() => cache.Bump(["Teacher"]));
        // Boshqa oqimning bump'i DARHOL ishladi — to'plamga tushib qolmadi.
        Assert.Equal(1, cache.Version("Teacher"));

        otherDone.SetResult();
        await bulk;
        Assert.Equal(1, cache.Version("Student"));
    }
}
