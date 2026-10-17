using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

public static partial class WorkTaskFlow
{
    /// <summary>Yangi doskaning standart ustunlari. Birinchisi va yopiluvchisi TIZIM ustuni —
    /// o'chirilmaydi (topshiriqning "uyi" va "bajarildi" nuqtasi doim qolsin).
    /// <para>⚠️ «Tekshiruvda» 2026-10 dan EKILMAYDI (CRM Phase 5): tekshiruv endi TASDIQLASH bosqichi
    /// (<see cref="WorkTaskApproval"/>), ustun emas. Mavjud doskalar tegilmaydi.</para>
    /// <para>YAGONA nusxa: panel seed'i (<c>WorkTasksController.EnsureSeedAsync</c>) ham, lid avto-topshirig'i
    /// (<see cref="LeadAutoTask"/>) ham shuni ishlatadi.</para></summary>
    public static List<WorkTaskColumn> DefaultColumns(string boardId) =>
    [
        new() { BoardId = boardId, Title = "Rejada",     Color = "slate",   Order = 0, IsSystem = true },
        new() { BoardId = boardId, Title = "Jarayonda",  Color = "blue",    Order = 1 },
        new() { BoardId = boardId, Title = "Bajarildi",  Color = "emerald", Order = 2, IsDone = true, IsSystem = true },
    ];

    /// <summary>Birorta doska bo'lmasa — "Umumiy" doskasini standart ustunlar bilan QO'SHADI (saqlamaydi —
    /// SaveChanges chaqiruvchida). Doska bo'lsa <c>null</c>.</summary>
    public static async Task<WorkTaskBoard?> AddDefaultBoardIfNoneAsync(
        IAppDbContext db, string createdById, CancellationToken ct = default)
    {
        if (await db.WorkTaskBoards.AnyAsync(ct)) return null;
        var board = new WorkTaskBoard { Title = "Umumiy", Color = "violet", Order = 0, CreatedById = createdById };
        db.WorkTaskBoards.Add(board);
        db.WorkTaskColumns.AddRange(DefaultColumns(board.Id));
        return board;
    }
}
