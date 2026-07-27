using GameEngine.DataClasses;

namespace GameEngine;

public static class GameDataExtensions
{
    public static T Lookup<T>(this IEnumerable<T> source, string id) where T : IGameDataObject
        => source.First(x => x.Id == id);
}
