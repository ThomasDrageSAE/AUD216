using UnityEngine;

/*
 * AssessedPrefabRegistry.cs
 *
 * Purpose: names the prefabs whose audio slots belong in the student progress report
 * even though the scene never holds one of them at rest.
 *
 * Used by: AudioProgressScanner.Scan()
 *
 * Why it exists: the scanner walks the components it can FIND - it asks the scene for
 * every MonoBehaviour and reflects over their serialized fields - and it deliberately
 * stops at any Unity object reference, because following prefabs, materials and
 * textures would drag the whole asset graph into the report. That boundary hides two
 * components a student is expected to author: BossZombieController, which the boss wave
 * spawns, and FishingRod, which only enters the scene when the player picks it up.
 * Their clip slots were invisible until this list existed.
 *
 * This component is the one door through that boundary, and it is a HAND-WRITTEN list
 * for exactly that reason: nothing is discovered, only what is dropped in here is
 * followed. Put it on the Audio Managers object (Tools > Audio > Fix Slot Surface adds
 * and fills it) and drag in the prefab ROOT - the scanner walks the root and all of its
 * children.
 *
 * A component type reached through this list is counted ONCE: the scanner records the
 * types it walked here and skips them when the same type turns up as a scene instance,
 * so the boss reads the same in the report before its wave and while it is alive.
 *
 * Runtime-safe: no UnityEditor API. The prefabs referenced here are ordinary asset
 * references from a scene component, so they ship in a build like any other prefab
 * reference and the in-game Ctrl+G report lists exactly what the editor window does.
 */
public class AssessedPrefabRegistry : MonoBehaviour
{
    [Tooltip("Prefab ROOTS whose audio slots belong in the progress report even though " +
             "the scene has no instance of them at rest - the boss zombie and the " +
             "fishing rod. The scanner walks each root and its children, and skips any " +
             "component type it found here when it later meets it as a scene instance, " +
             "so nothing is counted twice.")]
    public GameObject[] prefabs;
}
