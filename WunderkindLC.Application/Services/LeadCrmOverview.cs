using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «BUTUN CRM MANZARASI» — markazdagi BARCHA lidlar: qaysi kanaldan kelgan, qaysi bosqichda
/// turibdi va qanchasi haqiqatan pul to'lagan.
///
/// <para><b>Nima uchun kerak:</b> "Formalar" bo'limidagi ikkala statistika ham (lid formalari va
/// daraja testi) faqat O'Z kanalidan kelgan lidlarni sanaydi. Markazda esa qo'lda kiritilgan,
/// Instagramdan kelgan va boshqa lidlar ham bor — shu kontekstsiz sahifadagi "jami" raqami
/// "markazning hamma lidi" deb o'qilib, noto'g'ri xulosaga olib kelardi ("bizga oyiga atigi
/// 12 ta lid kelibdi").</para>
///
/// <para><b>Nega bitta joyda:</b> ikkala sahifa ham AYNAN shu funksiyani chaqiradi, ya'ni
/// "qo'lda kiritilgan" yoki "to'ladi" so'zi ikki sahifada ikki xil hisoblanib qolmaydi.
/// Kanal tasnifi — <see cref="LeadOrigins"/>, kesimning o'zi —
/// <see cref="LeadAnalytics.BuildOrigins"/> (CRM statistikasi sahifasi ham shundan).</para>
/// </summary>
public static class LeadCrmOverview
{
    /// <summary>Barcha lidlar bo'yicha jamlanma + kanallar va bosqichlar kesimi.</summary>
    public static async Task<CrmOverviewDto> BuildAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var leads = await db.Leads.AsNoTracking()
            .Select(l => new { l.Id, l.Stage, l.ConvertedStudentId, l.CreatedAt })
            .ToListAsync(ct);

        if (leads.Count == 0) return new CrmOverviewDto(0, 0, 0, 0m, [], []);

        // Lid → o'quvchi → TO'LOV zanjiri (vozvrat ayirilgan) — daraja testi va lid formalari
        // statistikasidagi bilan AYNAN bir xil.
        var outcome = await LeadOutcome.BuildAsync(db, leads.Select(l => l.Id));

        var ids = leads.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var manualIds = (await db.LeadEvents.AsNoTracking()
                .Where(e => e.Type == LeadAnalytics.TypeCreated && e.ActorUserId != null && e.ActorUserId != "")
                .Select(e => e.LeadId).Distinct().ToListAsync(ct))
            .Where(ids.Contains).ToHashSet(StringComparer.Ordinal);
        var formIds = (await db.LeadFormSubmissions.AsNoTracking()
                .Where(x => x.LeadId != "").Select(x => x.LeadId).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var testIds = (await db.LevelTestSubmissions.AsNoTracking()
                .Where(x => x.LeadId != "").Select(x => x.LeadId).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var igIds = (await db.IgConversations.AsNoTracking()
                .Where(x => x.LeadId != null).Select(x => x.LeadId!).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var adsIds = (await db.IgAdLeads.AsNoTracking()
                .Where(x => x.LeadId != "").Select(x => x.LeadId).Distinct().ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var rows = leads.Select(l => new LeadAnalytics.LeadRow(
            l.Id, l.Stage ?? "", "", l.ConvertedStudentId != null, l.CreatedAt ?? "",
            Paid: outcome.HasPaid(l.Id),
            // Tushum — faqat MUSBAT sof summa: to'liq qaytarilgan pul kanalning "daromadi" emas
            // (`LeadFormService.Funnel` bilan bir xil qoida).
            Revenue: Math.Max(0m, outcome.PaidTotal(l.Id)),
            Origin: LeadOrigins.Classify(l.Id, manualIds, formIds, testIds, igIds, adsIds))).ToList();

        // BOSQICH — lidning HOZIRGI kanban ustuni. Bosqichi yo'q (yoki ustuni o'chirilgan) lid
        // ro'yxatga kirmaydi: kanbanda ham ko'rinmaydi, sun'iy "Noma'lum bosqich" YASALMAYDI.
        var byStage = leads.Select(l => outcome.StageOf(l.Id))
            .Where(st => st.Title.Length > 0)
            .GroupBy(st => (st.Title, st.Color))
            .Select(g => new LeadStageCountDto(g.Key.Title, g.Key.Color, g.Count()))
            .OrderByDescending(x => x.Leads).ThenBy(x => x.Stage)
            .ToList();

        return new CrmOverviewDto(
            Leads: rows.Count,
            Converted: rows.Count(l => l.Converted),
            Paid: rows.Count(l => l.Paid),
            Revenue: rows.Sum(l => l.Revenue),
            Origins: LeadAnalytics.BuildOrigins(rows),
            ByStage: byStage);
    }
}
