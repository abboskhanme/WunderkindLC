using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;
using System.Net;
using System.Text;

namespace WunderkindLC.Application.Services;

/// <summary>
/// ONLAYN TEST — Telegram bot oqimi. O'quvchi (yoki ota-onasi) botdagi «📝 Testni ishlash» tugmasini
/// bosadi → o'z guruh(lar)i uchun ochilgan onlayn testlar sanasi bilan chiqadi → testni tanlaganda
/// savollar PDF'i yuboriladi → javoblarni 2 usulda kiritadi (tugmalar bilan yoki bitta xabarda) →
/// yuborilgach avtomatik tekshiriladi, ball <see cref="TestScore"/>ga yoziladi (Source="bot") va
/// natija chiroyli ko'rinishda qaytariladi. Ball shu zahoti "Testlar natijalari" bo'limida ko'rinadi.
///
/// <para>Kim ko'radi: shu chat <see cref="TelegramRegistration"/> orqali bog'langan o'quvchi(lar)ning
/// FAOL a'zoligi bor guruhlari (ro'yxatdan o'tishda telefon markazdagi raqam bilan solishtirilgan).</para>
///
/// <para>Bir marta topshiriladi: (test, o'quvchi) uchun bot bali mavjud bo'lsa qayta topshirib
/// bo'lmaydi. Javob kaliti test vaqti TUGAGUNCHA ko'rsatilmaydi (birinchi topshirgan o'quvchi
/// kalitni tarqatib yubormasligi uchun) — tugagach to'liq tahlil chiqadi.</para>
///
/// <para><b>MARKAZDAN TASHQARI ISHTIROKCHI (test kodi).</b> Markazda o'qimaydigan odam ham testni
/// ishlay oladi: «📝 Testni ishlash» → «🔑 Test kodi bilan kirish» → KOD → F.I.Sh → test. Uning
/// natijasi <see cref="ExternalTestScore"/> ga yoziladi (u <see cref="Student"/> emas) va natijalar
/// ro'yxatida "Markazdan tashqari" bo'limida ko'rinadi. Majburiy kanal obunasi bu yo'lda ham
/// ishlaydi — darvoza <c>TelegramBotService.RequireSubscriptionAsync</c> da, tugma bosilishida.</para>
/// </summary>
public class OnlineTestBotService(TelegramService telegram, IHostEnvironment env, ILogger<OnlineTestBotService> logger)
{
    /// <summary>Reply-klaviaturadagi tugma matni (kod olish bilan adminga murojaat orasida).</summary>
    public const string TestButtonText = "📝 Testni ishlash";

    /// <summary><see cref="BotUser.Mode"/> qiymati: keyingi matn TEST KODI deb o'qiladi.</summary>
    public const string ModeAwaitingCode = "testcode";

    /// <summary><see cref="TestBotSession.Stage"/> qiymati: markazdan tashqari ishtirokchidan F.I.Sh kutilyapti.</summary>
    private const string StageName = "name";

    // ---------- callback_data prefikslari (Telegram cheklovi: 64 bayt) ----------
    /// <summary>Testni ochish: <c>ot:{testId}:{studentIdx}</c></summary>
    public const string CbOpen = "ot:";
    /// <summary>«Test kodi bilan kirish» — kod so'raladi.</summary>
    public const string CbCode = "ocode";
    /// <summary>Kiritish usuli: <c>omb</c> (tugmalar) | <c>omt</c> (bitta xabarda)</summary>
    public const string CbModeButtons = "omb";
    public const string CbModeText = "omt";
    /// <summary>Javob: <c>oa:{savol}:{harf}</c></summary>
    public const string CbAnswer = "oa:";
    /// <summary>Savolga o'tish: <c>og:{savol}</c></summary>
    public const string CbGoto = "og:";
    public const string CbFinish = "ofin";
    public const string CbConfirm = "ocon";
    public const string CbEdit = "oedit";
    public const string CbCancel = "ocan";
    public const string CbList = "olist";

    /// <summary>Berilgan callback shu servisga tegishlimi (TelegramBotService shunga qarab yo'naltiradi).</summary>
    public static bool Handles(string data) =>
        data.StartsWith(CbOpen, StringComparison.Ordinal)
        || data.StartsWith(CbAnswer, StringComparison.Ordinal)
        || data.StartsWith(CbGoto, StringComparison.Ordinal)
        || data is CbModeButtons or CbModeText or CbFinish or CbConfirm or CbEdit or CbCancel or CbList or CbCode;

    // ==================================================================================
    //  1) TESTLAR RO'YXATI
    // ==================================================================================

    /// <summary>«📝 Testni ishlash» — shu chatga bog'langan o'quvchi(lar)ning onlayn testlari ro'yxati.
    /// <para>Chat markaz o'quvchisiga bog'lanmagan bo'lsa (MARKAZDAN TASHQARI odam) ro'yxat bo'lmaydi,
    /// lekin «🔑 Test kodi bilan kirish» yo'li OCHIQ — o'qituvchi bergan kod bilan testni ishlaydi.
    /// Kod tugmasi bog'langan foydalanuvchilarga ham ko'rsatiladi (boshqa guruh testiga qo'shilishi mumkin).</para></summary>
    public async Task ShowListAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var students = await ChatStudentsAsync(db, chatId, ct);

        // MARKAZ O'QUVCHISI EMAS (chat hech qaysi o'quvchiga bog'lanmagan) — ro'yxat ko'rsatishning
        // ma'nosi yo'q: darhol TEST KODI so'raymiz (foydalanuvchi qo'shimcha tugma bosmasin).
        // Markaz o'quvchisi bo'lsa-yu hali ro'yxatdan o'tmagan bo'lsa — telefon yuborish yo'li ham
        // shu xabarda aytiladi (kontakt yuborish matn emas, shuning uchun kod rejimiga xalaqit bermaydi).
        if (students.Count == 0)
        {
            await SetBotModeAsync(db, chatId, ModeAwaitingCode, ct);
            await telegram.SendMessageAsync(chatId,
                "🔑 <b>Test kodini yuboring.</b>\n\n"
                + "Markazda o'qimasangiz ham testni ishlashingiz mumkin — o'qituvchi bergan kodni "
                + "shu yerga yozing (masalan <code>K7M4QP</code>).\n\n"
                + "ℹ️ Markaz o'quvchisi bo'lsangiz — telefon raqamingizni yuboring, keyin testlaringiz "
                + "shu tugmada o'zi chiqadi.",
                new
                {
                    inline_keyboard = new object[][]
                    {
                        new object[] { new { text = "✖️ Bekor qilish", callback_data = CbCancel } },
                    },
                }, ct, "HTML");
            return;
        }

        var items = await AvailableTestsAsync(db, students, ct);

        var rows = new List<object[]>();
        var text = new StringBuilder("📝 <b>Onlayn testlar</b>\n\n");

        if (items.Count > 0)
        {
            var multi = students.Count > 1;
            foreach (var it in items)
            {
                var state = StateOf(it);
                text.Append($"{state.Icon} <b>{Esc(it.Test.Name)}</b>\n")
                    .Append($"     📅 {Human(it.Test.Date)} · {it.Test.QuestionCount} ta savol\n")
                    .Append($"     ⏰ {Clock(it.Test.StartAt)} – {Clock(it.Test.EndAt)} · {state.Label}\n")
                    .Append(multi ? $"     👤 {Esc(it.StudentName)}\n" : "")
                    .Append('\n');
                var label = $"{state.Icon} {Human(it.Test.Date)} — {Short(it.Test.Name, 22)}"
                            + (multi ? $" ({Short(it.StudentName, 12)})" : "");
                rows.Add(new object[] { new { text = label, callback_data = CbOpen + it.Test.Id + ":" + it.StudentIndex } });
            }
            text.Append("Ishlamoqchi bo'lgan testni tanlang 👇\n\n")
                .Append("Yoki o'qituvchingiz alohida <b>test kodi</b> bergan bo'lsa — pastdagi tugma.");
        }
        else
        {
            text.Append("📭 Hozircha siz uchun ochilgan onlayn test yo'q.\n\n")
                .Append("O'qituvchingiz test e'lon qilganda, shu tugma orqali uni ishlashingiz mumkin bo'ladi.\n\n")
                .Append("Sizga alohida <b>test kodi</b> berilgan bo'lsa — pastdagi tugmani bosing.");
        }

        rows.Add(new object[] { new { text = "🔑 Test kodi bilan kirish", callback_data = CbCode } });

        await telegram.SendMessageAsync(chatId, text.ToString(),
            new { inline_keyboard = rows.ToArray() }, ct, "HTML");
    }

    // ==================================================================================
    //  1b) TEST KODI — markazda o'qimaydigan (yoki boshqa guruhdagi) ishtirokchi uchun
    // ==================================================================================

    /// <summary>«🔑 Test kodi bilan kirish» bosildi — keyingi matn KOD deb o'qiladi
    /// (<see cref="BotUser.Mode"/> = <see cref="ModeAwaitingCode"/>).</summary>
    public async Task AskCodeAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        await SetBotModeAsync(db, chatId, ModeAwaitingCode, ct);
        await telegram.SendMessageAsync(chatId,
            "🔑 <b>Test kodini yuboring.</b>\n\n"
            + "Kodni o'qituvchi yoki markaz beradi — masalan <code>K7M4QP</code>.\n"
            + "Kichik harf, probel, tire — farqi yo'q.",
            new
            {
                inline_keyboard = new object[][]
                {
                    new object[] { new { text = "✖️ Bekor qilish", callback_data = CbCancel } },
                },
            }, ct, "HTML");
    }

    /// <summary>Chat hozir TEST KODI kutyaptimi (matn shu servisga yo'naltirilsinmi).</summary>
    public static async Task<bool> AwaitingCodeAsync(IAppDbContext db, long chatId, CancellationToken ct) =>
        await db.BotUsers.AnyAsync(u => u.ChatId == chatId && u.Mode == ModeAwaitingCode, ct);

    /// <summary>Kod yuborildi: test topilsa ishlash boshlanadi. Chat markaz o'quvchisiga bog'langan
    /// bo'lsa — o'sha o'quvchi nomidan (bali <see cref="TestScore"/> ga), aks holda F.I.Sh so'raladi
    /// va natija <see cref="ExternalTestScore"/> ga yoziladi.</summary>
    public async Task HandleCodeAsync(IAppDbContext db, long chatId, string text, CancellationToken ct)
    {
        var test = await TestResultService.FindByCodeAsync(db, text);
        if (test is null)
        {
            await telegram.SendMessageAsync(chatId,
                "❌ Bunday kodli test topilmadi.\n\n"
                + "Kodni tekshirib qaytadan yuboring yoki o'qituvchingizdan so'rang.",
                new
                {
                    inline_keyboard = new object[][]
                    {
                        new object[] { new { text = "✖️ Bekor qilish", callback_data = CbCancel } },
                    },
                }, ct);
            return;   // rejim saqlanadi — foydalanuvchi qayta urinib ko'radi
        }

        await SetBotModeAsync(db, chatId, "", ct);

        var students = await ChatStudentsAsync(db, chatId, ct);
        if (students.Count == 1)
        {
            await StartAttemptAsync(db, chatId, test, students[0].Id, students[0].Name, ct);
            return;
        }
        if (students.Count > 1)
        {
            // Bir chatda bir nechta farzand — kim ishlashini so'raymiz (odatdagi ro'yxat tugmalari).
            await telegram.SendMessageAsync(chatId,
                $"📝 <b>{Esc(test.Name)}</b>\n\nTestni kim ishlaydi?",
                new
                {
                    inline_keyboard = students
                        .Select((s, i) => new object[]
                        {
                            new { text = $"👤 {Short(s.Name, 30)}", callback_data = CbOpen + test.Id + ":" + i },
                        })
                        .Append(new object[] { new { text = "✖️ Bekor qilish", callback_data = CbCancel } })
                        .ToArray(),
                }, ct, "HTML");
            return;
        }

        // MARKAZDAN TASHQARI — avval F.I.Sh so'raymiz (natija shu ism bilan chiqadi).
        var gate = await GateAsync(db, test, chatId, studentId: null, ct);
        if (gate is not null) { await telegram.SendMessageAsync(chatId, gate, null, ct, "HTML"); return; }

        await ResetSessionAsync(db, chatId, new TestBotSession
        {
            ChatId = chatId, TestResultId = test.Id, StudentId = "",
            Answers = new string('-', test.QuestionCount), Current = 0,
            InputMode = "buttons", Stage = StageName,
            StartedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        }, ct);

        await telegram.SendMessageAsync(chatId,
            $"✅ Kod to'g'ri — <b>{Esc(test.Name)}</b>\n\n"
            + "👤 Endi <b>familiya va ismingizni</b> yuboring.\n"
            + "Natija ro'yxatida aynan shu ism ko'rinadi — to'g'ri yozing.\n\n"
            + "Masalan: <code>Aliyev Vali</code>",
            new
            {
                inline_keyboard = new object[][]
                {
                    new object[] { new { text = "✖️ Bekor qilish", callback_data = CbCancel } },
                },
            }, ct, "HTML");
    }

    /// <summary>F.I.Sh yuborildi (markazdan tashqari ishtirokchi) — saqlanadi va test boshlanadi.</summary>
    private async Task HandleNameAsync(
        IAppDbContext db, long chatId, TestBotSession session, TestResult test, string text, CancellationToken ct)
    {
        var name = (text ?? "").Trim();
        if (name.Length is < 3 or > 120)
        {
            await telegram.SendMessageAsync(chatId,
                "⚠️ Familiya va ismingizni to'liqroq yozing (kamida 3 ta belgi).", null, ct);
            return;
        }

        session.ExternalName = name;
        session.Stage = "";
        await db.SaveChangesAsync(ct);

        await SendPdfAsync(db, chatId, test, ct);
        await AskInputModeAsync(chatId, test, name, ct);
    }

    // ==================================================================================
    //  2) TESTNI OCHISH (PDF + kiritish usuli)
    // ==================================================================================

    /// <summary>Test tanlandi: holat tekshiriladi, PDF yuboriladi va javob kiritish usuli so'raladi.</summary>
    public async Task OpenTestAsync(IAppDbContext db, long chatId, string data, CancellationToken ct)
    {
        var parts = data[CbOpen.Length..].Split(':');
        var testId = parts[0];
        var sIdx = parts.Length > 1 && int.TryParse(parts[1], out var i) ? i : 0;

        var students = await ChatStudentsAsync(db, chatId, ct);
        if (sIdx < 0 || sIdx >= students.Count)
        {
            await telegram.SendMessageAsync(chatId, "O'quvchi topilmadi. Ro'yxatni qaytadan oching.", null, ct);
            return;
        }
        var (studentId, studentName) = students[sIdx];

        var test = await db.TestResults.AsNoTracking().FirstOrDefaultAsync(t => t.Id == testId, ct);
        if (test is null || test.Mode != "online")
        {
            await telegram.SendMessageAsync(chatId, "Test topilmadi yoki o'chirilgan.", null, ct);
            return;
        }

        await StartAttemptAsync(db, chatId, test, studentId, studentName, ct);
    }

    /// <summary>
    /// TESTNI BOSHLASH — ikkala yo'l uchun ham YAGONA nuqta: ro'yxatdan tanlash (markaz o'quvchisi) va
    /// test KODI bilan kirish. Tekshiradi (allaqachon topshirgan / vaqt oynasi), PDF yuboradi,
    /// sessiya ochadi va javob kiritish usulini so'raydi.
    /// <paramref name="studentId"/> bo'sh bo'lsa — markazdan tashqari ishtirokchi
    /// (<paramref name="displayName"/> — u yozgan F.I.Sh).
    /// </summary>
    private async Task StartAttemptAsync(
        IAppDbContext db, long chatId, TestResult test, string studentId, string displayName, CancellationToken ct)
    {
        var gate = await GateAsync(db, test, chatId, studentId, ct);
        if (gate is not null) { await telegram.SendMessageAsync(chatId, gate, null, ct, "HTML"); return; }

        // Savollar PDF'i (file_id keshi bilan — bir marta yuklanadi).
        await SendPdfAsync(db, chatId, test, ct);

        await ResetSessionAsync(db, chatId, new TestBotSession
        {
            ChatId = chatId, TestResultId = test.Id, StudentId = studentId,
            Answers = new string('-', test.QuestionCount), Current = 0,
            InputMode = "buttons", Stage = "",
            ExternalName = studentId.Length == 0 ? displayName : "",
            StartedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        }, ct);

        await AskInputModeAsync(chatId, test, displayName, ct);
    }

    /// <summary>Testni ishlashga RUXSAT tekshiruvi. null = boshlash mumkin; aks holda — foydalanuvchiga
    /// ko'rsatiladigan sabab (allaqachon topshirgan / hali boshlanmagan / vaqti tugagan).
    /// <paramref name="studentId"/> null yoki bo'sh — markazdan tashqari ishtirokchi (chat bo'yicha).</summary>
    private static async Task<string?> GateAsync(
        IAppDbContext db, TestResult test, long chatId, string? studentId, CancellationToken ct)
    {
        // Allaqachon topshirganmi?
        if (!string.IsNullOrEmpty(studentId))
        {
            var existing = await db.TestScores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TestResultId == test.Id && s.StudentId == studentId, ct);
            if (existing is not null && OnlineTestService.IsStudentSubmission(existing.Source))
            {
                var name = await db.Students.AsNoTracking()
                    .Where(s => s.Id == studentId).Select(s => s.FullName).FirstOrDefaultAsync(ct) ?? "";
                return BuildResultText(test, existing.Answers, (int)existing.Score, name, existing.SubmittedAt,
                    rank: null, total: null, header: "ℹ️ Siz bu testni allaqachon topshirgansiz");
            }
        }
        else
        {
            var ext = await db.ExternalTestScores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TestResultId == test.Id && s.ChatId == chatId, ct);
            if (ext is not null)
                return BuildResultText(test, ext.Answers, (int)ext.Score, ext.FullName, ext.SubmittedAt,
                    rank: null, total: null, header: "ℹ️ Siz bu testni allaqachon topshirgansiz");
        }

        var now = NowStamp();
        if (string.CompareOrdinal(now, StartOf(test)) < 0)
            return $"⏳ Test hali boshlanmadi.\n\n📝 <b>{Esc(test.Name)}</b>\n"
                   + $"🕒 Boshlanishi: <b>{Human(test.Date)} {Clock(test.StartAt)}</b>\n\n"
                   + "Shu vaqtda qayta kiring.";
        if (string.CompareOrdinal(now, EndOf(test)) > 0)
            return $"⛔️ Test vaqti tugagan.\n\n📝 <b>{Esc(test.Name)}</b>\n"
                   + $"🕒 Tugagan: <b>{Human(test.Date)} {Clock(test.EndAt)}</b>\n\n"
                   + "O'qituvchingizga murojaat qiling.";
        return null;
    }

    /// <summary>Javob kiritish usulini so'rovchi xabar (ikkala yo'lda ham bir xil).</summary>
    private async Task AskInputModeAsync(long chatId, TestResult test, string displayName, CancellationToken ct) =>
        await telegram.SendMessageAsync(chatId,
            $"📝 <b>{Esc(test.Name)}</b>\n"
            + $"👤 {Esc(displayName)}\n"
            + $"❓ Savollar: <b>{test.QuestionCount}</b> ta · variantlar: <b>A–{Letter(test.OptionCount - 1)}</b>\n"
            + $"⏰ Javoblar qabul qilinadi: <b>{Clock(test.StartAt)} – {Clock(test.EndAt)}</b>\n"
            + $"❗️ Bitta urinish beriladi.\n\n"
            + "Javoblarni qanday kiritasiz?",
            new
            {
                inline_keyboard = new object[][]
                {
                    new object[] { new { text = "🔘 Tugmalar bilan (savol-savol)", callback_data = CbModeButtons } },
                    new object[] { new { text = "⌨️ Bitta xabarda (masalan: abcd...)", callback_data = CbModeText } },
                    new object[] { new { text = "✖️ Bekor qilish", callback_data = CbCancel } },
                },
            }, ct, "HTML");

    /// <summary>Chatdagi eski sessiyani o'chirib yangisini qo'yadi (bitta chatda bitta faol sessiya).</summary>
    private static async Task ResetSessionAsync(
        IAppDbContext db, long chatId, TestBotSession fresh, CancellationToken ct)
    {
        var old = await db.TestBotSessions.FirstOrDefaultAsync(s => s.ChatId == chatId, ct);
        if (old is not null) db.TestBotSessions.Remove(old);
        db.TestBotSessions.Add(fresh);
        await db.SaveChangesAsync(ct);
    }

    /// <summary><see cref="BotUser.Mode"/> ni o'zgartiradi (BotUser bo'lmasa — jim o'tadi:
    /// u /start da yaratiladi, kod oqimi esa har doim tugma bosishdan keyin keladi).</summary>
    private static async Task SetBotModeAsync(IAppDbContext db, long chatId, string mode, CancellationToken ct)
    {
        var user = await db.BotUsers.FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
        if (user is null || user.Mode == mode) return;
        user.Mode = mode;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Savollar PDF'ini yuboradi. Telegram <c>file_id</c> keshlanadi — keyingi o'quvchilarga
    /// fayl qayta yuklanmaydi (APK yuborish bilan bir xil usul).</summary>
    private async Task SendPdfAsync(IAppDbContext db, long chatId, TestResult test, CancellationToken ct)
    {
        try
        {
            var caption = $"📄 {test.Name} — savollar";
            var fileName = string.IsNullOrWhiteSpace(test.PdfName) ? "test.pdf" : test.PdfName;
            if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) fileName += ".pdf";

            string? newId;
            if (test.PdfFileId.Length > 0)
            {
                newId = await telegram.SendDocumentReturningIdAsync(
                    chatId, test.PdfFileId, null, fileName, "application/pdf", caption, ct);
            }
            else
            {
                var rel = test.PdfUrl.TrimStart('/');
                var abs = Path.Combine(env.ContentRootPath, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(abs))
                {
                    await telegram.SendMessageAsync(chatId,
                        "⚠️ Savollar fayli topilmadi — o'qituvchingizga xabar bering.", null, ct);
                    return;
                }
                var bytes = await File.ReadAllBytesAsync(abs, ct);
                newId = await telegram.SendDocumentReturningIdAsync(
                    chatId, null, bytes, fileName, "application/pdf", caption, ct);
            }

            if (!string.IsNullOrEmpty(newId) && newId != test.PdfFileId)
            {
                var tracked = await db.TestResults.FirstOrDefaultAsync(t => t.Id == test.Id, ct);
                if (tracked is not null) { tracked.PdfFileId = newId; await db.SaveChangesAsync(ct); }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Onlayn test PDF yuborilmadi: test {Id}", test.Id);
        }
    }

    // ==================================================================================
    //  3) JAVOB KIRITISH — tugmalar rejimi
    // ==================================================================================

    /// <summary>Kiritish usuli tanlandi.</summary>
    public async Task SetModeAsync(IAppDbContext db, long chatId, bool buttons, CancellationToken ct)
    {
        var (session, test) = await SessionAsync(db, chatId, ct);
        if (session is null || test is null) { await NoSessionAsync(chatId, ct); return; }
        // Hali F.I.Sh kiritilmagan (markazdan tashqari ishtirokchi) — avval ism kerak.
        if (session.Stage == StageName)
        {
            await telegram.SendMessageAsync(chatId,
                "👤 Avval familiya va ismingizni yuboring.", null, ct);
            return;
        }

        session.InputMode = buttons ? "buttons" : "text";
        session.Current = FirstEmpty(session.Answers);
        await db.SaveChangesAsync(ct);

        if (buttons)
        {
            var msgId = await telegram.SendMessageReturningIdAsync(
                chatId, SheetText(test, session), SheetKeyboard(test, session), ct, "HTML");
            if (msgId is not null)
            {
                session.MessageId = msgId.Value;
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            var sample = SampleAnswers(test.QuestionCount, test.OptionCount);
            await telegram.SendMessageAsync(chatId,
                "⌨️ <b>Barcha javoblarni BITTA xabarda yuboring.</b>\n\n"
                + $"Kerak: <b>{test.QuestionCount}</b> ta javob (A–{Letter(test.OptionCount - 1)}).\n\n"
                + "Namuna:\n"
                + $"<code>{sample}</code>\n"
                + "yoki raqam bilan:\n"
                + $"<code>{NumberedSample(test.OptionCount)}</code>\n\n"
                + "Bosh/kichik harf, probel, vergul — farqi yo'q.",
                new
                {
                    inline_keyboard = new object[][]
                    {
                        new object[] { new { text = "🔘 Tugmalar bilan kiritaman", callback_data = CbModeButtons } },
                        new object[] { new { text = "✖️ Bekor qilish", callback_data = CbCancel } },
                    },
                }, ct, "HTML");
        }
    }

    /// <summary>Tugma bosildi: <c>oa:{savol}:{harf}</c> — javob yoziladi va varaqa joyida yangilanadi.</summary>
    public async Task AnswerAsync(IAppDbContext db, long chatId, string data, CancellationToken ct)
    {
        var (session, test) = await SessionAsync(db, chatId, ct);
        if (session is null || test is null) { await NoSessionAsync(chatId, ct); return; }

        var parts = data[CbAnswer.Length..].Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var q) || parts[1].Length == 0) return;
        var letter = char.ToUpperInvariant(parts[1][0]);
        if (q < 0 || q >= test.QuestionCount) return;
        if (letter < 'A' || letter > Letter(test.OptionCount - 1)) return;

        var arr = Pad(session.Answers, test.QuestionCount).ToCharArray();
        // Shu javobni qayta bosish — belgini olib tashlaydi (fikrini o'zgartirsa).
        arr[q] = arr[q] == letter ? '-' : letter;
        session.Answers = new string(arr);
        // Keyingi javobsiz savolga avtomatik o'tamiz (bo'lmasa — shu joyda qolamiz).
        var next = FirstEmpty(session.Answers);
        session.Current = next >= 0 ? next : q;
        await db.SaveChangesAsync(ct);

        await RedrawAsync(db, chatId, session, test, ct);
    }

    /// <summary>Savolga o'tish (<c>og:{savol}</c>) — oldingi/keyingi tugmalari.</summary>
    public async Task GotoAsync(IAppDbContext db, long chatId, string data, CancellationToken ct)
    {
        var (session, test) = await SessionAsync(db, chatId, ct);
        if (session is null || test is null) { await NoSessionAsync(chatId, ct); return; }
        if (!int.TryParse(data[CbGoto.Length..], out var q)) return;
        session.Current = Math.Clamp(q, 0, test.QuestionCount - 1);
        await db.SaveChangesAsync(ct);
        await RedrawAsync(db, chatId, session, test, ct);
    }

    /// <summary>«Yakunlash» — javoblar ro'yxati va tasdiqlash so'raladi.</summary>
    public async Task FinishAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var (session, test) = await SessionAsync(db, chatId, ct);
        if (session is null || test is null) { await NoSessionAsync(chatId, ct); return; }

        var answers = Pad(session.Answers, test.QuestionCount);
        var empty = answers.Count(c => c == '-');
        var warn = empty > 0
            ? $"\n⚠️ <b>{empty} ta savol javobsiz</b> — ular xato hisoblanadi.\n"
            : "";

        await telegram.SendMessageAsync(chatId,
            "📋 <b>Javoblaringiz:</b>\n"
            + $"<code>{AnswerGrid(answers)}</code>\n"
            + warn
            + "\n❗️ Yuborilgandan keyin o'zgartirib bo'lmaydi. Yuborilsinmi?",
            new
            {
                inline_keyboard = new object[][]
                {
                    new object[] { new { text = "✅ Ha, yuborish", callback_data = CbConfirm } },
                    new object[] { new { text = "✏️ Yo'q, o'zgartiraman", callback_data = CbEdit } },
                },
            }, ct, "HTML");
    }

    /// <summary>«O'zgartiraman» — javob varaqasini qayta chizadi (tugmali rejim).</summary>
    public async Task EditAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var (session, test) = await SessionAsync(db, chatId, ct);
        if (session is null || test is null) { await NoSessionAsync(chatId, ct); return; }
        session.InputMode = "buttons";
        session.Current = Math.Max(0, FirstEmpty(session.Answers));
        var msgId = await telegram.SendMessageReturningIdAsync(
            chatId, SheetText(test, session), SheetKeyboard(test, session), ct, "HTML");
        if (msgId is not null) session.MessageId = msgId.Value;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sessiyani bekor qiladi (javoblar saqlanmaydi) va kod kutish rejimini ham o'chiradi.</summary>
    public async Task CancelAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var s = await db.TestBotSessions.FirstOrDefaultAsync(x => x.ChatId == chatId, ct);
        if (s is not null) { db.TestBotSessions.Remove(s); await db.SaveChangesAsync(ct); }
        await SetBotModeAsync(db, chatId, "", ct);
        await telegram.SendMessageAsync(chatId,
            "✖️ Bekor qilindi. Testni «" + TestButtonText + "» tugmasi orqali qaytadan ochishingiz mumkin.", null, ct);
    }

    // ==================================================================================
    //  4) JAVOB KIRITISH — matn rejimi
    // ==================================================================================

    /// <summary>Chatda faol test sessiyasi bormi (matn xabarini shu servis ushlashi kerakmi).</summary>
    public async Task<bool> HasSessionAsync(IAppDbContext db, long chatId, CancellationToken ct) =>
        await db.TestBotSessions.AnyAsync(s => s.ChatId == chatId, ct);

    /// <summary>Matn keldi (matn rejimida javoblar). Tahlil qilinadi va tasdiqlash so'raladi.
    /// Qaytadi: xabar shu servis tomonidan ishlandimi.</summary>
    public async Task<bool> HandleTextAsync(IAppDbContext db, long chatId, string text, CancellationToken ct)
    {
        var (session, test) = await SessionAsync(db, chatId, ct);
        if (session is null || test is null) return false;

        // MARKAZDAN TASHQARI ishtirokchidan F.I.Sh kutilyapti — javob emas, ism.
        if (session.Stage == StageName)
        {
            await HandleNameAsync(db, chatId, session, test, text, ct);
            return true;
        }

        var parsed = ParseAnswers(text, test.OptionCount);
        if (parsed.Length == 0) return false;   // javobga o'xshamaydi — oddiy oqimga qaytaramiz

        if (parsed.Length != test.QuestionCount)
        {
            await telegram.SendMessageAsync(chatId,
                $"⚠️ <b>{test.QuestionCount}</b> ta javob kerak, siz <b>{parsed.Length}</b> ta yubordingiz.\n\n"
                + $"Topilgan javoblar: <code>{parsed}</code>\n\n"
                + "Qaytadan, to'liq yuboring yoki tugmalar bilan kiriting.",
                new
                {
                    inline_keyboard = new object[][]
                    {
                        new object[] { new { text = "🔘 Tugmalar bilan kiritaman", callback_data = CbModeButtons } },
                        new object[] { new { text = "✖️ Bekor qilish", callback_data = CbCancel } },
                    },
                }, ct, "HTML");
            return true;
        }

        session.Answers = parsed;
        session.InputMode = "text";
        await db.SaveChangesAsync(ct);

        await telegram.SendMessageAsync(chatId,
            "📋 <b>Javoblaringiz qabul qilindi:</b>\n"
            + $"<code>{AnswerGrid(parsed)}</code>\n\n"
            + "❗️ Yuborilgandan keyin o'zgartirib bo'lmaydi. Yuborilsinmi?",
            new
            {
                inline_keyboard = new object[][]
                {
                    new object[] { new { text = "✅ Ha, yuborish", callback_data = CbConfirm } },
                    new object[] { new { text = "✏️ Qaytadan kiritaman", callback_data = CbEdit } },
                },
            }, ct, "HTML");
        return true;
    }

    /// <summary>Matndan javob harflarini ajratib oladi: "abcda", "1a 2b 3c", "1) A, 2) B" — hammasi bo'ladi.
    /// Raqamlar/tinish belgilari e'tiborsiz; kirill A/В/С/Е/Д harflari lotinga o'giriladi.</summary>
    public static string ParseAnswers(string text, int optionCount)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var max = (char)('A' + Math.Clamp(optionCount, 2, 6) - 1);
        var sb = new StringBuilder();
        foreach (var raw in text.ToUpperInvariant())
        {
            var ch = raw switch
            {
                'А' => 'A', 'В' => 'B', 'С' => 'C', 'Е' => 'E', 'Д' => 'D', 'Ф' => 'F',
                _ => raw,
            };
            if (ch >= 'A' && ch <= max) sb.Append(ch);
            else if (char.IsLetter(ch)) return "";   // notanish harf — bu javob emas (oddiy matn)
        }
        return sb.ToString();
    }

    // ==================================================================================
    //  5) YUBORISH VA NATIJA
    // ==================================================================================

    /// <summary>Tasdiqlandi — javoblar tekshiriladi, ball saqlanadi, natija ko'rsatiladi.</summary>
    public async Task SubmitAsync(IAppDbContext db, long chatId, CancellationToken ct)
    {
        var (session, test) = await SessionAsync(db, chatId, ct);
        if (session is null || test is null) { await NoSessionAsync(chatId, ct); return; }

        var now = NowStamp();
        if (string.CompareOrdinal(now, EndOf(test)) > 0)
        {
            db.TestBotSessions.Remove(session);
            await db.SaveChangesAsync(ct);
            await telegram.SendMessageAsync(chatId,
                $"⛔️ Afsuski, test vaqti tugadi ({Clock(test.EndAt)}) — javoblar qabul qilinmadi.", null, ct);
            return;
        }

        var studentId = session.StudentId;
        var external = studentId.Length == 0;   // markazdan tashqari ishtirokchi (test kodi bilan kirgan)
        var answers = Pad(session.Answers, test.QuestionCount);
        var correct = CountCorrect(answers, test.AnswerKey);
        var submittedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        string takerName;

        if (external)
        {
            // Qayta topshirishning oldini olamiz (chat bo'yicha — bu odamning Student yozuvi yo'q).
            var existingExt = await db.ExternalTestScores
                .FirstOrDefaultAsync(s => s.TestResultId == test.Id && s.ChatId == chatId, ct);
            if (existingExt is not null)
            {
                db.TestBotSessions.Remove(session);
                await db.SaveChangesAsync(ct);
                await telegram.SendMessageAsync(chatId, "ℹ️ Siz bu testni allaqachon topshirgansiz.", null, ct);
                return;
            }

            takerName = session.ExternalName.Length > 0 ? session.ExternalName : "Ishtirokchi";
            var phone = await db.BotUsers.AsNoTracking()
                .Where(u => u.ChatId == chatId).Select(u => u.Phone).FirstOrDefaultAsync(ct) ?? "";

            db.ExternalTestScores.Add(new ExternalTestScore
            {
                TestResultId = test.Id, ChatId = chatId, FullName = takerName, Phone = phone,
                Score = correct, Answers = answers, SubmittedAt = submittedAt, Source = "bot",
            });
        }
        else
        {
            // Qayta topshirishning oldini olamiz (parallel urinish / ikki marta bosish).
            var existing = await db.TestScores
                .FirstOrDefaultAsync(s => s.TestResultId == test.Id && s.StudentId == studentId, ct);
            if (existing is not null && OnlineTestService.IsStudentSubmission(existing.Source))
            {
                db.TestBotSessions.Remove(session);
                await db.SaveChangesAsync(ct);
                await telegram.SendMessageAsync(chatId, "ℹ️ Siz bu testni allaqachon topshirgansiz.", null, ct);
                return;
            }

            if (existing is null)
                db.TestScores.Add(new TestScore
                {
                    TestResultId = test.Id, StudentId = studentId, Score = correct,
                    Answers = answers, SubmittedAt = submittedAt, Source = "bot",
                });
            else
            {
                existing.Score = correct;
                existing.Answers = answers;
                existing.SubmittedAt = submittedAt;
                existing.Source = "bot";
            }

            takerName = await db.Students.AsNoTracking()
                .Where(s => s.Id == studentId).Select(s => s.FullName).FirstOrDefaultAsync(ct) ?? "";
        }

        db.TestBotSessions.Remove(session);
        await db.SaveChangesAsync(ct);

        // O'rin — MARKAZDAGI va MARKAZDAN TASHQARI ishtirokchilar BIRGA sanaladi (ishtirokchiga
        // "nechanchiman" degan savolga to'g'ri javob shu; hisobotdagi ikki ro'yxat bundan mustaqil).
        var scores = await db.TestScores.AsNoTracking()
            .Where(s => s.TestResultId == test.Id).Select(s => s.Score).ToListAsync(ct);
        var extScores = await db.ExternalTestScores.AsNoTracking()
            .Where(s => s.TestResultId == test.Id).Select(s => s.Score).ToListAsync(ct);
        var all = scores.Concat(extScores).ToList();
        var rank = all.Count(x => x > correct) + 1;

        await telegram.SendMessageAsync(chatId,
            BuildResultText(test, answers, correct, takerName, submittedAt, rank, all.Count,
                header: "✅ <b>Javoblaringiz qabul qilindi!</b>"),
            null, ct, "HTML");
    }

    /// <summary>Natija xabari — ball, foiz, baho, o'rin. Javob kaliti FAQAT test vaqti tugagach ochiladi.</summary>
    private static string BuildResultText(
        TestResult test, string answers, int correct, string studentName, string submittedAt,
        int? rank, int? total, string header)
    {
        var count = Math.Max(1, test.QuestionCount);
        var percent = (int)Math.Round(correct * 100.0 / count);
        var (grade, emoji) = Grade(percent);
        var finished = string.CompareOrdinal(NowStamp(), EndOf(test)) > 0;

        var sb = new StringBuilder();
        sb.Append(header).Append("\n\n")
          .Append($"📝 <b>{Esc(test.Name)}</b>\n")
          .Append($"👤 {Esc(studentName)} · 📅 {Human(test.Date)}\n")
          .Append("━━━━━━━━━━━━━━━━━━\n")
          .Append($"✔️ To'g'ri javoblar: <b>{correct} / {test.QuestionCount}</b>\n")
          .Append($"📊 Natija: <b>{percent}%</b>   {Bar(percent)}\n")
          .Append($"{emoji} Baho: <b>{grade}</b>\n");
        if (submittedAt.Length >= 16) sb.Append($"🕒 Yuborildi: {submittedAt[11..16]}\n");
        if (rank is not null && total is > 0)
            sb.Append($"🏅 O'rin: <b>{rank} / {total}</b>{(finished ? "" : " (hozircha)")}\n");

        if (finished && test.AnswerKey.Length == test.QuestionCount)
        {
            sb.Append("\n📋 <b>Savollar bo'yicha:</b>\n<code>").Append(Review(answers, test.AnswerKey)).Append("</code>\n");
        }
        else
        {
            sb.Append($"\n⏳ Batafsil tahlil test yakunlangach ({Clock(test.EndAt)}) ko'rsatiladi.\n");
        }
        sb.Append("\nNatijangiz o'qituvchingizga ham yuborildi.");
        return sb.ToString();
    }

    /// <summary>To'g'ri javoblar soni ('-' — javobsiz, xato hisoblanadi).</summary>
    public static int CountCorrect(string answers, string key)
    {
        var n = Math.Min(answers.Length, key.Length);
        var c = 0;
        for (var i = 0; i < n; i++)
            if (answers[i] != '-' && answers[i] == key[i]) c++;
        return c;
    }

    // ==================================================================================
    //  Ko'rinish (chizish) yordamchilari
    // ==================================================================================

    /// <summary>Javob varaqasi matni (tugmali rejim) — progress + to'ldirilgan javoblar jadvali.</summary>
    private static string SheetText(TestResult test, TestBotSession session)
    {
        var answers = Pad(session.Answers, test.QuestionCount);
        var done = answers.Count(c => c != '-');
        var q = Math.Clamp(session.Current, 0, test.QuestionCount - 1);
        var sb = new StringBuilder();
        sb.Append($"📝 <b>{Esc(test.Name)}</b>\n")
          .Append($"⏰ {Clock(test.StartAt)} – {Clock(test.EndAt)}\n")
          .Append($"{Bar(done * 100 / Math.Max(1, test.QuestionCount))}  <b>{done}/{test.QuestionCount}</b>\n\n")
          .Append($"<code>{AnswerGrid(answers)}</code>\n\n");
        sb.Append(done == test.QuestionCount
            ? "✅ Barcha javoblar to'ldirildi — «Yakunlash» tugmasini bosing."
            : $"👉 <b>{q + 1}-savol</b> javobini tanlang:");
        return sb.ToString();
    }

    /// <summary>Javob varaqasi tugmalari: variantlar + navigatsiya + yakunlash.</summary>
    private static object SheetKeyboard(TestResult test, TestBotSession session)
    {
        var answers = Pad(session.Answers, test.QuestionCount);
        var q = Math.Clamp(session.Current, 0, test.QuestionCount - 1);
        var current = answers[q];

        var options = new List<object>();
        for (var i = 0; i < test.OptionCount; i++)
        {
            var letter = Letter(i);
            options.Add(new
            {
                text = current == letter ? $"✅ {letter}" : letter.ToString(),
                callback_data = $"{CbAnswer}{q}:{letter}",
            });
        }

        var nav = new List<object>();
        if (q > 0) nav.Add(new { text = "⬅️ Oldingi", callback_data = $"{CbGoto}{q - 1}" });
        nav.Add(new { text = $"{q + 1}/{test.QuestionCount}", callback_data = $"{CbGoto}{q}" });
        if (q < test.QuestionCount - 1) nav.Add(new { text = "Keyingi ➡️", callback_data = $"{CbGoto}{q + 1}" });

        return new
        {
            inline_keyboard = new object[][]
            {
                options.ToArray(),
                nav.ToArray(),
                new object[] { new { text = "✅ Yakunlash va yuborish", callback_data = CbFinish } },
                new object[] { new { text = "✖️ Bekor qilish", callback_data = CbCancel } },
            },
        };
    }

    /// <summary>Javob varaqasi xabarini JOYIDA yangilaydi (bo'lmasa — yangi xabar yuboradi).</summary>
    private async Task RedrawAsync(
        IAppDbContext db, long chatId, TestBotSession session, TestResult test, CancellationToken ct)
    {
        var text = SheetText(test, session);
        var kb = SheetKeyboard(test, session);
        if (session.MessageId != 0
            && await telegram.EditMessageTextAsync(chatId, session.MessageId, text, kb, ct, "HTML"))
            return;

        var msgId = await telegram.SendMessageReturningIdAsync(chatId, text, kb, ct, "HTML");
        if (msgId is not null)
        {
            session.MessageId = msgId.Value;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Javoblar jadvali (qatorda 5 ta savol): <c> 1.A   2.C   3.–</c></summary>
    private static string AnswerGrid(string answers)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < answers.Length; i++)
        {
            var ch = answers[i] == '-' ? '–' : answers[i];
            sb.Append($"{i + 1,3}.{ch}");
            sb.Append((i + 1) % 5 == 0 ? "\n" : "  ");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Test yakunlangach: har savol bo'yicha ✅/❌ va to'g'ri javob.</summary>
    private static string Review(string answers, string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            var mine = i < answers.Length ? answers[i] : '-';
            var ok = mine == key[i];
            sb.Append($"{i + 1,3}. {(mine == '-' ? '–' : mine)} {(ok ? "✅" : $"❌ → {key[i]}")}");
            sb.Append((i + 1) % 3 == 0 ? "\n" : "   ");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Bar(int percent)
    {
        var filled = Math.Clamp(percent, 0, 100) * 10 / 100;
        return new string('▰', filled) + new string('▱', 10 - filled);
    }

    private static (string Label, string Emoji) Grade(int percent) => percent switch
    {
        >= 90 => ("A'lo", "🥇"),
        >= 75 => ("Yaxshi", "👍"),
        >= 60 => ("Qoniqarli", "🙂"),
        _ => ("Yana harakat qiling", "📕"),
    };

    private static string SampleAnswers(int count, int options)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++) sb.Append(Letter(i % options));
        return sb.ToString().ToLowerInvariant();
    }

    private static string NumberedSample(int options)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 4; i++) sb.Append($"{i + 1}{char.ToLowerInvariant(Letter(i % options))} ");
        return sb.Append("...").ToString();
    }

    // ==================================================================================
    //  Ma'lumot yordamchilari
    // ==================================================================================

    /// <summary>Shu chatga bog'langan o'quvchilar (id, F.I.SH) — barqaror tartibda (callback indeksi uchun).</summary>
    private static async Task<List<(string Id, string Name)>> ChatStudentsAsync(
        IAppDbContext db, long chatId, CancellationToken ct)
    {
        var ids = await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.ChatId == chatId && r.StudentId != "")
            .Select(r => r.StudentId).Distinct().ToListAsync(ct);
        if (ids.Count == 0) return new List<(string, string)>();

        return (await db.Students.AsNoTracking()
                .Where(s => ids.Contains(s.Id) && !s.IsArchived)
                .Select(s => new { s.Id, s.FullName }).ToListAsync(ct))
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => (s.Id, s.FullName))
            .ToList();
    }

    private record TestItem(TestResult Test, string StudentId, string StudentName, int StudentIndex, bool Submitted);

    /// <summary>Shu chat o'quvchilarining FAOL guruhlaridagi onlayn testlar — yaqin 7 kun ichida
    /// tugaganlar va kelgusi/hozirgi testlar (sana bo'yicha yangisi tepada, ko'pi bilan 10 ta).</summary>
    private static async Task<List<TestItem>> AvailableTestsAsync(
        IAppDbContext db, List<(string Id, string Name)> students, CancellationToken ct)
    {
        var ids = students.Select(s => s.Id).ToList();
        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => ids.Contains(sg.StudentId) && sg.IsActive && sg.Status != "frozen")
            .Select(sg => new { sg.StudentId, sg.GroupId })
            .ToListAsync(ct);
        if (memberships.Count == 0) return new List<TestItem>();

        var groupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        // GroupOpen=false — "FAQAT ONLAYN" test: guruhga E'LON QILINMAYDI, faqat kod bilan ishlanadi.
        var tests = await db.TestResults.AsNoTracking()
            .Where(t => t.Mode == "online" && t.GroupOpen && groupIds.Contains(t.GroupId))
            .ToListAsync(ct);
        if (tests.Count == 0) return new List<TestItem>();

        var testIds = tests.Select(t => t.Id).ToList();
        var scored = (await db.TestScores.AsNoTracking()
                .Where(s => testIds.Contains(s.TestResultId) && ids.Contains(s.StudentId))
                .Select(s => new { s.TestResultId, s.StudentId }).ToListAsync(ct))
            .Select(x => (x.TestResultId, x.StudentId)).ToHashSet();

        var cutoff = AppClock.Now.AddDays(-7).ToString("yyyy-MM-ddTHH:mm");
        var result = new List<TestItem>();
        foreach (var m in memberships)
        {
            var idx = students.FindIndex(s => s.Id == m.StudentId);
            if (idx < 0) continue;
            foreach (var t in tests.Where(t => t.GroupId == m.GroupId))
            {
                if (string.CompareOrdinal(EndOf(t), cutoff) < 0) continue;  // ancha oldin tugagan
                result.Add(new TestItem(t, m.StudentId, students[idx].Name, idx,
                    scored.Contains((t.Id, m.StudentId))));
            }
        }
        return result
            .OrderByDescending(x => x.Test.Date)
            .ThenByDescending(x => x.Test.CreatedAt)
            .Take(10)
            .ToList();
    }

    private static (string Icon, string Label) StateOf(TestItem it)
    {
        if (it.Submitted) return ("✅", "topshirilgan");
        var now = NowStamp();
        if (string.CompareOrdinal(now, StartOf(it.Test)) < 0) return ("⏳", "hali boshlanmagan");
        if (string.CompareOrdinal(now, EndOf(it.Test)) > 0) return ("⛔️", "vaqti tugagan");
        return ("🟢", "hozir ochiq");
    }

    private async Task<(TestBotSession? Session, TestResult? Test)> SessionAsync(
        IAppDbContext db, long chatId, CancellationToken ct)
    {
        var s = await db.TestBotSessions.FirstOrDefaultAsync(x => x.ChatId == chatId, ct);
        if (s is null) return (null, null);
        var t = await db.TestResults.AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.TestResultId, ct);
        return (s, t);
    }

    private async Task NoSessionAsync(long chatId, CancellationToken ct) =>
        await telegram.SendMessageAsync(chatId,
            "Test sessiyasi topilmadi (eskirgan bo'lishi mumkin).\n«" + TestButtonText + "» tugmasi orqali qaytadan oching.",
            null, ct);

    private static string NowStamp() => AppClock.Now.ToString("yyyy-MM-ddTHH:mm");
    private static string StartOf(TestResult t) => t.StartAt.Length >= 16 ? t.StartAt[..16] : t.Date + "T00:00";
    private static string EndOf(TestResult t) => t.EndAt.Length >= 16 ? t.EndAt[..16] : t.Date + "T23:59";
    private static char Letter(int i) => (char)('A' + Math.Clamp(i, 0, 5));
    private static string Pad(string s, int n) =>
        s.Length == n ? s : s.Length > n ? s[..n] : s + new string('-', n - s.Length);
    private static int FirstEmpty(string answers)
    {
        var i = answers.IndexOf('-');
        return i;
    }
    private static string Esc(string s) => WebUtility.HtmlEncode(s ?? "");
    private static string Short(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
    /// <summary>"2026-07-22" → "22.07.2026"</summary>
    private static string Human(string iso) =>
        iso.Length >= 10 ? $"{iso[8..10]}.{iso[5..7]}.{iso[..4]}" : iso;
    /// <summary>"2026-07-22T09:30" → "09:30"</summary>
    private static string Clock(string iso) => iso.Length >= 16 ? iso[11..16] : "—";
}
