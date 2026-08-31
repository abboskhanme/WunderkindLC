using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using IntellectCRM.Application.Abstractions;

namespace IntellectCRM.Application.Services;

/// <summary>
/// "Ma'lumot o'zgarganda avtomatik yangilanadigan" kesh qatlami. Og'ir (butun jadvalni yuklaydigan)
/// hisob-kitoblar natijasini keshlaydi va bog'liq jadvallardan biri o'zgarganda keshni AVTOMATIK
/// eskirtiradi — TTL tugashini kutmasdan.
///
/// <para><b>Ishlash printsipi (versiyali kalit):</b> har bir entity turi ("guruh") uchun
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> da butun son "versiya" saqlanadi. Kesh yozuvining
/// TO'LIQ kaliti = mantiqiy kalit + bog'liq turlar versiyalari. Bog'liq jadvalga yozuv qo'shilsa/
/// o'zgarsa/o'chsa (interceptor orqali) o'sha turning versiyasi +1 bo'ladi → to'liq kalit o'zgaradi →
/// <see cref="IMemoryCache"/> uchun bu YANGI kalit, ya'ni natija qayta hisoblanadi. Eski (endi hech kim
/// so'ramaydigan) yozuv o'z TTL'i bilan xotiradan o'zi chiqib ketadi. Bu yondashuv keshni "invalidatsiya"
/// qilish (aniq kalitlarni o'chirish) muammosini butunlay chetlab o'tadi — biror joyda o'zgargan
/// versiyaning ta'sirini keshning O'ZI hal qiladi.</para>
///
/// <para><b>Nima uchun scope:</b> bu xizmat Singleton (versiyalar butun ilova bo'ylab yagona bo'lishi
/// shart). Scoped <see cref="IAppDbContext"/> ni singleton ushlab qololmaydi (captive dependency), shuning
/// uchun yuklash paytida <see cref="IServiceScopeFactory"/> orqali vaqtinchalik scope ochiladi va DbContext
/// o'sha yerdan olinadi. Keshga faqat o'zgarmas natija (DTO/record) saqlanadi — EF entity EMAS.</para>
///
/// <para>TTL bu yerda faqat <b>xavfsizlik tarmog'i</b>: interceptor sezmaydigan o'zgarishlar (masalan
/// DB'ga to'g'ridan-to'g'ri SQL, tashqi jarayon) uchun kesh baribir vaqti-vaqti bilan yangilanadi.
/// Asosiy (tezkor) yangilanish versiya orqali bo'ladi.</para>
/// </summary>
public sealed class DataCache(IMemoryCache cache, IServiceScopeFactory scopeFactory)
{
    // Entity turi nomi (masalan nameof(JournalEntry)) → joriy versiya. Yozuv o'zgarganda +1.
    private readonly ConcurrentDictionary<string, long> _versions = new();

    /// <summary>OMMAVIY amal uchun "to'plash rejimi" (qarang: <see cref="BeginBatch"/>).
    /// <para>⚠️ <b>AsyncLocal bo'lishi SHART.</b> <see cref="DataCache"/> — SINGLETON: oddiy maydon
    /// bo'lsa bitta so'rovning to'plash rejimi butun ilovani, ya'ni PARALLEL ishlayotgan boshqa
    /// foydalanuvchilarning bump'larini ham yutib yuborardi. AsyncLocal esa qiymatni faqat SHU
    /// so'rovning asinxron oqimiga bog'laydi.</para></summary>
    private static readonly AsyncLocal<HashSet<string>?> _batch = new();

    /// <summary>Guruh (entity turi) joriy versiyasi. Hali ko'rilmagan bo'lsa 0 dan boshlanadi.</summary>
    public long Version(string group) => _versions.GetOrAdd(group, 0);

    /// <summary>Berilgan guruhlarning har birining versiyasini +1 qiladi — shu turga bog'liq barcha
    /// kesh yozuvlarining to'liq kaliti o'zgaradi va keyingi so'rovda qayta hisoblanadi.
    /// <para>To'plash rejimi (<see cref="BeginBatch"/>) yoqilgan bo'lsa versiya DARHOL oshmaydi —
    /// turlar to'planadi va <c>Dispose</c> da BIR MARTA qo'llanadi.</para></summary>
    public void Bump(IEnumerable<string> groups)
    {
        var batch = _batch.Value;
        if (batch is not null)
        {
            foreach (var g in groups) batch.Add(g);
            return;
        }
        BumpNow(groups);
    }

    private void BumpNow(IEnumerable<string> groups)
    {
        foreach (var g in groups)
            _versions.AddOrUpdate(g, 1, (_, v) => v + 1);
    }

    /// <summary>
    /// OMMAVIY amal uchun to'plash rejimi: blok ichidagi barcha <see cref="Bump"/> chaqiruvlari
    /// TO'PLANADI va <c>Dispose</c> da har bir tur uchun BIR MARTA qo'llanadi.
    ///
    /// <para><b>Nega kerak:</b> ommaviy muzlatish/aktivlashtirish har a'zolikni ALOHIDA
    /// <c>SaveChanges</c> bilan saqlaydi (bittasi xato bersa qolganlari bajarilsin), ya'ni 500 ta
    /// o'quvchida <c>CacheInvalidationInterceptor</c> ~1500 marta bump qiladi. Natijada bosh sahifa,
    /// reyting, ball, kurslar analitikasi kabi OG'IR keshlar butun jarayon davomida qayta-qayta
    /// eskiradi va bir necha marta boshqatdan hisoblanadi — hisoblangani esa keyingi iteratsiyada
    /// darhol yana eskiradi.</para>
    ///
    /// <para>⚠️ <b>Kelishilgan murosa:</b> to'plash davom etayotgan bir necha soniya ichida parallel
    /// foydalanuvchilar keshning ESKI qiymatini ko'radi. Bu ATAYIN va hozirgidan yaxshiroq —
    /// hozir ular baribir eskirgan natijani, ustiga har safar qaytadan hisoblab olishadi.</para>
    ///
    /// <para>ICHMA-ICH (nested) qo'llab-quvvatlanadi: ichki blok <c>Dispose</c> da turlarni TASHQI
    /// to'plamga qo'shadi (bump qilmaydi), haqiqiy bump esa eng tashqi blokda bo'ladi. Istisno
    /// bo'lganda ham <c>using</c> tufayli <c>Dispose</c> ishlaydi — bump YO'QOLMAYDI.</para>
    /// </summary>
    public IDisposable BeginBatch() => new BatchScope(this);

    private sealed class BatchScope : IDisposable
    {
        private readonly DataCache _owner;
        private readonly HashSet<string>? _parent;   // ichma-ich blokda — tashqi to'plam
        private readonly HashSet<string> _own;
        private bool _disposed;

        public BatchScope(DataCache owner)
        {
            _owner = owner;
            _parent = _batch.Value;
            _own = new HashSet<string>(StringComparer.Ordinal);
            _batch.Value = _own;
        }

        public void Dispose()
        {
            if (_disposed) return;   // ikki marta Dispose qilinsa turlar ikki marta qo'llanmasin
            _disposed = true;
            _batch.Value = _parent;  // oldingi (tashqi yoki yo'q) holatni TIKLAYMIZ
            if (_own.Count == 0) return;
            // Tashqi to'plam bo'lsa — unga QO'SHAMIZ (bump eng tashqi blokda bir marta bo'ladi).
            if (_parent is not null)
            {
                foreach (var g in _own) _parent.Add(g);
                return;
            }
            _owner.BumpNow(_own);
        }
    }

    /// <summary>
    /// Keshdan oladi yoki (yo'q bo'lsa) <paramref name="load"/> orqali hisoblab keshga qo'yadi.
    /// To'liq kalit = <paramref name="key"/> + bog'liq turlar (<paramref name="dependsOn"/>) versiyalari;
    /// shu turlardan biri o'zgarsa kalit o'zgaradi va natija qayta hisoblanadi.
    /// </summary>
    /// <param name="key">Mantiqiy kalit (masalan "rating:school" yoki "dashboard:2026-07-02").</param>
    /// <param name="dependsOn">Natija bog'liq entity turlari nomlari (nameof(...)).</param>
    /// <param name="ttl">Xavfsizlik tarmog'i muddati (versiyadan mustaqil ravishda ham eskiradi).</param>
    /// <param name="load">Og'ir hisob-kitob — ajratilgan scope'dagi DbContext beriladi.</param>
    public async Task<T> GetOrCreateAsync<T>(
        string key, string[] dependsOn, TimeSpan ttl, Func<IAppDbContext, Task<T>> load)
    {
        var fullKey = key + ":" + string.Join(".", dependsOn.Select(Version));
        return (await cache.GetOrCreateAsync(fullKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            // Singleton xizmat scoped DbContext'ni ushlab qolmasligi uchun alohida scope (ReferenceCache uslubi).
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            return await load(db);
        }))!;
    }
}
