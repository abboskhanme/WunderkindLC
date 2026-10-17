namespace WunderkindLC.Domain;

// Phase 4 of the CRM upgrade (docs/SPEC-crm-upgrade.md §5 P4.1) — rating settings.
// Model config: WunderkindLC.Infrastructure/Data/AppDbContext.CrmRating.cs.
// Rules: .claude/rules/rating.md.

public partial class CenterMeta
{
    /// <summary>Share of the TEST average in the rating score, percent (0..100). The rest (100 − x) is the
    /// journal ball normalised to the group's best. Default 60 (owner decision, 2026-10-11).
    /// <para>⚠️ The migration must fill existing rows with 60, not 0 — 0 is a VALID value ("ball only").</para></summary>
    public int RatingTestWeight { get; set; } = 60;

    /// <summary>Share of HOMEWORK (journal «Uyga vazifa»: done = 1, partly = ½, not done = 0; unmarked skipped) in the
    /// rating score, percent (0..100, owner decision 2026-10-17). The ball gets 100 − test − homework. Default 0 — the
    /// rating stays exactly as before until the owner sets a weight (SPEC-backlog-2026-10-17 A3).</summary>
    public int RatingHomeworkWeight { get; set; }

    /// <summary>Monthly TOP/BOTTOM list size N (1..10, default 5); the real k = min(N, ⌊n/2⌋) — U4d.</summary>
    public int RatingTopBottomCount { get; set; } = 5;

    /// <summary>TOP/BOTTOM scope: <c>group</c> (default — per group) | <c>centre</c> (one centre-wide list) — U4d.</summary>
    public string RatingTopBottomScope { get; set; } = "group";
}

// U4a (test entry, SPEC P4.1) — result-only tests: kind + "sent to parents" stamp.
// QuestionCount (existing column) = «Savollar soni»; for every test created from now on MaxScore = QuestionCount and a
// score is the SOLVED count, so the rating engine's percent = Score / MaxScore stays the same formula.
public partial class TestResult
{
    /// <summary>Test kind: <c>monthly</c> («Oylik») | <c>interim</c> («Oraliq»). Required on create; existing rows
    /// are backfilled to <c>interim</c> by the data SQL (<c>docs/migrations/Phase4a.sql</c>). Does not change the
    /// rating weight.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>When results were last queued to parents («Ota-onalarga yuborish»), centre time. Null = never.</summary>
    public DateTime? ResultsSentAt { get; set; }

    /// <summary>User id of who pressed «Ota-onalarga yuborish» last.</summary>
    public string? ResultsSentById { get; set; }
}
