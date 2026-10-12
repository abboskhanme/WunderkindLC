namespace WunderkindLC.Application.Dtos;

// CRM upgrade U4a — test result entry (docs/SPEC-crm-upgrade.md P4.2). JSON camelCase.

/// <summary>One grid row: <c>solved = null</c> clears the result.</summary>
public record TestScoreBatchRow(string StudentId, decimal? Solved);

/// <summary><c>PUT …/test-results/{id}/scores/batch</c>.</summary>
public record TestScoreBatchRequest(List<TestScoreBatchRow>? Rows);

/// <summary>A row that was NOT saved (never clamped) with the Uzbek reason.</summary>
public record TestScoreBatchError(string StudentId, string Message);

/// <summary>Batch answer: the fresh detail (valid rows saved) + the rejected rows.</summary>
public record TestScoreBatchResult(TestResultDetailDto Detail, List<TestScoreBatchError> Errors);

/// <summary>One test of the centre overview (<c>GET api/admin/test-results/overview</c>).
/// <paramref name="QuestionCount"/> 0 = old test (<paramref name="MaxScore"/> is the denominator).</summary>
public record TestOverviewRowDto(
    string TestId, string Name, string Kind, string Date, string GroupId, string GroupName, string TeacherName,
    int QuestionCount, decimal MaxScore, int Scored, int Members, double? AvgPercent, DateTime? ResultsSentAt);

/// <summary><c>POST …/test-results/{id}/send-results</c>. <c>resend = true</c> sends every scored result again.</summary>
public record SendTestResultsRequest(bool? Resend = null);

/// <summary>Send answer: Telegram messages queued, students without a reachable parent chat, and students whose
/// result was already sent unchanged.</summary>
public record SendTestResultsResultDto(int Queued, int NoTelegram, int SkippedUnchanged);
