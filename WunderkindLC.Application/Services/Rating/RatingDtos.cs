using System.Diagnostics.CodeAnalysis;

namespace WunderkindLC.Application.Services.Rating;

// JSON contracts of the rating API (docs/SPEC-crm-upgrade.md P4.3). Serialised camelCase by ASP.NET.

public sealed record RatingPeriodDto(string Kind, string Key, string Label, string From, string To)
{
    public static RatingPeriodDto Of(RatingPeriod p) => new(p.Kind, p.Key, p.Label, p.From, p.To);
}

// ---------- GET api/admin/rating/centre ----------

public sealed record RatingCentreGroupDto(string Id, string Name, int? Rank, double? Score);

public sealed record RatingCentreItemDto(
    int Rank, string StudentId, string FullName, double Score, double? TestAvg, int TestCount, int Ball,
    List<RatingCentreGroupDto> Groups, double? HomeworkAvg = null);

/// <summary><paramref name="Total"/> — ranked students matching the search (before paging).</summary>
public sealed record RatingCentreDto(
    RatingPeriodDto Period, int TestWeight, int Total, int Page, int PageSize, List<RatingCentreItemDto> Items,
    int HomeworkWeight = 0);

// ---------- GET api/admin/rating/groups/{groupId} ----------

public sealed record RatingGroupHeaderDto(string Id, string Name, string CourseName, string TeacherName);

/// <summary>Rank/score null — the student is in the group but has no test score and no ball in the period.</summary>
public sealed record RatingGroupItemDto(
    int? Rank, string StudentId, string FullName, double? Score, double? TestAvg, int TestCount, int Ball,
    double? BallNorm, double? HomeworkAvg = null);

public sealed record RatingGroupDto(
    RatingPeriodDto Period, int TestWeight, RatingGroupHeaderDto Group, int Ranked, List<RatingGroupItemDto> Items,
    int HomeworkWeight = 0);

// ---------- GET api/admin/rating/students/{id} · GET api/student/rating/summary ----------

/// <summary>Centre place. <paramref name="PrevRank"/> — the centre rank in the previous period of the same kind.</summary>
public sealed record RatingCentrePlaceDto(int? Rank, int Total, double? Score, int? PrevRank);

/// <summary><paramref name="Total"/> — ranked students of the group in the period.</summary>
public sealed record RatingBlockGroupDto(
    string GroupId, string GroupName, string CourseName, int? Rank, int Total, double? Score, double? TestAvg,
    int TestCount, int Ball, double? BallNorm, double? HomeworkAvg = null);

/// <summary>The student's centre-level components: T over all tests, total ball and the configured weight.</summary>
/// <paramref name="HomeworkAvg"/>/<paramref name="HomeworkWeight"/> — homework points over all groups and its weight
/// (0 = not part of the score — SPEC-backlog A3).
public sealed record RatingComponentsDto(
    double? TestAvg, int TestCount, int Ball, int TestWeight, double? HomeworkAvg = null, int HomeworkWeight = 0);

public sealed record RatingHistoryPointDto(string Month, int? CentreRank, int CentreTotal, double? Score, double? TestAvg);

/// <summary>One test of the student in the period. <paramref name="Solved"/>/<paramref name="Questions"/> = score /
/// max score (after U4a: solved count / number of questions). <paramref name="Kind"/> — «monthly»/«interim»
/// once U4a adds it, "" until then.</summary>
public sealed record RatingTestRowDto(
    string TestId, string Name, string Kind, string Date, string GroupName, decimal Solved, decimal Questions,
    double? Percent, int RankInTest, int Takers);

public sealed record RatingAttendanceDto(int Held, int Attended, int? Percent);

/// <summary>The "rich but uncluttered" student rating block (admin profile «Reyting» tab and the cabinet).
/// Non-positional on purpose: <see cref="StudentRatingSummaryDto"/> extends it via the record copy constructor.</summary>
public record StudentRatingBlockDto
{
    public string StudentId { get; init; } = "";
    public required RatingPeriodDto Period { get; init; }
    public required RatingCentrePlaceDto Centre { get; init; }
    public List<RatingBlockGroupDto> Groups { get; init; } = new();
    public required RatingComponentsDto Components { get; init; }
    /// <summary>Last 12 months (oldest first), ending with the period's end month.</summary>
    public List<RatingHistoryPointDto> History { get; init; } = new();
    /// <summary>Tests in the period, newest first.</summary>
    public List<RatingTestRowDto> Tests { get; init; } = new();
    public required RatingAttendanceDto Attendance { get; init; }
}

public sealed record RatingTopRowDto(int Rank, string FullName, double Score, bool IsMe);

public sealed record RatingGroupTopDto(string GroupId, string GroupName, List<RatingTopRowDto> Rows);

/// <summary>Cabinet variant: the block + group and centre top lists (top 10 + the student's own row when
/// outside the top 10). Other students appear only with name, rank and score — no ids.</summary>
public sealed record StudentRatingSummaryDto : StudentRatingBlockDto
{
    [SetsRequiredMembers]
    public StudentRatingSummaryDto(StudentRatingBlockDto block) : base(block) { }

    public List<RatingGroupTopDto> GroupTop { get; init; } = new();
    public List<RatingTopRowDto> CentreTop { get; init; } = new();
}

// ---------- GET/PUT api/admin/rating/settings ----------

/// <param name="HomeworkWeight">Homework share, % (0..100, test + homework ≤ 100; the ball gets the rest). In a PUT
/// <c>null</c> = unchanged (a cached old client must not silently reset it to 0).</param>
public sealed record RatingSettingsDto(int TestWeight, int TopBottomCount, string TopBottomScope, int? HomeworkWeight = null);
