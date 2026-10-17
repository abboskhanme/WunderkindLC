using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Abstractions;

/// <summary>Owner backlog 2026-10-17 (F3) — expense approval and staff payroll tables.</summary>
public partial interface IAppDbContext
{
    DbSet<ExpenseRequest> ExpenseRequests { get; }
    DbSet<StaffSalary> StaffSalaries { get; }
}
