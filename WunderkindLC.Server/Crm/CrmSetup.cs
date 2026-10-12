namespace WunderkindLC.Server.Crm;

/// <summary>
/// CRM upgrade wiring (docs/SPEC-crm-upgrade.md §0.2). <c>Program.cs</c> calls the five
/// <c>AddCrm&lt;Name&gt;</c> methods once in the DI section and <see cref="RunStartupAsync"/> once after
/// migrations and the existing seeds. Each phase edits ONLY its own <c>Crm&lt;Name&gt;Setup.cs</c>.
/// </summary>
public static class CrmSetup
{
    public static async Task RunStartupAsync(IServiceProvider services)
    {
        await CrmMessagingSetup.RunStartupAsync(services);
        await CrmAttendanceSetup.RunStartupAsync(services);
        await CrmRatingSetup.RunStartupAsync(services);
        await CrmWorkspaceSetup.RunStartupAsync(services);
        await CrmAccountsSetup.RunStartupAsync(services);
    }
}
