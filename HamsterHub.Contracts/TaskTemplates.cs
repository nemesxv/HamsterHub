namespace HamsterHub.Contracts;

public sealed record TaskTemplate(string Id, string Image, string Frequency, int Points = 5)
{
    public string TitleKey => "Template_" + Id;
    public string StepsKey => "TemplateSteps_" + Id;
}

// Stable, allowlisted illustration identifiers shared by website, API and Android.
public static class TaskTemplates
{
    public static IReadOnlyList<TaskTemplate> All { get; } =
    [
        new("teeth", "teeth.svg", "Daily"), new("toys", "toys.svg", "Daily"),
        new("books", "books.svg", "Daily"), new("dress", "dress.svg", "Daily"),
        new("feeding", "feeding.webp", "Daily"), new("water", "water.webp", "Daily"),
        new("cleaning", "cleaning.webp", "Weekly"), new("playing", "playing.webp", "Daily")
    ];
    public static TaskTemplate? Find(string? id) => All.FirstOrDefault(item => item.Id == id);
    public static bool IsValid(string? id) => string.IsNullOrEmpty(id) || Find(id) is not null;
    public static string? ImagePath(string? id) => Find(id) is { } item ? "/images/tasks/" + item.Image : null;
}
