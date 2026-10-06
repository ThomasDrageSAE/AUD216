using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/*
 * AudioProfileKeys.cs
 *
 * Purpose: Shared key handling for the string-keyed audio profile managers.
 * Used by: EnemyAudioManager, WeaponAudioManager, InteractionAudioManager,
 *          FootstepAudioManager
 *
 * The audio managers look their profiles up by free-text names authored in the
 * inspector ("Zombie", "grass", "Mixing Desk Music"). That is the single biggest source
 * of silent failure for a student implementing sound: a trailing space or a capital
 * letter produces no sound, no error, and nothing to debug.
 *
 * This does not remove the strings - migrating to enums is a separate, larger job - it
 * makes them forgiving and makes a miss LOUD:
 *
 *   - keys are trimmed and compared case-insensitively,
 *   - duplicate and empty keys are reported when the lookup is built,
 *   - a failed lookup warns once per key, listing what IS available.
 *
 * The warn-once matters: a missing footstep surface would otherwise log on every step.
 */
public static class AudioProfileKeys
{
    private static readonly HashSet<string> warnedKeys = new HashSet<string>();

    /// <summary>Creates a lookup that ignores case and surrounding whitespace.</summary>
    public static Dictionary<string, TProfile> CreateLookup<TProfile>()
    {
        return new Dictionary<string, TProfile>(System.StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Normalises an authored key. Null and whitespace collapse to empty.</summary>
    public static string Normalize(string key)
    {
        return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
    }

    /// <summary>
    /// Adds a profile under its authored key, reporting the two authoring mistakes that
    /// otherwise fail silently: a blank key, and two profiles claiming the same one.
    /// </summary>
    public static void AddProfile<TProfile>(Dictionary<string, TProfile> lookup, string key,
                                            TProfile profile, Object context, string managerName)
    {
        string normalized = Normalize(key);

        if (string.IsNullOrEmpty(normalized))
        {
            Debug.LogWarning($"[{managerName}] A sound profile has no name and can never be played. " +
                             "Give it the name the gameplay code asks for.", context);
            return;
        }

        if (lookup.ContainsKey(normalized))
        {
            Debug.LogWarning($"[{managerName}] Two sound profiles are both named '{normalized}'. " +
                             "Only the first is used; rename or remove the duplicate.", context);
            return;
        }

        lookup.Add(normalized, profile);
    }

    /// <summary>
    /// True when this profile is the one the lookup actually serves under its authored
    /// name. A duplicate that AddProfile rejected fails this test - which is what keeps
    /// a rejected profile from smuggling itself back in through its id alias.
    /// </summary>
    public static bool Owns<TProfile>(Dictionary<string, TProfile> lookup, string key,
                                      TProfile profile) where TProfile : class
    {
        if (lookup == null) return false;

        string normalized = Normalize(key);
        return normalized.Length > 0 &&
               lookup.TryGetValue(normalized, out TProfile canonical) &&
               ReferenceEquals(canonical, profile);
    }

    /// <summary>
    /// Registers the id's own name as an extra spelling for a profile, so an assigned id
    /// keeps the sound reachable even when the authored string is mistyped. Silent when
    /// the id is unassigned or the name is already claimed - the authored string wins.
    /// Callers must alias only profiles that pass Owns() for their authored name.
    /// </summary>
    public static void AddIdAlias<TProfile, TId>(Dictionary<string, TProfile> lookup, TId id,
                                                 TProfile profile) where TId : struct, System.Enum
    {
        if (lookup == null || !AudioProfileIds.IsAssigned(id)) return;

        string alias = AudioProfileIds.NameOf(id);
        if (!lookup.ContainsKey(alias)) lookup.Add(alias, profile);
    }

    /// <summary>
    /// Looks a profile up, warning once per unknown key with the list of names that do
    /// exist - which is almost always enough to spot the typo.
    /// </summary>
    /// <param name="noisyLog">
    /// Marks a successful lookup as high-frequency so the Ctrl+J monitor hides it
    /// (footsteps). A MISS is never marked: it is rare, it is the row the student needs,
    /// and hiding it would defeat the monitor.
    /// </param>
    public static bool TryGetProfile<TProfile>(Dictionary<string, TProfile> lookup, string key,
                                               out TProfile profile, Object context, string managerName,
                                               bool noisyLog = false)
    {
        profile = default;
        if (lookup == null) return false;

        string normalized = Normalize(key);
        if (string.IsNullOrEmpty(normalized)) return false;

        if (lookup.TryGetValue(normalized, out profile))
        {
            AudioEventLog.Record(AudioEventKind.Play, managerName, normalized, noisyLog);
            return true;
        }

        // Logged on every miss, not just the first: the warn-once below goes quiet
        // for a key it has already reported, but the student watching the monitor
        // needs to see the miss each time they retry the action.
        AudioEventLog.Record(AudioEventKind.Miss, managerName, normalized);

        string warnKey = $"{managerName}:{normalized}";
        if (warnedKeys.Add(warnKey))
        {
            Debug.LogWarning($"[{managerName}] No sound profile named '{normalized}'. " +
                             $"Available: {(lookup.Count == 0 ? "(none)" : string.Join(", ", lookup.Keys))}", context);
        }

        return false;
    }

    /// <summary>
    /// Clears the warn-once memory both at startup and on every scene load.
    /// RuntimeInitializeOnLoadMethod alone fires once per play session, not per
    /// SceneManager.LoadScene - so after an in-game restart the second run looked
    /// clean while the same sounds were still missing.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetWarnings()
    {
        warnedKeys.Clear();

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        warnedKeys.Clear();
    }
}
