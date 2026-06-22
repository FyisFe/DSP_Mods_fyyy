namespace DashboardOverhaul;

/// <summary>
/// Self-contained bilingual helper — no external dependency.
/// Returns the Chinese string on zh-CN clients, English everywhere else.
/// </summary>
public static class Loc
{
    public static string L(string zh, string en) => Localization.isZHCN ? zh : en;
}
