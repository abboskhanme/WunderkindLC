using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUH ARXIVDAN CHIQARILGANDA guruh bilan birga qaytariladigan ESKI o'quvchilarning
/// (<see cref="Student.ArchivedWithClass"/>) a'zoliklari — arxivda turgan oylar HECH QACHON
/// orqaga hisoblanmaydi (<c>ClassesController.Unarchive</c>).
///
/// <para><b>MUAMMO:</b> 2026-09-10 gacha guruhni arxivlash o'quvchini ham arxivlar, lekin
/// a'zoligini <c>active</c> qoldirardi. Arxivdagi o'quvchiga oylik yozilmaydi (accrual uni
/// o'tkazib yuboradi) — ammo guruh arxivdan chiqarilib o'quvchi qaytsa, a'zolik yana "tirik":
/// 12 soatlik <c>AccrueDue</c> butun tarixni qayta skanerlab, o'quvchi arxivda turgan HAR OYGA
/// qarz yozardi va har oy uchun ota-onaga "oylik hisoblandi" SMS'i ketardi.</para>
///
/// <para><b>QAROR — a'zolik MUZLATILGAN holatda qaytadi</b> (o'quvchi arxivlangan sanadan):
/// <list type="bullet">
///   <item>guruh arxivdan chiqarilganda a'zoliklar baribir "muzlatilganicha qoladi" (yopish/arxivlash
///     ularni muzlatgan) — eski yozuvlar ham AYNAN shu holatga keltiriladi, ikkita xatti-harakat yo'q;</item>
///   <item>o'qishni davom ettirish — ongli «Aktivlashtirish» (sana tanlanadi, qisman oy to'g'ri yoziladi,
///     eski davr tarixga ko'chadi). Avtomatik qayta boshlash oyning qolganini hisobsiz qoldirardi yoki
///     qisman oylikni ikki marta yozardi;</item>
///   <item>PULGA TEGILMAYDI: arxivlash oyining yozilgan hisobi o'z holicha qoladi (o'sha oy muzlatish
///     oyi sifatida "pullik"), keyingi oylarga hisob yo'q edi va endi ham yozilmaydi.</item>
/// </list>
/// O'quvchining BARCHA tirik <c>active</c> a'zoliklari (boshqa guruhdagilar ham) muzlatiladi:
/// u arxivda turgan davrda hech qayerda o'qimagan, accrual esa guruhning arxiv bayrog'iga qaramaydi.</para>
/// <para><c>SaveChanges</c> QILMAYDI. ⚠️ O'quvchining arxiv maydonlari TOZALANISHIDAN OLDIN
/// chaqirilsin — muzlatish sanasi <see cref="Student.ArchivedAt"/> dan olinadi.</para>
/// </summary>
public static class GroupUnarchive
{
    /// <summary>
    /// Muzlatish sanasi (sof funksiya): o'quvchi arxivlangan kun → bo'lmasa guruh arxivlangan kun →
    /// bo'lmasa bugun; aktivlashtirish sanasidan OLDIN bo'lmaydi (teskari davr yasalmasin).
    /// </summary>
    public static string FreezeDate(string? studentArchivedAt, string? groupArchivedAt, string? activatedAt, string today)
    {
        var date = IsDate(studentArchivedAt) ? studentArchivedAt![..10]
            : IsDate(groupArchivedAt) ? groupArchivedAt![..10]
            : today;
        if (IsDate(activatedAt) && string.CompareOrdinal(date, activatedAt![..10]) < 0) date = activatedAt[..10];
        return date;
    }

    /// <summary>
    /// Qaytariladigan o'quvchilarning tirik <c>active</c> a'zoliklarini o'quvchi arxivlangan sanadan
    /// muzlatadi va har biriga <c>Membership</c> audit qatorini yozadi. Qaytaradi: nechta a'zolik.
    /// </summary>
    public static async Task<int> FreezeRevivedMembershipsAsync(
        IAppDbContext db, AuditService? audit, Group group, string? groupArchivedAt,
        IReadOnlyCollection<Student> restored, CancellationToken ct = default)
    {
        if (restored.Count == 0) return 0;
        var byId = restored.ToDictionary(s => s.Id);
        var ids = byId.Keys.ToList();
        var live = await db.StudentGroups
            .Where(sg => ids.Contains(sg.StudentId) && sg.IsActive && sg.Status == "active")
            .ToListAsync(ct);
        if (live.Count == 0) return 0;

        var groupIds = live.Select(m => m.GroupId).Distinct().ToList();
        var names = await db.Classes.AsNoTracking().Where(c => groupIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var today = AppClock.Today.ToString("yyyy-MM-dd");

        foreach (var m in live)
        {
            var s = byId[m.StudentId];
            var date = FreezeDate(s.ArchivedAt, groupArchivedAt, m.ActivatedAt, today);
            m.Status = "frozen";
            m.FrozenAt = date;
            // «Aktiv muzlatish» belgisi — faqat ataylab qo'yiladi; bu yerda oddiy muzlatish.
            m.YearFreeze = false;
            audit?.Record("Membership", $"{m.GroupId}:{m.StudentId}", "update",
                $"Arxivdan qaytarildi («{group.Name}» guruhi bilan): {s.FullName} — a'zolik {date} sanasidan " +
                $"MUZLATILGAN holatda tiklandi (guruh: {names.GetValueOrDefault(m.GroupId, m.GroupId)}); arxivda " +
                $"turgan oylar hisoblanmaydi, o'qishni davom ettirish uchun «Aktivlashtirish»",
                studentId: m.StudentId);
        }
        return live.Count;
    }

    private static bool IsDate(string? iso) =>
        iso is { Length: >= 10 } && DateOnly.TryParse(iso[..10], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);
}
