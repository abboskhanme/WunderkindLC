using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Kam o'zgaradigan ma'lumotlar (portal meta, fan/o'qituvchi nomlari) uchun qisqa-TTL kesh.
/// Portal endpointlari ularni deyarli har so'rovda o'qiydi — kesh DB yukini sezilarli kamaytiradi.
///
/// <para><b>MUHIM:</b> faqat o'zgarmas DTO/lug'atlar keshlanadi (EF entity EMAS) — shuning uchun
/// so'rovlar/oqimlar orasida bo'lishish xavfsiz. Yuklash paytida alohida scope'da DbContext
/// ochiladi (singleton xizmat scoped DbContext'ni ushlab qolmasligi uchun).</para>
/// </summary>
public class ReferenceCache(IMemoryCache cache, IServiceScopeFactory scopeFactory)
{
    private async Task<T> GetAsync<T>(string key, TimeSpan ttl, Func<IAppDbContext, Task<T>> load) =>
        (await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            return await load(db);
        }))!;

    /// <summary>Portal meta (sintetik davr, davomat sabablari + joriy davr/hafta). Dars jadvali/dars
    /// vaqtlari olib tashlandi — LessonTimes bo'sh, CurrentQuarter/Week = 1. TTL 30s.</summary>
    public Task<PortalMetaDto> MetaAsync() =>
        GetAsync("ref:meta", TimeSpan.FromSeconds(30), async db =>
        {
            var reasons = await db.AbsenceReasons.OrderBy(r => r.Name)
                .Select(r => new AbsenceReasonDto(r.Id, r.Name, r.Short, r.IsLate)).ToListAsync();
            var quarters = await TuitionService.SyntheticPeriodsAsync(db);
            return new PortalMetaDto(new List<LessonTimeDto>(), reasons, quarters, 1, 1);
        });

    // Quyida faqat Id/Name proyeksiya qilinadi (AsNoTracking): ilgari ToDictionaryAsync BUTUN
    // entityni tracking bilan materializatsiya qilardi — lug'atga esa ikkita ustun yetadi.

    /// <summary>Fan id → nomi. TTL 2 daqiqa.</summary>
    public Task<Dictionary<string, string>> SubjectNamesAsync() =>
        GetAsync("ref:subjectNames", TimeSpan.FromMinutes(2),
            db => db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name));

    /// <summary>O'qituvchi id → FISH. TTL 2 daqiqa.</summary>
    public Task<Dictionary<string, string>> TeacherNamesAsync() =>
        GetAsync("ref:teacherNames", TimeSpan.FromMinutes(2),
            db => db.Teachers.AsNoTracking().Select(t => new { t.Id, Name = t.FullName })
                .ToDictionaryAsync(x => x.Id, x => x.Name));
}
