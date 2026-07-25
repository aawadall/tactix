using UnityEngine;

namespace Tactix.Game
{
    /// <summary>
    /// Spawns the whole game at startup. The scene only needs to exist; camera,
    /// board, and UI are all created in code.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (Object.FindFirstObjectByType<GameController>() != null) return;
            new GameObject("TactixGame").AddComponent<GameController>();
        }
    }
}
