using IntellectCRM.Application.Services;
using IntellectCRM.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// GURUH SIG'IMI (<see cref="Group.Capacity"/>) — "nechta o'rin band".
///
/// <para>Talab: sig'imni FAQAT o'rin band qilgan a'zoliklar to'ldiradi — <c>active</c> va
/// <c>trial</c> (sinovdagi ham darsga kelib o'tiradi). <b>MUZLATILGAN o'quvchi o'rin
/// egallamaydi</b>, guruhdan chiqib ketgan ham.</para>
///
/// <para>Ilgari sig'im "guruhdagi hamma a'zo" (<c>IsActive</c>) bo'yicha sanalardi: muzlatilganlar
/// ham kirib, haqiqatda bo'sh guruh "to'lgan" ko'rinar va yangi o'quvchi qo'shib bo'lmasdi.</para>
///
/// <para>Yagona ta'rif — <see cref="MembershipLifecycle.OccupiesSeat(string?, bool)"/> va uning
/// SQL ko'rinishi <see cref="MembershipLifecycle.OccupiesSeatExpr"/>. Foydalanuvchilari:
/// <c>ClassesController.AddMember</c> · <c>ClassesController.TransferMember</c> ·
/// <c>ClassesController.Fill</c> · <c>LeadsController.Convert</c>.</para>
/// </summary>
public class GroupCapacityTests
{
    // ---------- sof funksiya ----------

    [Theory]
    [InlineData("active", true, true)]
    // Sinov ham O'RIN OLADI — u darsga keladi, faqat oyligi hisoblanmaydi (pul boshqa savol).
    [InlineData("trial", true, true)]
    // Muzlatilgan — a'zo bo'lib qoladi, lekin o'rinni BO'SHATADI.
    [InlineData("frozen", true, false)]
    // Chiqib ketgan (IsActive=false) — holatidan qat'i nazar o'rin olmaydi.
    [InlineData("active", false, false)]
    [InlineData("trial", false, false)]
    [InlineData("frozen", false, false)]
    // Noma'lum/bo'sh holat "active" deb qaraladi — MembershipLifecycle.Tally bilan bir xil.
    [InlineData("", true, true)]
    [InlineData(null, true, true)]
    public void OccupiesSeat_faqat_active_va_trial(string? status, bool isActive, bool expected) =>
        Assert.Equal(expected, MembershipLifecycle.OccupiesSeat(status, isActive));

    /// <summary>«Aktiv muzlatish» ham oddiy muzlatish — <c>Status</c> baribir "frozen"
    /// (<c>.claude/rules/year-freeze.md</c> §1), demak o'rin ham bo'shaydi.</summary>
    [Fact]
    public void OccupiesSeat_YearFreeze_ham_orin_bushatadi() =>
        Assert.False(MembershipLifecycle.OccupiesSeat(
            new StudentGroup { Status = "frozen", IsActive = true, YearFreeze = true }));

    // ---------- SQL ko'rinishi sof funksiya bilan BIR XIL ----------

    /// <summary>
    /// <see cref="MembershipLifecycle.OccupiesSeatExpr"/> bazada AYNAN sof funksiyaday ishlashi
    /// shart — ikkisi ayrilib ketsa server bir sonni, ekran boshqasini ko'rsatardi.
    /// </summary>
    [Fact]
    public async Task OccupiesSeatExpr_bazada_sof_funksiya_bilan_bir_xil()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;

        var rows = new[]
        {
            new StudentGroup { GroupId = "g1", StudentId = "s1", Status = "active", IsActive = true },
            new StudentGroup { GroupId = "g1", StudentId = "s2", Status = "trial",  IsActive = true },
            new StudentGroup { GroupId = "g1", StudentId = "s3", Status = "frozen", IsActive = true },
            new StudentGroup { GroupId = "g1", StudentId = "s4", Status = "frozen", IsActive = true, YearFreeze = true },
            new StudentGroup { GroupId = "g1", StudentId = "s5", Status = "active", IsActive = false, LeftAt = "2026-01-01" },
            // Boshqa guruh — sanoqqa aralashmasin.
            new StudentGroup { GroupId = "g2", StudentId = "s6", Status = "active", IsActive = true },
        };
        ctx.StudentGroups.AddRange(rows);
        await ctx.SaveChangesAsync();

        var fromDb = await ctx.StudentGroups
            .Where(sg => sg.GroupId == "g1")
            .CountAsync(MembershipLifecycle.OccupiesSeatExpr);

        var inMemory = rows.Count(r => r.GroupId == "g1" && MembershipLifecycle.OccupiesSeat(r));

        // 5 ta a'zolikdan faqat active + trial: 2 ta (2 muzlatilgan + 1 ketgan sanalmaydi).
        Assert.Equal(2, fromDb);
        Assert.Equal(inMemory, fromDb);
    }

    /// <summary>
    /// ⚠️ ASOSIY REGRESSIYA: sig'imi 10 bo'lgan guruhda 8 o'quvchi muzlatilgan bo'lsa, guruh
    /// TO'LGAN EMAS — eski qoida (<c>IsActive</c>) uni "to'lgan" deb hisoblardi.
    /// </summary>
    [Fact]
    public async Task Muzlatilganlar_guruhni_TOLGAN_qilib_qoymaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var group = new Group { Name = "A guruh", Capacity = 10 };
        ctx.Classes.Add(group);
        for (var i = 0; i < 8; i++)
            ctx.StudentGroups.Add(new StudentGroup
            {
                GroupId = group.Id, StudentId = $"muz{i}", Status = "frozen", IsActive = true,
            });
        for (var i = 0; i < 2; i++)
            ctx.StudentGroups.Add(new StudentGroup
            {
                GroupId = group.Id, StudentId = $"faol{i}", Status = "active", IsActive = true,
            });
        await ctx.SaveChangesAsync();

        var enrolled = await ctx.StudentGroups
            .Where(sg => sg.GroupId == group.Id)
            .CountAsync(MembershipLifecycle.OccupiesSeatExpr);

        Assert.Equal(2, enrolled);
        Assert.False(enrolled >= group.Capacity);          // guruh TO'LMAGAN
        Assert.Equal(8, group.Capacity - enrolled);        // 8 ta bo'sh o'rin
    }
}
