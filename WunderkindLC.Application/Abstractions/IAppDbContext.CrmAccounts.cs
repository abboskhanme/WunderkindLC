using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Abstractions;

/// <summary>CRM upgrade Phase 6 «Accounts» (docs/SPEC-crm-upgrade.md §7).</summary>
public partial interface IAppDbContext
{
    /// <summary>Encrypted student passwords (<c>CredentialVault</c>). Never in backup/MCP/logs/audit.</summary>
    DbSet<StudentCredential> StudentCredentials { get; }
}
