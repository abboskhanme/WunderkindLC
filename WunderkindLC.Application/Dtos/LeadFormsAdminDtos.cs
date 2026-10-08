namespace WunderkindLC.Application.Dtos;

/// <summary>
/// <c>PATCH api/admin/level-tests/{id}/active</c> va <c>PATCH api/admin/lead-forms/{id}/active</c>
/// tanasi — forma/testni O'CHIRISH o'rniga nofaol qilish (topshiruvlar tarixi va lidlar kanali
/// saqlanadi; CRM-7). Klient shartnomasi: <c>levelTests.ts</c> / <c>leadForms.ts</c> → <c>setActive</c>.
/// </summary>
public record FormActiveRequest(bool IsActive);
