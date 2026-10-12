using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// "Topshiriqlar" modulining Telegram ko'rinishi — YAGONA joy: yangi topshiriq xabari ham,
/// kunlik eslatma ham, bot callback javobi ham shu yerdagi matn/tugmalardan foydalanadi
/// (tugmalar ikki joyda ayrilib ketmasin).
/// </summary>
public static class WorkTaskTelegram
{
    /// <summary>Callback data: "wtdone:{taskId}" — mas'ul topshiriqni bajarildi deb belgilaydi.</summary>
    public const string CallbackDone = "wtdone:";

    /// <summary>Muhimlik yorlig'i (bot matni va tugmalar uchun).</summary>
    public static string Priority(int p) => p switch
    {
        3 => "🔴 Shoshilinch",
        2 => "🟠 Yuqori",
        0 => "⚪️ Past",
        _ => "🔵 O'rta",
    };

    /// <summary>"yyyy-MM-dd" → "dd.MM.yyyy" (bo'sh bo'lsa "muddatsiz").</summary>
    public static string Day(string? iso) =>
        string.IsNullOrWhiteSpace(iso) || iso.Length < 10
            ? "muddatsiz"
            : $"{iso[8..10]}.{iso[5..7]}.{iso[..4]}";

    /// <summary>Muddat matni: kun (+soat) va kechikkan bo'lsa ogohlantirish.</summary>
    public static string Due(WorkTask t, string today)
    {
        if (string.IsNullOrWhiteSpace(t.DueDate)) return "muddatsiz";
        var s = Day(t.DueDate);
        if (!string.IsNullOrWhiteSpace(t.DueTime)) s += $" {t.DueTime}";
        if (string.CompareOrdinal(t.DueDate, today) < 0) return s + " ⚠️ (kechikdi)";
        if (t.DueDate == today) return s + " (bugun)";
        return s;
    }

    /// <summary>YANGI topshiriq biriktirilgandagi xabar.</summary>
    public static string NewTaskText(WorkTask t, string boardTitle, string authorName, string today)
    {
        var lines = new List<string>
        {
            "🆕 Sizga yangi topshiriq berildi",
            "",
            $"📌 {t.Title}",
        };
        if (!string.IsNullOrWhiteSpace(t.Description)) lines.Add(t.Description.Trim());
        lines.Add("");
        lines.Add($"🗂 Doska: {boardTitle}");
        lines.Add($"⏰ Muddat: {Due(t, today)}");
        lines.Add($"❗️ Muhimlik: {Priority(t.Priority)}");
        if (!string.IsNullOrWhiteSpace(authorName)) lines.Add($"👤 Bergan: {authorName}");
        return string.Join("\n", lines);
    }

    /// <summary>Bitta topshiriq uchun "bajardim" tugmasi.</summary>
    public static object DoneKeyboard(string taskId) => new
    {
        inline_keyboard = new[]
        {
            new object[] { new { text = "✅ Bajardim", callback_data = CallbackDone + taskId } },
        },
    };

    /// <summary>KUNLIK eslatma matni — bugungi va kechikkan topshiriqlar ro'yxati.</summary>
    public static string ReminderText(IReadOnlyList<WorkTask> tasks, string today)
    {
        var late = tasks.Where(t => !string.IsNullOrWhiteSpace(t.DueDate)
                                    && string.CompareOrdinal(t.DueDate, today) < 0).ToList();
        var now = tasks.Except(late).ToList();

        var lines = new List<string> { $"📋 Topshiriqlar eslatmasi ({Day(today)})", "" };
        if (late.Count > 0)
        {
            lines.Add($"⚠️ Muddati o'tgan — {late.Count} ta:");
            lines.AddRange(late.Select(t => $"  • {t.Title} ({Day(t.DueDate)})"));
            lines.Add("");
        }
        if (now.Count > 0)
        {
            lines.Add($"⏰ Bugun bajariladi — {now.Count} ta:");
            lines.AddRange(now.Select(t => $"  • {t.Title}"
                                           + (string.IsNullOrWhiteSpace(t.DueTime) ? "" : $" ({t.DueTime})")));
            lines.Add("");
        }
        lines.Add("Bajarganingizni quyidagi tugma orqali belgilang.");
        return string.Join("\n", lines);
    }

    /// <summary>Eslatma klaviaturasi — har topshiriq bitta tugma (bosilsa bajarildi bo'ladi).</summary>
    public static object ReminderKeyboard(IEnumerable<WorkTask> tasks) => new
    {
        inline_keyboard = tasks
            .Select(t => new object[]
            {
                new { text = "✅ " + Cut(t.Title), callback_data = CallbackDone + t.Id },
            })
            .ToArray(),
    };

    // ---------- TASDIQLASH (CRM Phase 5, topshiriqlar.md §8) ----------

    /// <summary>Callback data: "wtok:{taskId}" — topshiriq bergan xodim (yoki admin) bajarilganini TASDIQLAYDI.</summary>
    public const string CallbackApprove = "wtok:";

    /// <summary>Topshiriq bergan xodimga: mas'ul bajardi — tasdiqlang.</summary>
    public static string ApprovalRequestText(WorkTask t, string assigneeName) =>
        $"✅ {(string.IsNullOrWhiteSpace(assigneeName) ? "Mas'ul" : assigneeName)} topshiriqni bajardi: «{t.Title}». Tasdiqlang.";

    /// <summary>"Tasdiqlash" tugmasi.</summary>
    public static object ApproveKeyboard(string taskId) => new
    {
        inline_keyboard = new[]
        {
            new object[] { new { text = "✅ Tasdiqlash", callback_data = CallbackApprove + taskId } },
        },
    };

    /// <summary>Mas'ulga: topshiriq tasdiqlandi.</summary>
    public static string ApprovedText(WorkTask t) => $"👍 Topshiriq tasdiqlandi: «{t.Title}»";

    /// <summary>Mas'ulga: topshiriq sabab bilan qaytarildi.</summary>
    public static string ReturnedText(WorkTask t, string reason) => $"↩️ Topshiriq qaytarildi: «{t.Title}» — {reason}";

    /// <summary>Telegram tugma matni uzun bo'lmasligi uchun qisqartma.</summary>
    private static string Cut(string s, int max = 40) =>
        s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
