using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>
/// BIR MARTALIK KO'CHIRISH YORDAMCHISI — kalitlar bazadan <c>.env</c> ga o'tayotganda.
///
/// <para>Ilgari integratsiya kalitlari <c>CenterMeta</c> ustunlarida saqlanardi (admin ularni
/// Sozlamalar sahifasidan kiritardi). <c>RemoveSecretsFromDb</c> migratsiyasi o'sha ustunlarni
/// O'CHIRADI — ya'ni qiymatlar yo'qoladi. Shu sababli migratsiyadan <b>OLDIN</b> shu yordamchi
/// ishga tushadi: bazada hali qiymat qolgan bo'lsa, uni tayyor <c>.env</c> qatorlari ko'rinishida
/// logga chiqaradi, admin nusxa olib serverdagi <c>.env</c> ga joylashi uchun.</para>
///
/// <para>DIQQAT: qiymatlar TO'LIQ holda logga tushadi — bu ATAYIN (aks holda kalitlar butunlay
/// yo'qolardi). Nusxa olgach loglarni tozalash kerak (<c>docker compose down &amp;&amp; up -d</c> yoki
/// log faylini o'chirish). Ustunlar o'chgach bu blok boshqa hech qachon ishlamaydi.</para>
/// </summary>
public static class LegacySecretRescue
{
    /// <summary>Eski ustun → mos <c>.env</c> o'zgaruvchisi.</summary>
    private static readonly (string Column, string EnvKey)[] Map =
    [
        ("TelegramBotToken", "TELEGRAM_BOT_TOKEN"),
        ("FcmServiceAccountJson", "FCM_SERVICE_ACCOUNT_JSON"),
        ("GeminiApiKey", "GEMINI_API_KEY"),
        ("AzureSpeechKey", "AZURE_SPEECH_KEY"),
        ("AzureSpeechRegion", "AZURE_SPEECH_REGION"),
        ("EskizEmail", "ESKIZ_EMAIL"),
        ("EskizPassword", "ESKIZ_PASSWORD"),
        ("TurnstileUsername", "TURNSTILE_USERNAME"),
        ("TurnstilePassword", "TURNSTILE_PASSWORD"),
    ];

    /// <summary>
    /// Migratsiyadan OLDIN chaqiriladi. Eski ustunlar hali mavjud va ichida qiymat bo'lsa —
    /// <c>.env</c> qatorlarini logga chiqaradi. Ustunlar allaqachon o'chirilgan bo'lsa (yoki baza
    /// yangi) — hech narsa qilmaydi. Har qanday xato yutiladi: bu blok ishga tushishni to'smaydi.
    /// </summary>
    /// <returns><c>true</c> — bazaga ulanib tekshirib bo'lindi (qayta chaqirish shart emas);
    /// <c>false</c> — baza hali tayyor emas, migratsiyaning keyingi urinishida yana chaqirilsin.
    /// Shu bilan kalitlar logga BIR MARTA chiqadi.</returns>
    public static bool ReportPendingSecrets(DatabaseFacade database, ILogger logger)
    {
        try
        {
            var conn = database.GetDbConnection();
            var opened = false;
            if (conn.State != ConnectionState.Open) { conn.Open(); opened = true; }
            try
            {
                var present = ExistingColumns(conn);
                var columns = Map.Where(m => present.Contains(m.Column)).ToList();
                if (columns.Count == 0) return true;   // allaqachon ko'chirilgan — normal holat

                var values = ReadRow(conn, columns.Select(c => c.Column).ToList());
                if (values.Count == 0) return true;     // CenterMeta qatori yo'q

                var lines = new List<string>();
                foreach (var (column, envKey) in columns)
                {
                    if (!values.TryGetValue(column, out var v) || string.IsNullOrWhiteSpace(v)) continue;
                    // JSON kabi ko'p qatorli qiymatlar .env'da BIR qatorda bo'lishi kerak.
                    lines.Add($"{envKey}={v.Replace("\r", "").Replace("\n", "")}");
                }
                if (lines.Count == 0) return true;      // ustunlar bor, lekin bo'sh — jim o'tamiz

                var sb = new StringBuilder();
                sb.AppendLine("==================== KALITLAR BAZADAN .env GA KO'CHMOQDA ====================");
                sb.AppendLine("Quyidagi kalitlar bazada (CenterMeta) saqlangan edi. Ular endi FAQAT .env dan");
                sb.AppendLine("o'qiladi, ustunlar esa shu migratsiyada BAZADAN O'CHIRILADI.");
                sb.AppendLine("Quyidagi qatorlarni serverdagi .env fayliga ko'chiring va qayta ishga tushiring:");
                sb.AppendLine("    docker compose up -d");
                sb.AppendLine("-----------------------------------------------------------------------------");
                foreach (var line in lines) sb.AppendLine(line);
                sb.AppendLine("-----------------------------------------------------------------------------");
                sb.AppendLine("Nusxa olgach LOGLARNI TOZALANG (bu qiymatlar log faylida qolib ketmasin).");
                sb.AppendLine("=============================================================================");
                logger.LogWarning("{Block}", sb.ToString());
                return true;
            }
            finally
            {
                if (opened) conn.Close();
            }
        }
        catch (Exception ex)
        {
            // Odatda baza hali ko'tarilmagan — keyingi urinishda qayta tekshiriladi.
            logger.LogWarning(ex, "[secrets] Eski kalitlarni o'qib bo'lmadi — migratsiya davom etadi");
            return false;
        }
    }

    private static HashSet<string> ExistingColumns(System.Data.Common.DbConnection conn)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select column_name from information_schema.columns " +
            "where table_name = 'CenterMeta' and table_schema = current_schema()";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    private static Dictionary<string, string?> ReadRow(System.Data.Common.DbConnection conn, List<string> columns)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        // Ustun nomlari KODDAN keladi (Map) — foydalanuvchi kiritmaydi, SQL injection yo'q.
        cmd.CommandText = $"select {string.Join(", ", columns.Select(c => $"\"{c}\""))} from \"CenterMeta\" limit 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return result;
        for (var i = 0; i < columns.Count; i++)
            result[columns[i]] = r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();
        return result;
    }
}
