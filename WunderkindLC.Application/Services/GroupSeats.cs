using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUH SIG'IMI — o'quvchi YARATISH va Excel IMPORTI yo'llari uchun (<c>ClassesController.AddMember</c>
/// bilan bir xil qoida: o'rinni faqat tirik active/trial a'zolik egallaydi —
/// <see cref="MembershipLifecycle.OccupiesSeatExpr"/>, muzlatilgani EGALLAMAYDI).
///
/// <para>Ilgari bu ikki yo'l a'zolikni sig'im tekshiruvisiz yaratardi — "Guruh to'lgan" himoyasi
/// faqat guruh sahifasidan qo'shishda ishlardi, o'quvchi formasi va import esa guruhni to'ldirib
/// yuborardi.</para>
///
/// <para>⚠️ ARXIVDAGI o'quvchi o'rin EGALLAMAYDI (arxiv = ketdi). Yangi arxivlashda a'zolik baribir
/// yopiladi, lekin ESKI arxivdagilarda a'zolik hamon tirik — ular sanalmaydi. Ta'rif YAGONA —
/// <see cref="MembershipLifecycle.SeatHolders"/> (AddMember, o'tkazish, lid aylantirish ham shu).</para>
/// </summary>
public static class GroupSeats
{
    /// <summary>Guruhda o'rin band qilgan a'zoliklar soni (arxivdagi o'quvchilarsiz).</summary>
    public static Task<int> OccupiedAsync(IAppDbContext db, string groupId, CancellationToken ct = default) =>
        MembershipLifecycle.SeatHolders(db.StudentGroups.Where(sg => sg.GroupId == groupId), db.Students)
            .CountAsync(ct);

    /// <summary>Yana bitta o'rin bormi (<c>Capacity = 0</c> — cheklanmagan). Sof funksiya.</summary>
    public static bool HasRoom(Group g, int occupied) => g.Capacity <= 0 || occupied < g.Capacity;

    /// <summary>Shu holatdagi yangi a'zolik o'rin egallaydimi (muzlatilgan — yo'q). Sof funksiya.</summary>
    public static bool TakesSeat(string status) => MembershipLifecycle.OccupiesSeat(status, true);

    /// <summary>Foydalanuvchiga ko'rsatiladigan matn (AddMember bilan bir xil boshlanadi).</summary>
    public static string FullMessage(Group g) => $"Guruh to'lgan ({g.Capacity} o'rin)";
}
