using System.Globalization;

namespace WunderkindLC.Application.Services;

/// <summary>
/// TEST RESULT ENTRY rules (CRM upgrade U4a, docs/SPEC-crm-upgrade.md P4.1–P4.2; rules <c>.claude/rules/tests.md</c>).
/// Pure — no database, tested in <c>TestScoreBatchTests</c>.
///
/// <para>Owner decision (2026-10-11): no question authoring — only RESULTS per group. A test has a name, a date, a
/// KIND (monthly / interim) and «Savollar soni» (number of questions); the teacher enters ONE number per student —
/// how many were SOLVED. Percent = solved ÷ questions × 100. So for every test created from now on
/// <c>MaxScore = QuestionCount</c> and the stored score is the solved count: the rating engine
/// (<c>RatingFormula.Percent(score, max)</c>) reads it unchanged.</para>
///
/// <para>Old tests (<c>QuestionCount = 0</c>) keep <c>MaxScore</c> as the denominator; their grid column is «Ball»
/// with range 0..MaxScore — still whole numbers only (number-inputs.md).</para>
/// </summary>
public static class TestEntryRules
{
    public const string Monthly = "monthly";
    public const string Interim = "interim";

    /// <summary>All kinds in display order.</summary>
    public static readonly string[] Kinds = [Monthly, Interim];

    public const int MinQuestions = 1;
    public const int MaxQuestions = 200;

    /// <summary>Most rows one batch save accepts (a group is far smaller — a guard, not a limit users meet).</summary>
    public const int MaxBatchRows = 500;

    public const string NameRequired = "Test nomi kerak";
    public const string KindRequired = "Test turini tanlang";
    public const string QuestionsRange = "Savollar soni 1 dan 200 gacha bo'lsin";
    public const string NotMember = "O'quvchi bu guruh a'zosi emas";
    public const string TooManyRows = "Bir so'rovda ko'pi bilan 500 ta qator saqlanadi";
    public const string DateInvalid = "Sana noto'g'ri (YYYY-MM-DD)";

    public static bool IsKind(string? kind) => kind is Monthly or Interim;

    /// <summary>«Oylik» / «Oraliq»; "" for an unknown (old, not yet backfilled) value.</summary>
    public static string KindLabel(string? kind) => kind switch
    {
        Monthly => "Oylik",
        Interim => "Oraliq",
        _ => "",
    };

    /// <summary>Normalised kind ("Monthly " → "monthly"); null when empty/unknown.</summary>
    public static string? NormalizeKind(string? kind)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        return IsKind(k) ? k : null;
    }

    /// <summary>Validates a NEW test: name, kind and «Savollar soni» are required. Null = valid.</summary>
    public static string? ValidateNew(string? name, string? kind, int? questionCount)
    {
        if (string.IsNullOrWhiteSpace(name)) return NameRequired;
        if (NormalizeKind(kind) is null) return KindRequired;
        if (questionCount is not (>= MinQuestions and <= MaxQuestions)) return QuestionsRange;
        return null;
    }

    /// <summary>Strict ISO date (<c>yyyy-MM-dd</c>) — the month filter and the rating compare dates as strings.</summary>
    public static bool IsIsoDate(string? date) =>
        !string.IsNullOrWhiteSpace(date)
        && DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>Strict month key <c>yyyy-MM</c>; null when invalid.</summary>
    public static string? NormalizeMonth(string? month)
    {
        if (string.IsNullOrWhiteSpace(month)) return null;
        var m = month.Trim();
        return DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? m
            : null;
    }

    /// <summary>Inclusive ISO range of a month key (<c>"2026-10"</c> → 2026-10-01..2026-10-31).</summary>
    public static (string From, string To) MonthRange(string month)
    {
        var from = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (from.ToString("yyyy-MM-dd"), from.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd"));
    }

    /// <summary>The largest whole score a row may hold: «Savollar soni» when set, else ⌊MaxScore⌋ (old tests).</summary>
    public static int MaxSolved(int questionCount, decimal maxScore) =>
        questionCount > 0 ? questionCount : (int)Math.Max(0, Math.Floor(maxScore));

    /// <summary>«Natija 0 dan {n} gacha butun son bo'lsin».</summary>
    public static string RangeMessage(int max) => $"Natija 0 dan {max} gacha butun son bo'lsin";

    /// <summary>
    /// One grid cell: <c>null</c> clears; otherwise a WHOLE number in 0..max. Out-of-range or fractional values are
    /// REJECTED, never clamped (SPEC: a value 31 for 30 questions is red and not saved). Null = valid.
    /// </summary>
    public static string? ValidateSolved(decimal? solved, int max)
    {
        if (solved is null) return null;
        var v = solved.Value;
        if (v != decimal.Truncate(v) || v < 0 || v > max) return RangeMessage(max);
        return null;
    }

    /// <summary>
    /// Lowering «Savollar soni» below an existing score is refused (no silent clamp of entered results):
    /// «{n} ta o'quvchining natijasi yangi savollar sonidan katta». Null = allowed.
    /// </summary>
    public static string? ValidateQuestionLowering(int newCount, IEnumerable<decimal> scores)
    {
        var over = scores.Count(s => s > newCount);
        return over > 0 ? $"{over} ta o'quvchining natijasi yangi savollar sonidan katta" : null;
    }

    /// <summary>score ÷ max × 100 (0..100), one decimal; null when max ≤ 0. Same clamp as the rating formula.</summary>
    public static double? Percent(decimal score, decimal max)
    {
        var p = Rating.RatingFormula.Percent(score, max);
        return p is null ? null : Rating.RatingFormula.Round1(p.Value);
    }

    /// <summary>Score as typed (no trailing zeros): 25 → "25", 37.5 → "37.5".</summary>
    public static string FormatScore(decimal score) => score.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Competition ranking ("1, 2, 2, 4") of the scored rows by score desc — the detail page, the «O'rin» column and
    /// the {orin} message token use the same numbers. Unscored rows are not ranked.
    /// </summary>
    public static Dictionary<string, int> Ranks(IEnumerable<(string StudentId, decimal Score)> scored)
    {
        var ordered = scored.OrderByDescending(x => x.Score).ToList();
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        decimal? prev = null;
        var rank = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (prev is null || ordered[i].Score != prev.Value) rank = i + 1;
            prev = ordered[i].Score;
            ranks[ordered[i].StudentId] = rank;
        }
        return ranks;
    }
}
