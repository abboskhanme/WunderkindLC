using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// TO'LOV BIR MARTA O'TADI — "Saqlash" bir necha marta bosilsa ham (<see cref="PaymentIdempotency"/>).
/// Ilgari himoya faqat "oxirgi 6 soniyadagi aynan shu to'lov" edi: u poygaga ochiq edi va
/// soniyalar oynasidan keyingi qayta yuborishni umuman ushlamasdi.
/// </summary>
public class PaymentIdempotencyTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static (Student s, Group g) Seed(AppDbContext ctx)
    {
        var month = AppClock.Today.ToString("yyyy-MM");
        var s = new Student { FullName = "Test O'quvchi", EnrollmentDate = $"{month}-01" };
        var g = new Group { Name = "A guruh", MonthlyFee = 600_000m, Days = new List<int> { 0, 2, 4 } };
        ctx.Students.Add(s);
        ctx.Classes.Add(g);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = $"{month}-01", ActivatedAt = $"{month}-01", RecordedAt = $"{month}-01",
        });
        ctx.SaveChanges();
        return (s, g);
    }

    private static Task<PaymentIntakeResult> Pay(AppDbContext ctx, Student s, string groupId, string? requestId) =>
        PaymentIntake.AddAsync(ctx, new AuditService(ctx, new NoHttpContext()), new MessagingStack().Auto, s,
            new PaymentRequest(200_000m, AppClock.Today.ToString("yyyy-MM"), groupId, Method: "cash",
                RequestId: requestId),
            createdBy: "Test kassir");

    /// <summary>Oxirgi to'lovni "bir daqiqa oldin" yozilgandek qiladi — 6 soniyalik oyna tashqarisi.</summary>
    private static async Task AgeTransactions(AppDbContext ctx)
    {
        foreach (var t in await ctx.FinanceTransactions.ToListAsync())
            t.CreatedAt = DateTime.UtcNow.AddMinutes(-1);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Bir_xil_kalit_bilan_QAYTA_yuborilgan_tolov_IKKINCHI_marta_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx);
        var key = Guid.NewGuid().ToString();

        var first = await Pay(ctx, s, g.Id, key);
        var balanceAfterFirst = s.Balance;
        // Tarmoq sekin — qayta urinish 6 soniyalik oynadan KEYIN keldi.
        await AgeTransactions(ctx);
        var second = await Pay(ctx, s, g.Id, key);

        Assert.Equal(first.TxId, second.TxId);
        Assert.True(second.Idempotent);
        Assert.Equal(1, await ctx.FinanceTransactions.CountAsync(t => t.StudentId == s.Id));
        Assert.Equal(balanceAfterFirst, s.Balance);
    }

    [Fact]
    public async Task Kalitsiz_va_oynadan_keyin_yangi_tolov_YOZILADI_eski_xatti_harakat()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx);

        await Pay(ctx, s, g.Id, requestId: null);
        await AgeTransactions(ctx);
        await Pay(ctx, s, g.Id, requestId: null);

        // Haqiqatan ikkinchi (alohida) to'lov — kalit yo'q, oyna o'tgan: bloklanmasligi kerak.
        Assert.Equal(2, await ctx.FinanceTransactions.CountAsync(t => t.StudentId == s.Id));
    }

    [Fact]
    public async Task Boshqa_kalit_yangi_oyna_degani_YANGI_tolov()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (s, g) = Seed(ctx);

        await Pay(ctx, s, g.Id, Guid.NewGuid().ToString());
        await AgeTransactions(ctx);
        await Pay(ctx, s, g.Id, Guid.NewGuid().ToString());

        Assert.Equal(2, await ctx.FinanceTransactions.CountAsync(t => t.StudentId == s.Id));
    }

    [Fact]
    public async Task Qulf_ikkinchi_sorovni_birinchisi_tugaguncha_KUTDIRADI()
    {
        var scope = "test:" + Guid.NewGuid();
        var first = await PaymentIdempotency.LockAsync(scope);
        var secondTask = PaymentIdempotency.LockAsync(scope);

        await Task.Delay(50);
        Assert.False(secondTask.IsCompleted);

        first.Dispose();
        first.Dispose(); // ikki marta bo'shatish semaforni buzmasin
        using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(secondTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Eskirgan_balans_bilan_kelgan_tolov_PARALLEL_tolov_tasirini_YOQOTMAYDI()
    {
        // Ikki xodim bir o'quvchiga deyarli bir vaqtda to'lov kiritadi: A so'rovi o'quvchini
        // (balans bilan) yuklab bo'lgan, shu orada B ning to'lovi saqlanadi. Ilgari A eski balans
        // ustidan yozib, B ning pulini balansdan jimgina "yo'qotardi".
        using var conn = new SqliteConnection("Filename=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        using var ctxA = new AppDbContext(options);
        ctxA.Database.EnsureCreated();
        var (seeded, g) = Seed(ctxA);

        var staleStudent = await ctxA.Students.FirstAsync(x => x.Id == seeded.Id);
        ctxA.ChangeTracker.Clear();
        staleStudent = await ctxA.Students.FirstAsync(x => x.Id == seeded.Id); // A: "eski" nusxa

        using (var ctxB = new AppDbContext(options))
        {
            var sB = await ctxB.Students.FirstAsync(x => x.Id == seeded.Id);
            await PaymentIntake.AddAsync(ctxB, new AuditService(ctxB, new NoHttpContext()), new MessagingStack().Auto, sB,
                new PaymentRequest(100_000m, AppClock.Today.ToString("yyyy-MM"), g.Id, Method: "cash"), "B kassir");
        }
        decimal afterB;
        using (var ctxCheck = new AppDbContext(options))
            afterB = (await ctxCheck.Students.AsNoTracking().FirstAsync(x => x.Id == seeded.Id)).Balance;

        await PaymentIntake.AddAsync(ctxA, new AuditService(ctxA, new NoHttpContext()), new MessagingStack().Auto, staleStudent,
            new PaymentRequest(250_000m, AppClock.Today.ToString("yyyy-MM"), g.Id, Method: "cash"), "A kassir");

        using var ctxFinal = new AppDbContext(options);
        var final = (await ctxFinal.Students.AsNoTracking().FirstAsync(x => x.Id == seeded.Id)).Balance;
        Assert.Equal(afterB + 250_000m, final);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc def")]
    [InlineData("../x")]
    public void Yaroqsiz_kalit_NULL_yani_kalitsiz_rejim(string? raw) =>
        Assert.Null(PaymentIdempotency.Key("student:1", raw));

    [Fact]
    public void Kalit_juda_uzun_bolsa_NULL() =>
        Assert.Null(PaymentIdempotency.Key("student:1", new string('a', PaymentIdempotency.MaxKeyLength + 1)));

    [Fact]
    public void Kalit_OQUVCHIGA_bogliq_boshqa_oquvchining_tolovini_qaytarmaydi()
    {
        var id = Guid.NewGuid().ToString();
        var a = PaymentIdempotency.Key("student:A", id);
        var b = PaymentIdempotency.Key("student:B", id);
        PaymentIdempotency.Remember(a, "tx-A", DateTime.UtcNow);

        Assert.Equal("tx-A", PaymentIdempotency.Find(a, DateTime.UtcNow));
        Assert.Null(PaymentIdempotency.Find(b, DateTime.UtcNow));
    }

    [Fact]
    public void Muddati_otgan_kalit_ESLANMAYDI()
    {
        var k = PaymentIdempotency.Key("student:A", Guid.NewGuid().ToString());
        var at = DateTime.UtcNow;
        PaymentIdempotency.Remember(k, "tx-old", at);

        Assert.Null(PaymentIdempotency.Find(k, at + PaymentIdempotency.KeyTtl + TimeSpan.FromSeconds(1)));
    }
}
