using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/*
 * AudioWiringValidator.cs
 *
 * Purpose: Reports the wiring mistakes that make a correctly-authored sound never play -
 *          a profile name no gameplay object asks for, a surface with no profile, a
 *          profile with no clips in it at all.
 * Used by: a dev-only RuntimeInitializeOnLoadMethod (logs each problem as a warning),
 *          and the "Validate wiring" button in AudioProgressReportWindow.
 *
 * This complements AudioProgressScanner rather than repeating it. The scanner answers
 * "how much has the student filled in"; this answers "will what they filled in be
 * reached". A profile can be 100% complete in the report and still be silent because the
 * name on the zombie prefab says "Ghoul" and the profile says "Choul".
 *
 * Everything here is read-only and tolerant: every manager is allowed to be absent, and
 * a missing component produces one line of output rather than an exception. It runs on
 * the loaded scene, so it sees exactly what will play.
 *
 * Findings are advisory. Some are expected - the surface vocabulary check reports
 * "default" until someone authors a fallback profile - so the output is a to-do list for
 * the audio implementer, not a pass/fail gate.
 */
public static class AudioWiringValidator
{
    /// <summary>
    /// Surfaces SurfaceDetector and TerrainTextureDetector can return. A footstep on a
    /// surface with no profile is silent, and "default" is what every unmapped surface
    /// falls back to, so it needs a profile more than any of the others.
    /// </summary>
    private static readonly string[] RequiredSurfaces =
    {
        "grass", "sand", "rock", "snow", "swamp", "wood", "water", "default"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ReportOnLoad()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;

        List<string> problems = Validate();
        if (problems.Count == 0)
        {
            Debug.Log("[AudioWiringValidator] No wiring problems found.");
            return;
        }

        foreach (string problem in problems)
        {
            Debug.LogWarning($"[AudioWiringValidator] {problem}");
        }
    }

    public static List<string> Validate()
    {
        var problems = new List<string>();

        try
        {
            ValidateEnemyWiring(problems);
            ValidateWeaponWiring(problems);
            ValidateInteractionWiring(problems);
            ValidateSurfaceCoverage(problems);
            ValidateProfileKeysAndContent(problems);
        }
        catch (Exception e)
        {
            // A validator that throws is worse than one that misses something.
            problems.Add($"Validation stopped early: {e.GetType().Name}: {e.Message}");
        }

        return problems;
    }

    // ---- Check 1: zombie prefabs against enemy profiles -----------------------

    private static void ValidateEnemyWiring(List<string> problems)
    {
        var manager = UnityEngine.Object.FindAnyObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            problems.Add("No EnemyAudioManager in the scene - enemy sounds cannot play.");
            return;
        }

        List<string> profileNames = ProfileNames(manager, "enemyProfiles", "enemyName");
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (GameObject prefab in CollectWaveZombiePrefabs())
        {
            var zombie = prefab.GetComponentInChildren<ZombieAI>(true);
            if (zombie == null)
            {
                problems.Add($"Wave zombie prefab '{prefab.name}' has no ZombieAI - it will make no sound.");
                continue;
            }

            string requested = AudioProfileKeys.Normalize(GetFieldValue(zombie, "enemyAudioProfileName") as string);

            if (string.IsNullOrEmpty(requested))
            {
                problems.Add($"Zombie prefab '{prefab.name}' has an empty Enemy Audio Profile Name - " +
                             $"set it to one of: {Join(profileNames)}.");
                continue;
            }

            used.Add(requested);

            if (!ContainsName(profileNames, requested))
            {
                problems.Add($"Zombie prefab '{prefab.name}' asks for enemy profile '{requested}', " +
                             $"which does not exist. EnemyAudioManager has: {Join(profileNames)}.");
            }
        }

        foreach (string profileName in profileNames)
        {
            if (!used.Contains(profileName))
            {
                problems.Add($"Enemy profile '{profileName}' is not used by any zombie prefab in " +
                             "WaveManager - sounds authored into it will never be heard.");
            }
        }
    }

    /// <summary>
    /// Walks WaveManager's weeks -> nights -> waves -> zombiePrefabs. Reflection rather
    /// than a public accessor because the whole hierarchy is private and serialized, and
    /// this is a read-only observer that should not widen WaveManager's surface.
    /// </summary>
    private static IEnumerable<GameObject> CollectWaveZombiePrefabs()
    {
        var seen = new HashSet<GameObject>();

        foreach (var waveManager in UnityEngine.Object.FindObjectsByType<WaveManager>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (object week in AsList(GetFieldValue(waveManager, "weeks")))
            foreach (object night in AsList(GetFieldValue(week, "nights")))
            foreach (object wave in AsList(GetFieldValue(night, "waves")))
            foreach (object entry in AsList(GetFieldValue(wave, "zombiePrefabs")))
            {
                if (entry is GameObject prefab && prefab != null && seen.Add(prefab))
                {
                    yield return prefab;
                }
            }
        }
    }

    // ---- Check 2: scene weapons against weapon profiles -----------------------

    private static void ValidateWeaponWiring(List<string> problems)
    {
        var manager = UnityEngine.Object.FindAnyObjectByType<WeaponAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            problems.Add("No WeaponAudioManager in the scene - weapon sounds cannot play.");
            return;
        }

        List<string> profileNames = ProfileNames(manager, "m_WeaponProfiles", "weaponName");

        foreach (var weapon in UnityEngine.Object.FindObjectsByType<WeaponController>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string requested = AudioProfileKeys.Normalize(GetFieldValue(weapon, "weaponName") as string);

            if (string.IsNullOrEmpty(requested))
            {
                problems.Add($"Weapon '{weapon.gameObject.name}' has an empty Weapon Name - " +
                             $"set it to one of: {Join(profileNames)}.");
            }
            else if (!ContainsName(profileNames, requested))
            {
                problems.Add($"Weapon '{weapon.gameObject.name}' asks for weapon profile '{requested}', " +
                             $"which does not exist. WeaponAudioManager has: {Join(profileNames)}.");
            }
        }
    }

    // ---- Check 3: interaction scripts against interaction profiles -------------

    /// <summary>
    /// Not in the original work order, but the same failure mode and the same fix: the
    /// interaction scripts each carry a serialized profile name string, and a mismatch
    /// silences a door or a campfire exactly as it silences a zombie.
    /// </summary>
    private static void ValidateInteractionWiring(List<string> problems)
    {
        var manager = UnityEngine.Object.FindAnyObjectByType<InteractionAudioManager>(FindObjectsInactive.Include);
        if (manager == null) return; // Optional system - not every scene has interactions.

        List<string> profileNames = ProfileNames(manager, "interactionProfiles", "interactionName");

        foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == null || behaviour is InteractionAudioManager) continue;

            // InteractionSystemTMP overwrites its serialized audioProfileName in Start
            // (power boxes become "Electrical Box", music systems "Mixing Desk Music"),
            // so judging the serialized value would pass and fail the wrong things.
            if (behaviour is InteractionSystemTMP) continue;

            if (!(GetFieldValue(behaviour, "audioProfileName") is string raw)) continue;

            string requested = AudioProfileKeys.Normalize(raw);
            if (string.IsNullOrEmpty(requested))
            {
                problems.Add($"{behaviour.GetType().Name} on '{behaviour.gameObject.name}' has an empty " +
                             $"audio profile name - set it to one of: {Join(profileNames)}.");
            }
            else if (!ContainsName(profileNames, requested))
            {
                problems.Add($"{behaviour.GetType().Name} on '{behaviour.gameObject.name}' asks for " +
                             $"interaction profile '{requested}', which does not exist. " +
                             $"InteractionAudioManager has: {Join(profileNames)}.");
            }
        }
    }

    // ---- Check 4: surface vocabulary coverage ---------------------------------

    private static void ValidateSurfaceCoverage(List<string> problems)
    {
        var manager = UnityEngine.Object.FindAnyObjectByType<Audio.FootstepAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            problems.Add("No FootstepAudioManager in the scene - footsteps cannot play.");
            return;
        }

        List<string> profileNames = ProfileNames(manager, "surfaceProfiles", "surfaceType");

        foreach (string surface in RequiredSurfaces)
        {
            if (!ContainsName(profileNames, surface))
            {
                problems.Add($"No footstep profile for surface '{surface}' - the player will walk " +
                             "silently there. SurfaceDetector can return every one of: " +
                             $"{Join(new List<string>(RequiredSurfaces))}.");
            }
        }
    }

    // ---- Check 5: duplicate, empty and clipless profiles ----------------------

    private static void ValidateProfileKeysAndContent(List<string> problems)
    {
        CheckProfileArray<EnemyAudioManager>(problems, "enemyProfiles", "enemyName", "enemyId", "enemy");
        CheckProfileArray<WeaponAudioManager>(problems, "m_WeaponProfiles", "weaponName", "weaponId", "weapon");
        CheckProfileArray<InteractionAudioManager>(problems, "interactionProfiles", "interactionName", "interactionId", "interaction");
        CheckProfileArray<Audio.FootstepAudioManager>(problems, "surfaceProfiles", "surfaceType", "surfaceId", "surface");
    }

    private static void CheckProfileArray<TManager>(List<string> problems, string arrayField,
                                                    string nameField, string idField, string label)
        where TManager : Component
    {
        foreach (TManager manager in UnityEngine.Object.FindObjectsByType<TManager>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;

            foreach (object profile in AsList(GetFieldValue(manager, arrayField)))
            {
                index++;
                if (profile == null) continue;

                string name = AudioProfileKeys.Normalize(GetFieldValue(profile, nameField) as string);

                if (string.IsNullOrEmpty(name))
                {
                    problems.Add($"{typeof(TManager).Name} {label} profile #{index} has no name - " +
                                 "nothing can ever ask for it.");
                }
                else if (!seen.Add(name))
                {
                    problems.Add($"{typeof(TManager).Name} has two {label} profiles named '{name}' - " +
                                 "only the first will ever play.");
                }

                if (!HasAnyClip(profile, 0))
                {
                    string shown = string.IsNullOrEmpty(name) ? $"#{index}" : $"'{name}'";
                    problems.Add($"{typeof(TManager).Name} {label} profile {shown} has no audio clips " +
                                 "assigned at all - it is wired up but silent.");
                }

                CheckIdMatchesName(problems, profile, idField, name, typeof(TManager).Name, label, index);
                CheckInteractionTypeRequirements(problems, profile, name, index);
            }
        }
    }

    /// <summary>
    /// A profile carrying both a name and an id answers to two spellings. If they drift
    /// apart - someone renames the profile but leaves the id, or vice versa - the sound
    /// keeps playing under one key while gameplay may be asking with the other, which is
    /// the confusing half-working state the ids were meant to prevent.
    /// </summary>
    private static void CheckIdMatchesName(List<string> problems, object profile, string idField,
                                           string name, string managerName, string label, int index)
    {
        if (string.IsNullOrEmpty(name)) return;

        object raw = GetFieldValue(profile, idField);
        if (!(raw is Enum id)) return;

        // Unassigned is "no id", not a mismatch.
        if (Convert.ToInt32(raw) == 0) return;

        string idName = id.ToString();
        if (AudioProfileIds.Canonical(idName) == AudioProfileIds.Canonical(name)) return;

        problems.Add($"{managerName} {label} profile '{name}' has id {idName}, which does not " +
                     $"match its name. It now answers to both '{name}' and '{idName}' - rename " +
                     "the profile or fix the id so there is one spelling.");
    }

    /// <summary>
    /// Interaction profiles branch on interactionType, and each branch needs specific
    /// clips - "any clip anywhere" cannot see that. A Mixing Desk profile carrying
    /// leftover fire clips but no speaker audio passes the generic check and is
    /// permanently silent; this catches exactly that.
    /// </summary>
    private static void CheckInteractionTypeRequirements(List<string> problems, object profile,
                                                         string name, int index)
    {
        if (!(profile is InteractionAudioManager.InteractionSoundProfile interaction)) return;

        string shown = string.IsNullOrEmpty(name) ? $"#{index}" : $"'{name}'";

        switch (interaction.interactionType)
        {
            case InteractionAudioManager.InteractionType.MixingDesk:
                if (interaction.leftSpeakerAudio == null || interaction.rightSpeakerAudio == null)
                    problems.Add($"Interaction profile {shown} is a Mixing Desk but is missing " +
                                 "left/right speaker clips - the desk plays nothing when switched on.");
                break;

            case InteractionAudioManager.InteractionType.ElectricalBox:
                if (interaction.leverSound == null)
                    problems.Add($"Interaction profile {shown} is an Electrical Box with no lever sound.");
                if (interaction.sparksSound == null)
                    problems.Add($"Interaction profile {shown} is an Electrical Box with no sparks loop - " +
                                 "the sparks effect will be silent.");
                break;

            case InteractionAudioManager.InteractionType.Firesticks:
            case InteractionAudioManager.InteractionType.Campfire:
                if (interaction.fireLoop == null)
                    problems.Add($"Interaction profile {shown} is a fire type with no fire loop - " +
                                 "igniting it produces no ongoing sound.");
                break;

            case InteractionAudioManager.InteractionType.SimpleInteraction:
                if (interaction.interactionSound == null)
                    problems.Add($"Interaction profile {shown} is a Simple Interaction with no " +
                                 "interaction sound assigned.");
                break;
        }
    }

    /// <summary>True if any AudioClip anywhere inside this profile is assigned.</summary>
    private static bool HasAnyClip(object instance, int depth)
    {
        if (instance == null || depth > 4) return false;

        foreach (FieldInfo field in SerializedFields(instance.GetType()))
        {
            object value;
            try { value = field.GetValue(instance); }
            catch { continue; }

            if (value is AudioClip clip) { if (clip != null) return true; continue; }

            if (value is IList list)
            {
                foreach (object element in list)
                {
                    if (element is AudioClip elementClip) { if (elementClip != null) return true; }
                    else if (element != null && IsPlainClass(element.GetType()) && HasAnyClip(element, depth + 1)) return true;
                }
                continue;
            }

            if (value != null && IsPlainClass(value.GetType()) && HasAnyClip(value, depth + 1)) return true;
        }

        return false;
    }

    // ---- Shared helpers -------------------------------------------------------

    private static List<string> ProfileNames(Component manager, string arrayField, string nameField)
    {
        var names = new List<string>();

        foreach (object profile in AsList(GetFieldValue(manager, arrayField)))
        {
            if (profile == null) continue;

            string name = AudioProfileKeys.Normalize(GetFieldValue(profile, nameField) as string);
            if (!string.IsNullOrEmpty(name)) names.Add(name);
        }

        return names;
    }

    private static bool ContainsName(List<string> names, string candidate)
    {
        foreach (string name in names)
        {
            if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string Join(List<string> names)
    {
        return names.Count == 0 ? "(none)" : string.Join(", ", names);
    }

    private static IEnumerable AsList(object value)
    {
        return value as IList ?? (IEnumerable)Array.Empty<object>();
    }

    /// <summary>Reads a field by name, searching base types too. Null when absent.</summary>
    private static object GetFieldValue(object instance, string fieldName)
    {
        if (instance == null) return null;

        for (Type type = instance.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            FieldInfo field = type.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            if (field == null) continue;

            try { return field.GetValue(instance); }
            catch { return null; }
        }

        return null;
    }

    private static IEnumerable<FieldInfo> SerializedFields(Type type)
    {
        for (Type current = type; current != null && current != typeof(MonoBehaviour)
             && current != typeof(object); current = current.BaseType)
        {
            foreach (FieldInfo field in current.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsStatic) continue;
                if (field.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false)) continue;

                yield return field;
            }
        }
    }

    private static bool IsPlainClass(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string)) return false;
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;
        return type.IsDefined(typeof(SerializableAttribute), false);
    }
}
