using System;
using System.Collections.Generic;

public static class UiEventQueue
{
    private static readonly Queue<Action> _queue = new();

    public static void Enqueue(Action action) => _queue.Enqueue(action);

    public static bool TryDequeue(out Action action) => _queue.TryDequeue(out action!);

    public static void Clear() => _queue.Clear();
}
