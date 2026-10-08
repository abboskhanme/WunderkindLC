using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// O'quvchi BALANSINI QULF ICHIDA bazadan qayta o'qish — YAGONA yo'l.
///
/// <para>Pul amallari (to'lov, vozvrat, fon accrual, arxivlash, guruhdan chiqarish) o'quvchini
/// qulfdan OLDIN yuklaydi, keyin <c>PaymentIdempotency.LockAsync("student:…")</c> ichida balansni
/// bazadan qayta o'qiydi — shu orada boshqa so'rov saqlagan pul harakati eski qiymat ustidan
/// yozilib yo'qolmasin.</para>
///
/// <para>⚠️ <b>NEGA faqat <c>s.Balance = fresh</c> YETMAYDI:</b> o'quvchi allaqachon KUZATILAYOTGAN
/// (tracked) bo'lsa EF uning ASL qiymatini (snapshot) birinchi yuklangan paytdagidek eslab turadi.
/// Yangi qiymat + amal ta'siri tasodifan o'sha eski snapshot'ga TENG chiqsa (masalan yuklanganda
/// 100, parallel vozvrat 50 ga tushirdi, endi +50 to'lov → yana 100) EF "o'zgarish yo'q" deb
/// <b>UPDATE yubormaydi</b> — bazada 50 qoladi va to'lov jimgina yo'qoladi. Shuning uchun asl
/// qiymat ham yangi qiymatga tenglashtiriladi: endi EF ta'sirni AYNAN bazadagi qiymatga nisbatan
/// ko'radi.</para>
///
/// <para>⚠️ Qulf OLMAYDI — chaqiruvchi allaqachon qulf ichida bo'lishi kerak
/// (<c>PaymentIdempotency</c> qulflari qayta kirib bo'lmaydigan — ikki marta olinsa o'zini o'zi kutib qoladi).</para>
/// </summary>
public static class StudentBalanceSync
{
    /// <summary>Balansni bazadan qayta o'qiydi va kuzatilayotgan nusxaning asl qiymatini ham shunga
    /// tenglashtiradi. Qaytaradi: bazadagi joriy balans. SaveChanges QILMAYDI.</summary>
    public static async Task<decimal> ReloadAsync(IAppDbContext db, Student s)
    {
        var fresh = await db.Students.AsNoTracking()
            .Where(x => x.Id == s.Id).Select(x => x.Balance).FirstOrDefaultAsync();
        s.Balance = fresh;
        var entry = db.Students.Entry(s);
        if (entry.State is EntityState.Unchanged or EntityState.Modified)
            entry.Property(x => x.Balance).OriginalValue = fresh;
        return fresh;
    }
}
