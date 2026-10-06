using System;
using UnityEngine;   // InspectorName, to park retired ids
using System.Collections.Generic;

/*
 * AudioProfileIds.cs
 *
 * Purpose: Compile-checked identities for the audio profiles that are currently
 *          addressed by free-text name.
 * Used by: EnemyAudioManager, WeaponAudioManager, InteractionAudioManager,
 *          FootstepAudioManager, AudioProfileEnumMigration (editor)
 *
 * These are NOT the existing EnemyType / WeaponType / InteractionType enums. Those are
 * behaviour categories - several profiles legitimately share one value (SMG and Revolver
 * are both WeaponType.Gun; Cave Door and Light Switch are both SimpleInteraction), and
 * InteractionType additionally decides which playback path runs. They cannot serve as
 * identities without collapsing distinct profiles and breaking that branching.
 *
 * The ids below are one member per authored profile, so they can. Every enum starts at
 * Unassigned = 0: a profile that has not been migrated reads as "no id", which is what
 * lets the string and the id coexist during the changeover.
 *
 * Assigning an id does not retire the string. It registers the id's own name as an extra
 * spelling for that profile, so the sound still plays when the string field is mistyped,
 * and gives Tools/Audio/Migrate Profile Keys To Enums something to migrate toward.
 */

public enum EnemyId
{
    Unassigned = 0,
    Butcher,
    Yokai,
    Demon,
    Choul,
    RockGolem
}

public enum WeaponId
{
    Unassigned = 0,
    SMG,
    Revolver,
    Axe,
    Crossbow,
    // Appended, never inserted: WeaponId serializes as an int, so renumbering would
    // silently repoint every profile already authored on the WeaponAudioManager.
    Grenade,
    Minigun
}

public enum InteractionId
{
    Unassigned = 0,
    Firestick,
    Campfire,
    CaveDoor,
    ElectricalBox,
    LightSwitch,
    MixingDeskMusic,
    // Appended, never inserted: these are serialized as ints, so reordering would
    // silently repoint every profile already authored in the scene.
    // MushroomEating was named Mushroom until 2026-09-02. Renaming a MEMBER is safe -
    // the int is what serializes - and Tools/Interactions/Add Missing Audio Profiles
    // migrates the authored strings to match.
    MushroomEating,
    Well,
    WellSplash,
    // 2026-09-03 consolidation: ONE profile per prop. Tape1 and FlashlightOn were
    // RENAMED (safe - the int is what serializes) to Tape and Flashlight; the other
    // tape and flashlight members are retired, but must keep holding their ints, so
    // they are parked and hidden from the dropdown rather than deleted - re-using a
    // retired int someday would silently repoint any row still carrying it.
    [Obsolete("Replaced by the Turntable (2026-09-03)")] [InspectorName(null)] RetiredTape,
    [Obsolete("Consolidated into Tape (2026-09-03)")] [InspectorName(null)] RetiredTape2,
    [Obsolete("Consolidated into Tape (2026-09-03)")] [InspectorName(null)] RetiredTape3,
    [Obsolete("Consolidated into Tape (2026-09-03)")] [InspectorName(null)] RetiredTape4,
    Flashlight,
    [Obsolete("Consolidated into Flashlight (2026-09-03)")] [InspectorName(null)] RetiredFlashlightOff,
    WalkieTalkie,       // briefly renamed Turntable on 2026-09-03; restored the same day
    Cooking,
    Fishing,
    Turntable           // 2026-09-03: the studio deck - it replaced the TAPE recorder
}

public enum SurfaceId
{
    Unassigned = 0,
    Wood,
    Grass,
    Sand,
    Rock,
    Snow,
    Swamp,
    Water,
    Default
}

public static class AudioProfileIds
{
    private static readonly Dictionary<Type, Dictionary<int, string>> nameCache =
        new Dictionary<Type, Dictionary<int, string>>();

    /// <summary>True when an id has actually been assigned, rather than left at Unassigned.</summary>
    public static bool IsAssigned<TId>(TId id) where TId : struct, Enum
    {
        return Convert.ToInt32(id) != 0;
    }

    /// <summary>
    /// The id's declared name, via a value-to-name map built once per enum type.
    /// Deliberately NOT an index into Enum.GetNames: that only works while every enum
    /// stays contiguous from 0, and the day someone retires a member it would silently
    /// return the WRONG member's name - cross-wiring profiles instead of failing.
    /// </summary>
    public static string NameOf<TId>(TId id) where TId : struct, Enum
    {
        Type type = typeof(TId);
        if (!nameCache.TryGetValue(type, out Dictionary<int, string> names))
        {
            names = new Dictionary<int, string>();
            foreach (TId value in (TId[])Enum.GetValues(type))
            {
                names[Convert.ToInt32(value)] = value.ToString();
            }
            nameCache[type] = names;
        }

        return names.TryGetValue(Convert.ToInt32(id), out string name) ? name : id.ToString();
    }

    /// <summary>
    /// Reduces a name to letters and digits, lowercased, so an authored string and an
    /// enum member match despite spacing and capitalisation: "Rock Golem" and RockGolem,
    /// "Mixing Desk Music" and MixingDeskMusic.
    /// </summary>
    public static string Canonical(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;

        var builder = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Finds the id whose name matches an authored string, ignoring spacing and case.
    /// Returns false for anything unrecognised - the caller reports it rather than guessing.
    /// </summary>
    public static bool TryParse<TId>(string name, out TId id) where TId : struct, Enum
    {
        id = default;

        string canonical = Canonical(name);
        if (canonical.Length == 0) return false;

        foreach (TId candidate in (TId[])Enum.GetValues(typeof(TId)))
        {
            if (!IsAssigned(candidate)) continue;

            if (Canonical(NameOf(candidate)) == canonical)
            {
                id = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Every assigned member's name - used to report the accepted vocabulary.</summary>
    public static IEnumerable<string> AssignedNames<TId>() where TId : struct, Enum
    {
        foreach (TId candidate in (TId[])Enum.GetValues(typeof(TId)))
        {
            if (IsAssigned(candidate)) yield return NameOf(candidate);
        }
    }
}
