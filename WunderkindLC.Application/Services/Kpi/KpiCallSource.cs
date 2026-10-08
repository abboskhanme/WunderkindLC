using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Kpi;

/// <summary>
/// KPI uchun QO'NG'IROQLAR MANBAI — Local Call (CTI) yozuvlari (<see cref="CtiCallRecord"/>).
///
/// <para><b>Nega alohida fayl:</b> eski bulutli telefoniya olib tashlangach (2026-10-08) KPI'ning
/// to'rt joyi (kunlik «teginish» va «muloqot vaqti» normasi, cheklistning ikki avto-bandi,
/// haftalik sifat nazorati namunasi) BIR XIL savolga javob beradi: "bu qo'ng'iroq KIMNIKI va
/// u nimani bildiradi". Javob shu yerda bitta — har joy o'z filtrini yozsa, bittasi
/// o'zgarganda qolganlari jimgina boshqa sonni ko'rsatib qolardi (`.claude/rules/kpi.md` §11.1).</para>
///
/// <para><b>Xodim ↔ qo'ng'iroq:</b> CTI yozuvida xodim id'si YO'Q — u agent (telefon) orqali
/// aniqlanadi: <see cref="CtiAgent.StaffUserId"/>. Agent hech kimga biriktirilmagan bo'lsa,
/// qo'ng'iroq hech bir xodimning "o'zinikiga" kirmaydi (faqat
/// <see cref="ChecklistAutoCheck.CallsAnswerSpeed"/> uni "egasiz" sifatida oladi).</para>
/// </summary>
public static class KpiCallSource
{
    /// <summary>Isbot sahifasi — Local Call bo'limi (`App.tsx` dagi mavjud marshrut).</summary>
    public const string Link = "/admin/calls/local";

    /* ---------- CtiCallRecord.Direction qiymatlari ---------- */

    /// <summary>Kiruvchi, KO'TARILGAN qo'ng'iroq.</summary>
    public const string Incoming = "incoming";
    /// <summary>Chiquvchi qo'ng'iroq (javob berilmagan bo'lsa ham — bu URINISH).</summary>
    public const string Outgoing = "outgoing";
    /// <summary>Javobsiz KIRUVCHI qo'ng'iroq.</summary>
    public const string Missed = "missed";

    /// <summary>Sifat nazorati namunasining minimal davomiyligi (soniya): undan qisqasini
    /// tinglab baholab bo'lmaydi.</summary>
    public const int SampleMinSeconds = 60;

    /// <summary>
    /// "Teginish"mi: operator suhbatdosh bilan HAQIQATAN gaplashgan (kiruvchi) yoki bog'lanishga
    /// URINGAN (chiquvchi, javobsiz bo'lsa ham) qo'ng'iroq.
    /// </summary>
    /// <remarks>⚠️ Javobsiz KIRUVCHI (<see cref="Missed"/>) teginish EMAS: unda operator hech kimga
    /// tegmagan — aksincha, mijoz unga yetib bora olmagan. Uni sanash bandligi past kunda
    /// normani "bajarilgan" qilib ko'rsatardi.</remarks>
    public static bool IsTouch(string? direction) => direction is Incoming or Outgoing;

    /// <summary>
    /// KPI namunasidagi yo'nalish — <see cref="KpiCallSampleDto.Direction"/> shartnomasi
    /// (<c>"inbound"</c>/<c>"outbound"</c>, klient `callDirectionLabel` shu qiymatlarni kutadi).
    /// </summary>
    /// <remarks>Javobsiz kiruvchi ham <c>"inbound"</c> — u ham KIRUVCHI qo'ng'iroq.</remarks>
    public static string SampleDirection(string? direction) =>
        direction == Outgoing ? "outbound" : "inbound";

    /// <summary>Xodimga biriktirilgan agent (telefon) id'lari.</summary>
    public static async Task<List<string>> AgentIdsOfAsync(
        IAppDbContext db, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return [];
        return await db.CtiAgents.AsNoTracking()
            .Where(a => a.StaffUserId == userId)
            .Select(a => a.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Kunlik «teginish» soni va «muloqot vaqti» (soniya) — xodimning agent(lar)i bo'yicha,
    /// <c>[from, to)</c> oralig'ida.
    /// </summary>
    /// <remarks>Muloqot vaqti BARCHA yozuvlar bo'yicha yig'iladi: javobsiz qo'ng'iroqning
    /// <c>DurationSec</c> i baribir 0, ya'ni u summani o'zgartirmaydi.</remarks>
    public static async Task<(int Touches, int TalkSeconds)> DailyTotalsAsync(
        IAppDbContext db, string userId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var agentIds = await AgentIdsOfAsync(db, userId, ct);
        if (agentIds.Count == 0) return (0, 0);

        var rows = await db.CtiCallRecords.AsNoTracking()
            .Where(c => agentIds.Contains(c.AgentId) && c.StartedAt >= from && c.StartedAt < to)
            .Select(c => new { c.Direction, c.DurationSec })
            .ToListAsync(ct);

        return (rows.Count(r => IsTouch(r.Direction)), rows.Sum(r => Math.Max(0, r.DurationSec)));
    }

    /// <summary>
    /// Haftalik sifat nazorati uchun NOMZODLAR: xodimning agent(lar)i, <paramref name="since"/>
    /// dan keyin, yozuvi SERVERDA bor va <see cref="SampleMinSeconds"/> dan uzun.
    /// </summary>
    /// <remarks>
    /// ⚠️ Yozuv FAYLI manzili qaytmaydi (faqat <c>HasRecording</c>): tinglash Local Call'ning
    /// avtorizatsiyalangan audio endpointi orqali (<c>GET /api/cti/calls/{id}/audio</c>,
    /// `.claude/rules/uploads-security.md` printsipi). Tasodifiy tanlov chaqiruvchida.
    /// </remarks>
    public static async Task<List<KpiCallSampleDto>> SampleCandidatesAsync(
        IAppDbContext db, string userId, DateTime since, CancellationToken ct = default)
    {
        var agentIds = await AgentIdsOfAsync(db, userId, ct);
        if (agentIds.Count == 0) return [];

        var rows = await db.CtiCallRecords.AsNoTracking()
            .Where(c => agentIds.Contains(c.AgentId) && c.StartedAt >= since
                        && c.DurationSec > SampleMinSeconds
                        && c.AudioUploaded && c.AudioPath != "")
            .Select(c => new
            {
                c.Id, c.RemoteNumber, c.Direction, c.StartedAt, c.DurationSec,
                c.Transcript, c.AiAnalysis,
            })
            .ToListAsync(ct);

        return rows.Select(c => new KpiCallSampleDto(
                c.Id, c.RemoteNumber, SampleDirection(c.Direction),
                c.StartedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), c.DurationSec,
                HasRecording: true,
                string.IsNullOrWhiteSpace(c.Transcript) ? null : c.Transcript,
                string.IsNullOrWhiteSpace(c.AiAnalysis) ? null : c.AiAnalysis))
            .ToList();
    }
}
