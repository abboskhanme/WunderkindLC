namespace WunderkindLC.Application.Services;

/// <summary>
/// Room FLOOR (CRM Phase 5, <c>Room.Floor</c>) — pure rules, tested (<c>RoomFloorTests</c>). The dated schedule
/// groups room columns under floor headers («1-qavat», …, «Qavat ko'rsatilmagan»), floors ascending.
/// </summary>
public static class RoomFloors
{
    public const int MinFloor = -2;
    public const int MaxFloor = 50;

    public const string InvalidMessage = "Qavat −2 dan 50 gacha bo'lishi kerak";
    public const string UnknownLabel = "Qavat ko'rsatilmagan";

    public static bool IsValid(int? floor) => floor is null || floor is >= MinFloor and <= MaxFloor;

    /// <summary>
    /// The floor after an edit: <paramref name="clear"/> → <c>null</c>; a value → that value; no value →
    /// UNCHANGED (an old client that does not send the field must not wipe it — same rule as <c>IsActive</c>,
    /// <c>RoomRules.ResolveIsActive</c>).
    /// </summary>
    public static int? Resolve(int? current, int? requested, bool clear) =>
        clear ? null : requested ?? current;

    /// <summary>Header label: «2-qavat», «-1-qavat», or «Qavat ko'rsatilmagan».</summary>
    public static string Label(int? floor) => floor is null ? UnknownLabel : $"{floor}-qavat";
}
