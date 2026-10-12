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

    /// <summary>Monthly TOP/BOTTOM list size N (1..10, default 5); the real k = min(N, ⌊n/2⌋) — U4d.</summary>
    public int RatingTopBottomCount { get; set; } = 5;

    /// <summary>TOP/BOTTOM scope: <c>group</c> (default — per group) | <c>centre</c> (one centre-wide list) — U4d.</summary>
    public string RatingTopBottomScope { get; set; } = "group";
}
