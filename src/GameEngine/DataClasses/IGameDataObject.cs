namespace GameEngine.DataClasses;

public interface IGameDataObject
{
    string Id { get; }
    int SchemaVersion { get; }

    static abstract string SchemaResourceName { get; }
}
