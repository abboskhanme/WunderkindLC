using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Task approval (CRM Phase 5, <c>WorkTaskApproval</c> + <c>WorkTaskFlow.Approval.cs</c>,
/// <c>.claude/rules/topshiriqlar.md</c> §8): default requirement, approve gate, return, reopen clears the
/// approval, the bot chat gate and "the repeat copy is still spawned only once".
/// </summary>
public class WorkTaskApprovalTests
{
    private const string Creator = "u-creator";
    private const string Assignee = "u-assignee";
    private const string AdminId = "u-admin";
    private const string Staff = "u-staff";

    private static async Task<(WorkTaskColumn Open, WorkTaskColumn Doing, WorkTaskColumn Done)> BoardAsync(TestDb db)
    {
        var board = new WorkTaskBoard { Title = "Umumiy" };
        var open = new WorkTaskColumn { BoardId = board.Id, Title = "Rejada", Order = 0, IsSystem = true };
        var doing = new WorkTaskColumn { BoardId = board.Id, Title = "Jarayonda", Order = 1 };
        var done = new WorkTaskColumn { BoardId = board.Id, Title = "Bajarildi", Order = 2, IsDone = true, IsSystem = true };
        db.Context.WorkTaskBoards.Add(board);
        db.Context.WorkTaskColumns.AddRange(open, doing, done);
        db.Context.Users.AddRange(
            new AppUser { Id = Creator, Email = "creator@t.uz", FullName = "Bergan", Role = Roles.Staff },
            new AppUser { Id = Assignee, Email = "assignee@t.uz", FullName = "Mas'ul", Role = Roles.Staff },
            new AppUser { Id = AdminId, Email = "admin@t.uz", FullName = "Admin", Role = Roles.Admin },
            new AppUser { Id = Staff, Email = "staff@t.uz", FullName = "Boshqa", Role = Roles.Staff });
        await db.Context.SaveChangesAsync();
        return (open, doing, done);
    }

    private static WorkTask Task(WorkTaskColumn col, bool approval = true, string repeat = "none") => new()
    {
        BoardId = col.BoardId, ColumnId = col.Id, Title = "Hisobot", AssigneeId = Assignee, CreatedById = Creator,
        ApprovalRequired = approval, Repeat = repeat, DueDate = AppClock.Today.ToString("yyyy-MM-dd"),
    };

    /// <summary>Panel "move into done" path, the same calls as <c>ApplyMoveAsync</c>.</summary>
    private static async Task CompleteAsync(TestDb db, WorkTask t, WorkTaskColumn done, string actor)
    {
        t.ColumnId = done.Id;
        t.CompletedAt = AppClock.Now;
        t.CompletedById = actor;
        await WorkTaskFlow.SpawnRepeatAsync(db.Context, t);
        await WorkTaskFlow.OnCompletedAsync(db.Context, t, actor);
        await db.Context.SaveChangesAsync();
    }

    [Fact]
    public void Standart_talab_va_holat()
    {
        Assert.True(WorkTaskApproval.DefaultRequired(null, Assignee, Creator));
        Assert.False(WorkTaskApproval.DefaultRequired(null, Creator, Creator));   // o'ziga — tasdiqsiz
        Assert.False(WorkTaskApproval.DefaultRequired(null, "", Creator));        // mas'ulsiz
        Assert.False(WorkTaskApproval.DefaultRequired(false, Assignee, Creator));
        Assert.True(WorkTaskApproval.DefaultRequired(true, Creator, Creator));

        Assert.Equal("open", WorkTaskApproval.State(false, false, true, null));
        Assert.Equal("overdue", WorkTaskApproval.State(false, true, true, null));
        Assert.Equal("done", WorkTaskApproval.State(true, false, false, null));
        Assert.Equal("awaiting_approval", WorkTaskApproval.State(true, false, true, null));
        Assert.Equal("approved", WorkTaskApproval.State(true, false, true, AppClock.Now));
        Assert.Equal("Tasdiq kutmoqda", WorkTaskApproval.StateLabels["awaiting_approval"]);
    }

    [Fact]
    public void Tasdiqlovchi_faqat_bergan_xodim_yoki_admin()
    {
        var t = new WorkTask { CreatedById = Creator, AssigneeId = Assignee };
        Assert.True(WorkTaskApproval.IsApprover(t, Creator, isAdmin: false));
        Assert.True(WorkTaskApproval.IsApprover(t, Staff, isAdmin: true));
        Assert.False(WorkTaskApproval.IsApprover(t, Assignee, isAdmin: false));   // mas'ul o'zini tasdiqlamaydi
        Assert.False(WorkTaskApproval.IsApprover(t, "", isAdmin: false));
        Assert.Equal("Faqat topshiriq bergan xodim tasdiqlaydi", WorkTaskApproval.NotApproverMessage);
    }

    [Fact]
    public async Task Bajarilganda_tasdiq_sorovi_va_tasdiqlash()
    {
        using var db = TestDb.Sqlite();
        var (open, _, done) = await BoardAsync(db);
        var t = Task(open);
        db.Context.WorkTasks.Add(t);
        await db.Context.SaveChangesAsync();

        Assert.Equal(WorkTaskApproval.NotDoneMessage, await WorkTaskFlow.ApproveAsync(db.Context, t, Creator));

        await CompleteAsync(db, t, done, Assignee);
        Assert.Equal(1, await db.Context.WorkTaskEvents.CountAsync(e => e.Kind == WorkTaskFlow.EventSubmitted));
        var bell = await db.Context.UserNotifications.SingleAsync();
        Assert.Equal(Creator, bell.UserId);

        Assert.Null(await WorkTaskFlow.ApproveAsync(db.Context, t, Creator));
        await db.Context.SaveChangesAsync();
        Assert.NotNull(t.ApprovedAt);
        Assert.Equal(Creator, t.ApprovedById);
        Assert.Equal(WorkTaskApproval.AlreadyApprovedMessage, await WorkTaskFlow.ApproveAsync(db.Context, t, Creator));
        Assert.Equal(1, await db.Context.UserNotifications.CountAsync(n => n.UserId == Assignee));   // "tasdiqlandi"
    }

    [Fact]
    public async Task Bergan_xodim_ozi_yopsa_sorov_yuborilmaydi_tasdiqsiz_topshiriqda_hech_narsa()
    {
        using var db = TestDb.Sqlite();
        var (open, _, done) = await BoardAsync(db);
        var t = Task(open);
        var plain = Task(open, approval: false);
        db.Context.WorkTasks.AddRange(t, plain);
        await db.Context.SaveChangesAsync();

        await CompleteAsync(db, t, done, Creator);
        await CompleteAsync(db, plain, done, Assignee);
        Assert.Empty(db.Context.UserNotifications);
        Assert.Equal(1, await db.Context.WorkTaskEvents.CountAsync(e => e.Kind == WorkTaskFlow.EventSubmitted));
        Assert.Equal(WorkTaskApproval.NotRequiredMessage, await WorkTaskFlow.ApproveAsync(db.Context, plain, Creator));
    }

    [Fact]
    public async Task Qaytarish_birinchi_ustunga_tushiradi_va_tozalaydi()
    {
        using var db = TestDb.Sqlite();
        var (open, _, done) = await BoardAsync(db);
        var other = Task(open, approval: false);
        var t = Task(open);
        db.Context.WorkTasks.AddRange(other, t);
        await db.Context.SaveChangesAsync();
        await CompleteAsync(db, t, done, Assignee);
        await WorkTaskFlow.ApproveAsync(db.Context, t, Creator);
        await db.Context.SaveChangesAsync();

        Assert.Equal(WorkTaskApproval.ReasonMessage, await WorkTaskFlow.ReturnAsync(db.Context, t, Creator, "  "));
        Assert.Equal(WorkTaskApproval.ReasonMessage, await WorkTaskFlow.ReturnAsync(db.Context, t, Creator, new string('x', 501)));

        Assert.Null(await WorkTaskFlow.ReturnAsync(db.Context, t, Creator, "Jadval to'liq emas"));
        await db.Context.SaveChangesAsync();
        Assert.Equal(open.Id, t.ColumnId);
        Assert.Equal(0, t.Order);
        Assert.Equal(1, other.Order);
        Assert.Null(t.CompletedAt);
        Assert.Null(t.ApprovedAt);
        Assert.Equal(1, t.ReturnCount);
        Assert.NotNull(t.LastReturnedAt);
        Assert.Contains(db.Context.WorkTaskEvents, e => e.Kind == WorkTaskFlow.EventReturned && e.Text == "Qaytarildi: Jadval to'liq emas");

        // Ochiq topshiriqni qaytarib bo'lmaydi.
        Assert.Equal(WorkTaskApproval.NotDoneMessage, await WorkTaskFlow.ReturnAsync(db.Context, t, Creator, "yana"));
    }

    [Fact]
    public async Task Bajarildi_ustunidan_chiqsa_tasdiq_bekor_bo_ladi()
    {
        using var db = TestDb.Sqlite();
        var (open, _, done) = await BoardAsync(db);
        var t = Task(open);
        db.Context.WorkTasks.Add(t);
        await db.Context.SaveChangesAsync();
        await CompleteAsync(db, t, done, Assignee);
        await WorkTaskFlow.ApproveAsync(db.Context, t, AdminId);
        await db.Context.SaveChangesAsync();

        // Ustun "bajarildi" emas bo'lib qoldi (yoki boshqa ko'chirish) — SyncCompletionAsync ochadi.
        Assert.True(await WorkTaskFlow.SyncCompletionAsync(db.Context, t, columnIsDone: false, AdminId));
        Assert.Null(t.ApprovedAt);
        Assert.Equal("", t.ApprovedById);
    }

    [Fact]
    public async Task Takroriy_nusxa_qaytarib_qayta_yopilganda_ham_bir_marta()
    {
        using var db = TestDb.Sqlite();
        var (open, _, done) = await BoardAsync(db);
        var t = Task(open, repeat: "daily");
        db.Context.WorkTasks.Add(t);
        await db.Context.SaveChangesAsync();

        await CompleteAsync(db, t, done, Assignee);
        await WorkTaskFlow.ReturnAsync(db.Context, t, Creator, "qayta");
        await db.Context.SaveChangesAsync();
        await CompleteAsync(db, t, done, Assignee);

        Assert.Equal(1, await db.Context.WorkTasks.CountAsync(x => x.RepeatOfId == t.Id));
        Assert.Equal(2, await db.Context.WorkTaskEvents.CountAsync(e => e.TaskId == t.Id && e.Kind == WorkTaskFlow.EventSubmitted));
    }

    [Fact]
    public async Task Bot_tasdiqlash_darvozasi_chat_boyicha()
    {
        using var db = TestDb.Sqlite();
        var (open, _, _) = await BoardAsync(db);
        var t = Task(open);
        db.Context.WorkTasks.Add(t);
        db.Context.TelegramRegistrations.AddRange(
            new TelegramRegistration { ChatId = 100, UserId = Creator },
            new TelegramRegistration { ChatId = 200, UserId = AdminId },
            new TelegramRegistration { ChatId = 300, UserId = Staff },
            new TelegramRegistration { ChatId = 400, UserId = Assignee },
            new TelegramRegistration { ChatId = 500, StudentId = "s-1" });
        await db.Context.SaveChangesAsync();

        Assert.Equal(Creator, await WorkTaskFlow.ChatApproverIdAsync(db.Context, 100, t));
        Assert.Equal(AdminId, await WorkTaskFlow.ChatApproverIdAsync(db.Context, 200, t));
        Assert.Null(await WorkTaskFlow.ChatApproverIdAsync(db.Context, 300, t));
        Assert.Null(await WorkTaskFlow.ChatApproverIdAsync(db.Context, 400, t));   // mas'ul o'zini tasdiqlamaydi
        Assert.Null(await WorkTaskFlow.ChatApproverIdAsync(db.Context, 500, t));
        Assert.Equal("Bu amal sizga ruxsat etilmagan", WorkTaskApproval.ChatForbiddenMessage);
    }
}
