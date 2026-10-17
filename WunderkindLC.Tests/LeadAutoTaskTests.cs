using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// SPEC-backlog-2026-10-17 F4 L1 (TZ 3.3): lid tashqi kanaldan ma'lumot qoldirganda «qo'ng'iroq» topshirig'i — navbat
/// bilan, bir lidda bitta ochiq topshiriq, qo'lda kiritilgan lidga yo'q, sozlama o'chiq bo'lsa hech narsa. Ustiga F3
/// revyusidagi «faqat superadmin beradigan kalit» qoidasi.
/// </summary>
public class LeadAutoTaskTests
{
    // ---------- Navbat (sof) ----------

    private static readonly HashSet<string> All = ["a", "b", "c", "lead-owner"];

    [Fact]
    public void Navbat_aylanadi_va_oxiridan_boshiga_qaytadi()
    {
        var pool = new[] { "a", "b", "c" };
        Assert.Equal(("a", 1), LeadAutoTask.Pick(pool, null, 0, All));
        Assert.Equal(("c", 0), LeadAutoTask.Pick(pool, null, 2, All));
        Assert.Equal(("a", 1), LeadAutoTask.Pick(pool, null, 3, All));   // eskirgan (katta) cursor ham yiqitmaydi
    }

    [Fact]
    public void Lidning_masuli_bolsa_unga__navbat_surilmaydi()
    {
        Assert.Equal(("lead-owner", 1), LeadAutoTask.Pick(["a", "b"], "lead-owner", 1, All));
    }

    [Fact]
    public void Ochirilgan_xodim_navbatdan_tushadi__hovuz_bosh_bolsa_masulsiz()
    {
        Assert.Equal(("b", 0), LeadAutoTask.Pick(["gone", "b"], null, 0, All));
        Assert.Equal(((string?)null, 0), LeadAutoTask.Pick(["gone"], "gone-too", 0, All));
    }

    // ---------- Baza ----------

    private static async Task<TestDb> SetupAsync(bool enabled = true, bool withBoard = true, params string[] pool)
    {
        var t = TestDb.Sqlite();
        var db = t.Context;
        foreach (var id in new[] { "a", "b" })
            db.Users.Add(new AppUser { Id = id, FullName = "Xodim " + id, Email = "x" + id, Role = Roles.Staff });
        db.CenterMeta.Add(new CenterMeta { LeadAutoTaskEnabled = enabled, LeadAutoTaskAssigneeIds = [.. pool] });
        if (withBoard)
        {
            var board = new WorkTaskBoard { Title = "Sotuv", Order = 0 };
            db.WorkTaskBoards.Add(board);
            db.WorkTaskColumns.AddRange(WorkTaskFlow.DefaultColumns(board.Id));
        }
        await db.SaveChangesAsync();
        return t;
    }

    private static async Task<Lead> LeadAsync(TestDb t, string name, Action<Lead>? edit = null)
    {
        var lead = new Lead { FullName = name, Phone = "+998901234567", Source = "Instagram", CreatedAt = AppClock.Iso() };
        edit?.Invoke(lead);
        t.Context.Leads.Add(lead);
        await t.Context.SaveChangesAsync();
        return lead;
    }

    [Fact]
    public async Task Sozlama_ochiq_bolsa_topshiriq_yaratilmaydi()
    {
        using var t = await SetupAsync(enabled: false, pool: ["a"]);
        var lead = await LeadAsync(t, "Ali");
        Assert.Null(await LeadAutoTask.EnsureAsync(t.Context, null, lead, repeat: false));
        Assert.Empty(await t.Context.WorkTasks.ToListAsync());
    }

    [Fact]
    public async Task Yangi_lidlar_navbat_bilan_taqsimlanadi__bugungi_muddat__qongiroqcha()
    {
        using var t = await SetupAsync(pool: ["a", "b"]);
        var who = new List<string>();
        foreach (var name in new[] { "Ali", "Vali", "Soli" })
        {
            var task = await LeadAutoTask.EnsureAsync(t.Context, null, await LeadAsync(t, name), repeat: false);
            Assert.NotNull(task);
            who.Add(task!.AssigneeId);
        }
        Assert.Equal(["a", "b", "a"], who);

        var tasks = await t.Context.WorkTasks.AsNoTracking().ToListAsync();
        Assert.All(tasks, x =>
        {
            Assert.Equal(AppClock.Today.ToString("yyyy-MM-dd"), x.DueDate);
            Assert.NotNull(x.LeadId);
            Assert.False(x.ApprovalRequired);
            Assert.StartsWith("Lidga qo'ng'iroq: ", x.Title);
            Assert.Contains("+998901234567", x.Description);
        });
        Assert.Equal(3, await t.Context.UserNotifications.CountAsync());
        Assert.Equal(3, await t.Context.WorkTaskEvents.CountAsync(e => e.Kind == "created"));
    }

    [Fact]
    public async Task Navbat_oxirgi_avto_topshiriqdan_aniqlanadi__CenterMeta_ozgartirilmaydi()
    {
        // Revyu S1: navbat saqlanmaydi — oxirgi avto-topshiriq olgan xodimdan keyingisi. Lid egasiga berilgan topshiriq ham
        // hisobga kiradi (egasi «b» → keyingi egasiz lid «a» ga).
        using var t = await SetupAsync(pool: ["a", "b"]);
        var tracked = await t.Context.CenterMeta.SingleAsync();
        var owned = await LeadAutoTask.EnsureAsync(t.Context, null, await LeadAsync(t, "Ali", l => l.AssigneeUserId = "b"), repeat: false);
        var next = await LeadAutoTask.EnsureAsync(t.Context, null, await LeadAsync(t, "Vali"), repeat: false);

        Assert.Equal(("b", "a"), (owned!.AssigneeId, next!.AssigneeId));
        Assert.Equal(EntityState.Unchanged, t.Context.Entry(tracked).State);   // CenterMeta'ga yozilmaydi — kesh tegilmaydi
    }

    [Fact]
    public async Task Bir_lidda_bitta_ochiq_topshiriq__yopilgandan_keyin_takroriy_murojaatda_yangisi()
    {
        using var t = await SetupAsync(pool: ["a"]);
        var lead = await LeadAsync(t, "Ali");
        var first = await LeadAutoTask.EnsureAsync(t.Context, null, lead, repeat: false);
        Assert.NotNull(first);
        Assert.Null(await LeadAutoTask.EnsureAsync(t.Context, null, lead, repeat: true));

        first!.CompletedAt = AppClock.Now;
        await t.Context.SaveChangesAsync();
        var second = await LeadAutoTask.EnsureAsync(t.Context, null, lead, repeat: true);
        Assert.NotNull(second);
        Assert.StartsWith("Takroriy murojaat", second!.Title);
    }

    [Fact]
    public async Task Lidning_masuli_bolsa_topshiriq_unga()
    {
        using var t = await SetupAsync(pool: ["a"]);
        var lead = await LeadAsync(t, "Ali", l => l.AssigneeUserId = "b");
        var task = await LeadAutoTask.EnsureAsync(t.Context, null, lead, repeat: false);
        Assert.Equal("b", task!.AssigneeId);
    }

    [Fact]
    public async Task Aylantirilgan_yoki_yoqotilgan_lidga_topshiriq_yoq()
    {
        using var t = await SetupAsync(pool: ["a"]);
        var converted = await LeadAsync(t, "Ali", l => l.ConvertedStudentId = "s1");
        var lost = await LeadAsync(t, "Vali", l => l.LostAt = AppClock.Iso());
        Assert.Null(await LeadAutoTask.EnsureAsync(t.Context, null, converted, repeat: false));
        Assert.Null(await LeadAutoTask.EnsureAsync(t.Context, null, lost, repeat: false));
        Assert.Empty(await t.Context.WorkTasks.ToListAsync());
    }

    [Fact]
    public async Task Doska_yoq_bolsa_Umumiy_doskasi_yaratiladi_va_birinchi_ustunga_tushadi()
    {
        using var t = await SetupAsync(withBoard: false, pool: ["a"]);
        var task = await LeadAutoTask.EnsureAsync(t.Context, null, await LeadAsync(t, "Ali"), repeat: false);
        Assert.NotNull(task);
        var board = await t.Context.WorkTaskBoards.AsNoTracking().SingleAsync();
        Assert.Equal("Umumiy", board.Title);
        var column = await t.Context.WorkTaskColumns.AsNoTracking().SingleAsync(c => c.Id == task!.ColumnId);
        Assert.Equal(("Rejada", false), (column.Title, column.IsDone));
    }

    [Fact]
    public async Task Notifier_orqali__bot_sozlanmagan_bolsa_ham_ishlaydi__qolda_kiritilganga_yoq()
    {
        using var t = await SetupAsync(pool: ["a"]);
        var telegram = new TelegramService(new NoNetworkHttpClientFactory(), NullLogger<TelegramService>.Instance);

        var manual = await LeadAsync(t, "Qo'lda");
        await LeadNotifier.NotifyNewLeadAsync(t.Context, telegram, manual, autoTask: false);
        Assert.Empty(await t.Context.WorkTasks.ToListAsync());

        var fromForm = await LeadAsync(t, "Formadan");
        await LeadNotifier.NotifyNewLeadAsync(t.Context, telegram, fromForm);
        Assert.Equal(fromForm.Id, (await t.Context.WorkTasks.AsNoTracking().SingleAsync()).LeadId);
    }

    // ---------- F3 revyu: faqat superadmin beradigan kalit ----------

    [Theory]
    [InlineData(new string[0], new[] { "finance", "expenseApproval" }, true)]      // berish
    [InlineData(new[] { "expenseApproval" }, new string[0], true)]                 // olish
    [InlineData(new[] { "staff", "expenseApproval" }, new[] { "expenseApproval", "finance" }, false)] // boshqa kalit o'zgardi
    [InlineData(new[] { "finance" }, new[] { "finance.main" }, false)]
    [InlineData(new string[0], new[] { "expenseApproval:view" }, true)]
    public void Chiqim_tasdigi_kaliti_faqat_superadmin_tomonidan(string[] before, string[] after, bool touches) =>
        Assert.Equal(touches, StaffRoles.ChangesSuperAdminOnly(before, after));
}
