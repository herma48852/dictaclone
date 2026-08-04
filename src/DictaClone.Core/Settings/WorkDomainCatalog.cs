namespace DictaClone.Core.Settings;

public static class WorkDomainCatalog
{
    public static IReadOnlyList<string> GetPromptTerms(
        WorkDomainPreset preset) => preset switch
        {
            WorkDomainPreset.General => [],
            WorkDomainPreset.SoftwareDevelopment =>
            ["API", "C#", ".NET", "GitHub", "JSON", "Kubernetes", "SQL"],
            WorkDomainPreset.Business =>
            ["KPI", "OKR", "roadmap", "stakeholder", "quarterly review"],
            WorkDomainPreset.Academic =>
            ["abstract", "citation", "hypothesis", "methodology", "peer review"],
            _ => throw new ArgumentOutOfRangeException(
                nameof(preset),
                preset,
                "Unknown work-domain preset."),
        };

    public static string GetDisplayName(WorkDomainPreset preset) => preset switch
    {
        WorkDomainPreset.General => "General",
        WorkDomainPreset.SoftwareDevelopment => "Software development",
        WorkDomainPreset.Business => "Business",
        WorkDomainPreset.Academic => "Academic",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };
}
