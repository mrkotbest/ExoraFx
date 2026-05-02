namespace ExoraFx.Api.Configuration;

public sealed class StorageSettings
{
    public const string SectionName = "Storage";

    public string DatabasePath { get; init; } = "state.db";
}
