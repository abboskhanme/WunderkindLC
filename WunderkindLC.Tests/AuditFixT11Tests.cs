using System.Net.WebSockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// T11 audit tuzatishlari (OPERATSIYALAR): NVR klipi (ffmpeg argumentlari, parolni tozalash),
/// o'chirilgan CTI agent, takroriy topshiriq, arxivlangan doska,
/// botdan naqd kitob buyurtmasi (olib ketishda to'lov) va shartnoma raqami navbati.
/// </summary>
public class AuditFixT11Tests
{
    // =============================================================================================
    //  OPS-1 — NVR klipi
    // =============================================================================================

    [Fact]
    public void Ffmpeg_argumentlarida_timeout_bor__stimeout_YOQ()
    {
        var args = HikvisionNvr.FfmpegArgs("rtsp://u:p@10.0.0.5:554/Streaming/tracks/101", 60);

        Assert.Contains("-timeout", args);
        Assert.DoesNotContain("-stimeout", args);        // ffmpeg 5.0 da olib tashlangan
        var i = Array.IndexOf(args, "-timeout");
        Assert.Equal("15000000", args[i + 1]);            // mikrosoniya = 15 s
        Assert.True(i < Array.IndexOf(args, "-i"));       // kirish opsiyasi — `-i` dan OLDIN
    }

    [Fact]
    public void Stderr_matnidagi_BARCHA_login_parollar_tozalanadi()
    {
        const string stderr =
            "[rtsp @ 0x55] method DESCRIBE failed: 401 Unauthorized\n"
            + "rtsp://admin:Sir%40123@10.0.0.5:554/Streaming/tracks/101?starttime=x: Server returned 401 Unauthorized\n"
            + "boshqa rtsp://root:qwerty@nvr.local/ manzil";

        var safe = HikvisionNvr.RedactText(stderr);

        Assert.DoesNotContain("admin", safe);
        Assert.DoesNotContain("Sir%40123", safe);
        Assert.DoesNotContain("qwerty", safe);
        Assert.Contains("rtsp://***:***@10.0.0.5:554", safe);
        Assert.Contains("rtsp://***:***@nvr.local/", safe);
    }

    [Fact]
    public void Klip_xato_SABABI_tushunarli_va_parolsiz()
    {
        var auth = HikvisionNvr.ClipFailureReason(
            "rtsp://admin:secret@10.0.0.5:554/Streaming/tracks/401: Server returned 401 Unauthorized", 1);
        Assert.Contains("login/parol", auth);
        Assert.DoesNotContain("secret", auth);

        var refused = HikvisionNvr.ClipFailureReason(
            "rtsp://admin:secret@10.0.0.5:554/Streaming/tracks/401: Connection refused", 1);
        Assert.Contains("ulanib bo'lmadi", refused);      // "401" kanal raqami AUTH deb o'qilmaydi

        var oldFfmpeg = HikvisionNvr.ClipFailureReason("Unrecognized option 'stimeout'.", 8);
        Assert.Contains("ffmpeg versiyasi", oldFfmpeg);   // "timeout" so'zi ulanish xatosi deb o'qilmaydi

        Assert.Contains("ffmpeg 1 kod", HikvisionNvr.ClipFailureReason("", 1));
        Assert.DoesNotContain("pw", HikvisionNvr.ClipFailureReason("rtsp://a:pw@h/x: Invalid data found", 1));
    }

    // =============================================================================================
    //  OPS-3 — o'chirilgan CTI agent
    // =============================================================================================

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static CtiSmsService Sms(CtiConnectionManager conn) =>
        new(conn, new FcmService(new FakeHttpClientFactory(), NullLogger<FcmService>.Instance),
            NullLogger<CtiSmsService>.Instance);

    private sealed class FakeSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        public List<string> Sent { get; } = new();
        public bool OutputClosed { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken ct)
        { _state = WebSocketState.Closed; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken ct)
        { OutputClosed = true; _state = WebSocketState.CloseSent; return Task.CompletedTask; }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct)
        {
            Sent.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Ochirilgan_STANDART_agent_SMS_uchun_TANLANMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var agent = new CtiAgent { Login = "ketgan", DisplayName = "Ketgan xodim", IsActive = false };
        ctx.CtiAgents.Add(agent);
        ctx.CenterMeta.Add(new CenterMeta { LocalSmsEnabled = true, LocalSmsDefaultAgentId = agent.Id });
        await ctx.SaveChangesAsync();
        var conn = new CtiConnectionManager();
        var socket = new FakeSocket();
        conn.AddOrReplace(agent.Id, socket);     // telefon hali ulanib turibdi

        var res = await Sms(conn).SendSmsAsync(ctx, null, "998901234567", "To'lov eslatmasi");

        Assert.False(res.Ok);
        Assert.Contains("o'chirilgan", res.Error!);
        Assert.Empty(socket.Sent);                                   // telefonga hech narsa ketmadi
        Assert.Empty(await ctx.CtiCommandLogs.ToListAsync());
        Assert.Equal("yetkazilmadi", (await ctx.SmsLogs.AsNoTracking().SingleAsync()).Status);

        // Aniq tanlangan o'chirilgan agent ham rad etiladi.
        var res2 = await Sms(conn).SendSmsAsync(ctx, agent.Id, "998901234567", "Salom");
        Assert.False(res2.Ok);
        Assert.Contains("o'chirilgan", res2.Error!);
    }

    [Fact]
    public async Task SMS_buyruq_id_si_jurnal_va_SmsLog_bilan_BIR_XIL()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var agent = new CtiAgent { Login = "a1", DisplayName = "Operator" };
        ctx.CtiAgents.Add(agent);
        await ctx.SaveChangesAsync();
        var conn = new CtiConnectionManager();
        var socket = new FakeSocket();
        conn.AddOrReplace(agent.Id, socket);

        var res = await Sms(conn).SendSmsAsync(ctx, agent.Id, "998901234567", "Salom");

        Assert.True(res.Ok);
        var cmd = await ctx.CtiCommandLogs.AsNoTracking().SingleAsync();
        Assert.Equal(cmd.Id, res.CommandId);                                     // ack shu id bilan keladi
        Assert.Equal(cmd.Id, (await ctx.SmsLogs.AsNoTracking().SingleAsync()).RequestId);
        Assert.Contains($"\"commandId\":\"{cmd.Id}\"", socket.Sent.Single());
    }

    [Fact]
    public async Task Agent_ochirilganda_logout_yuboriladi_va_ulanish_uziladi()
    {
        var conn = new CtiConnectionManager();
        var socket = new FakeSocket();
        conn.AddOrReplace("a1", socket);

        Assert.True(await conn.DisconnectAsync("a1"));

        Assert.Contains("\"action\":\"logout\"", socket.Sent.Single());
        Assert.True(socket.OutputClosed);
        Assert.False(conn.IsConnected("a1"));
        Assert.False(await conn.DisconnectAsync("a1"));                          // ikkinchi marta — jim
    }

    [Fact]
    public void Ochirilgan_agent_sababi_sozlamaga_yol_korsatadi()
    {
        var a = new CtiAgent { DisplayName = "Ali", IsActive = false };
        Assert.Contains("Sozlamalar", CtiSmsService.InactiveAgentError(a, isDefault: true));
        Assert.NotNull(CtiSmsService.InactiveAgentError(a, isDefault: false));
        a.IsActive = true;
        Assert.Null(CtiSmsService.InactiveAgentError(a, isDefault: true));
    }

    // =============================================================================================
    //  OPS-7 / OPS-8 — topshiriqlar
    // =============================================================================================

    private static async Task<(WorkTaskBoard Board, WorkTaskColumn Open, WorkTaskColumn Done)> BoardAsync(
        AppDbContext ctx, bool archived = false)
    {
        var board = new WorkTaskBoard { Title = "Umumiy", IsArchived = archived };
        var open = new WorkTaskColumn { BoardId = board.Id, Title = "Rejada", Order = 0, IsSystem = true };
        var done = new WorkTaskColumn { BoardId = board.Id, Title = "Bajarildi", Order = 3, IsDone = true, IsSystem = true };
        ctx.WorkTaskBoards.Add(board);
        ctx.WorkTaskColumns.AddRange(open, done);
        await ctx.SaveChangesAsync();
        return (board, open, done);
    }

    [Fact]
    public async Task Takroriy_topshiriq_yopish_qayta_ochish_yopish_BITTA_nusxa_tugdiradi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (board, open, _) = await BoardAsync(ctx);
        var t = new WorkTask
        {
            BoardId = board.Id, ColumnId = open.Id, Title = "Kassani topshirish",
            Repeat = "daily", DueDate = "2026-10-07", AssigneeId = "u1",
        };
        ctx.WorkTasks.Add(t);
        await ctx.SaveChangesAsync();

        Assert.True(await WorkTaskFlow.SpawnRepeatAsync(ctx, t));
        Assert.False(await WorkTaskFlow.SpawnRepeatAsync(ctx, t));   // saqlanmagan nusxa ham hisobga olinadi
        await ctx.SaveChangesAsync();
        Assert.False(await WorkTaskFlow.SpawnRepeatAsync(ctx, t));   // "Bajardim" ikki marta / qayta yopish
        await ctx.SaveChangesAsync();

        var copies = await ctx.WorkTasks.Where(x => x.RepeatOfId == t.Id).ToListAsync();
        var copy = Assert.Single(copies);
        Assert.Equal("2026-10-08", copy.DueDate);

        // Nusxa yopilsa — zanjir davom etadi (keyingi kun), ildiz bilan bir xil RepeatOfId.
        Assert.True(await WorkTaskFlow.SpawnRepeatAsync(ctx, copy));
        await ctx.SaveChangesAsync();
        Assert.Equal(new[] { "2026-10-08", "2026-10-09" },
            (await ctx.WorkTasks.Where(x => x.RepeatOfId == t.Id).Select(x => x.DueDate!).ToListAsync()).Order().ToList());
    }

    [Fact]
    public async Task Ustun_bajarildi_deb_belgilansa_CompletedAt_moslanadi__olinsa_qayta_ochiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (board, open, _) = await BoardAsync(ctx);
        var t = new WorkTask { BoardId = board.Id, ColumnId = open.Id, Title = "Hisobot", Repeat = "weekly", DueDate = "2026-10-07" };
        ctx.WorkTasks.Add(t);
        await ctx.SaveChangesAsync();

        Assert.True(await WorkTaskFlow.SyncCompletionAsync(ctx, t, columnIsDone: true, "admin"));
        await ctx.SaveChangesAsync();
        Assert.NotNull(t.CompletedAt);
        Assert.Equal("admin", t.CompletedById);
        Assert.Equal(1, await ctx.WorkTasks.CountAsync(x => x.RepeatOfId == t.Id));     // takroriysi tug'ildi

        Assert.False(await WorkTaskFlow.SyncCompletionAsync(ctx, t, columnIsDone: true, "admin")); // o'zgarish yo'q
        Assert.True(await WorkTaskFlow.SyncCompletionAsync(ctx, t, columnIsDone: false, "admin"));
        Assert.Null(t.CompletedAt);
    }

    [Fact]
    public async Task Arxivlangan_doska_topshiriqlari_ESLATMAGA_tushmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var live = await BoardAsync(ctx);
        var archived = await BoardAsync(ctx, archived: true);
        ctx.WorkTasks.AddRange(
            new WorkTask { BoardId = live.Board.Id, ColumnId = live.Open.Id, Title = "Tirik", AssigneeId = "u1", DueDate = "2026-10-01" },
            new WorkTask { BoardId = archived.Board.Id, ColumnId = archived.Open.Id, Title = "Arxivda", AssigneeId = "u1", DueDate = "2026-10-01" },
            new WorkTask { BoardId = live.Board.Id, ColumnId = live.Done.Id, Title = "Bajarilgan", AssigneeId = "u1", DueDate = "2026-10-01" });
        await ctx.SaveChangesAsync();

        var due = await WorkTaskFlow.DueForReminderAsync(ctx, "2026-10-07");

        Assert.Equal("Tirik", Assert.Single(due).Title);
    }

    // =============================================================================================
    //  OPS-6 — botdan NAQD kitob buyurtmasi: tasdiqlash = zaxira, pul olib ketishda
    // =============================================================================================

    private static Cashbox Box(string id, string name) =>
        new() { Id = id, Name = name, CreatedAt = "2026-10-01T10:00:00" };

    private static async Task<(AppDbContext Ctx, Book Book, BookOrder Order)> BotCashOrderAsync(TestDb db, int qty = 2)
    {
        var ctx = db.Context;
        ctx.Cashboxes.Add(Box(CashboxService.MainId, "Asosiy kassa"));
        ctx.TransactionTypes.AddRange(TransactionTypeCatalog.SeedRows());
        var book = new Book { Title = "Alifbo", Price = 30_000m, Stock = 10, IsActive = true };
        ctx.Books.Add(book);
        var order = new BookOrder
        {
            Number = 3, ChatId = 555, CustomerName = "Mijoz", BookId = book.Id, BookTitle = book.Title,
            UnitPrice = book.Price, Qty = qty, Total = book.Price * qty,
            PaymentMethod = BookSalesService.PayCash, Source = BookSalesService.SourceBot,
        };
        ctx.BookOrders.Add(order);
        await ctx.SaveChangesAsync();
        return (ctx, book, order);
    }

    [Fact]
    public async Task Bot_naqd_buyurtma_tasdiqlanganda_KASSAGA_KIRIM_YOQ__kitob_zaxirada()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book, order) = await BotCashOrderAsync(db);

        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Admin", "admin-1"));

        Assert.True(order.PayAtPickup);
        Assert.Null(order.PaidAt);
        Assert.False(BookSalesService.IsPaid(order));
        Assert.Equal(8, book.Stock);                                     // zaxiraga olindi
        Assert.Empty(await ctx.FinanceTransactions.ToListAsync());       // pul hali olinmagan

        // Mijoz keldi va to'ladi — kirim SHU PAYTDA, kassirning tanlagan turi bilan.
        Assert.Null(await BookSalesService.PayCreditAsync(ctx, order, BookSalesService.PayCard, "4321", "Kassir", "kassir-1"));
        var tx = await ctx.FinanceTransactions.AsNoTracking().SingleAsync();
        Assert.Equal(("income", 60_000m, BookSalesService.PayCard), (tx.Direction, tx.Amount, tx.Method));
        Assert.True(BookSalesService.IsPaid(order));
        Assert.Equal(8, book.Stock);                                     // ombor TEGILMADI
        Assert.NotNull(await BookSalesService.PayCreditAsync(ctx, order, BookSalesService.PayCash, null, "K", "k"));
    }

    [Fact]
    public async Task Olib_ketilmagan_buyurtma_BEKOR_qilinadi__qoldiq_qaytadi__pul_harakati_yoq()
    {
        using var db = TestDb.Sqlite();
        var (ctx, book, order) = await BotCashOrderAsync(db, qty: 3);
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Admin", "admin-1"));
        Assert.Equal(7, book.Stock);

        // Soxta "qaytarish" endi YO'Q — kitob mijozga berilmagan.
        Assert.Contains("Bekor qilish", await BookSalesService.ReturnAsync(ctx, order, 3, "", "Admin") ?? "");

        Assert.Null(await BookSalesService.CancelUnpaidAsync(ctx, order, "Mijoz kelmadi", "Admin"));

        Assert.Equal(10, book.Stock);
        Assert.Equal(BookSalesService.StatusRejected, order.Status);
        Assert.Contains("Mijoz kelmadi", order.RejectReason);
        Assert.Equal(0m, order.RefundedAmount);
        Assert.Empty(await ctx.FinanceTransactions.IgnoreQueryFilters().ToListAsync());
        var move = await ctx.BookStockMoves.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Qty).FirstAsync();
        Assert.Equal((3, BookSalesService.ReasonReturn), (move.Qty, move.Reason));   // "kirim" emas
    }

    [Fact]
    public async Task Tolangan_yoki_qolda_sotilgan_buyurtma_bekor_qilinmaydi()
    {
        using var db = TestDb.Sqlite();
        var (ctx, _, order) = await BotCashOrderAsync(db);
        Assert.Null(await BookSalesService.ApproveAsync(ctx, order, "Admin", "admin-1"));
        Assert.Null(await BookSalesService.PayCreditAsync(ctx, order, BookSalesService.PayCash, null, "K", "k"));
        Assert.NotNull(BookSalesService.CancelError(order));                       // to'langan — kitob berilgan

        var manual = new BookOrder { Status = BookSalesService.StatusApproved, PaymentMethod = BookSalesService.PayCash, Source = BookSalesService.SourceManual };
        Assert.False(BookSalesService.ShouldPayAtPickup(manual));
        Assert.NotNull(BookSalesService.CancelError(manual));
        var card = new BookOrder { PaymentMethod = BookSalesService.PayCard, Source = BookSalesService.SourceBot };
        Assert.False(BookSalesService.ShouldPayAtPickup(card));                    // chek bilan oldindan to'langan
        Assert.NotNull(BookSalesService.CancelError(new BookOrder { Status = BookSalesService.StatusPending }));
    }

    [Fact]
    public void Eski_tasdiqlangan_naqd_qatorlar_TOLANGAN_bolib_qoladi()
    {
        // PayAtPickup migratsiyada false — eski "approved" bot naqd buyurtmasi avvalgidek to'langan.
        var legacy = new BookOrder
        {
            Status = BookSalesService.StatusApproved, PaymentMethod = BookSalesService.PayCash,
            Source = BookSalesService.SourceBot, PaidAt = null,
        };
        Assert.True(BookSalesService.IsPaid(legacy));
        Assert.Equal(BookSalesService.PayCash, BookSalesService.EffectiveMethod(legacy));
    }

    // =============================================================================================
    //  Low (c) — shartnoma raqami navbati
    // =============================================================================================

    [Fact]
    public async Task Shartnoma_raqami_saqlanmasdan_turib_ham_TAKRORLANMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ContractService.ResetNumberSequence();
        ctx.Contracts.Add(new Contract { Number = 41, RecipientName = "Ali" });
        await ctx.SaveChangesAsync();

        var tasks = Enumerable.Range(0, 5).Select(_ => ContractService.NextNumberAsync(ctx)).ToList();
        var first = await tasks[0];
        var all = new List<int> { first };
        foreach (var t in tasks.Skip(1)) all.Add(await t);

        Assert.Equal(new[] { 42, 43, 44, 45, 46 }, all.Order().ToList());
        ContractService.ResetNumberSequence();
    }

    // =============================================================================================
    //  Low (e) — taklif/shikoyat auditi bo'limga tushadi
    // =============================================================================================

    [Fact]
    public void Feedback_audit_yozuvi_Boshqa_ga_tushmaydi()
    {
        Assert.Equal("students", AuditSections.SectionOf("Feedback"));
    }
}
