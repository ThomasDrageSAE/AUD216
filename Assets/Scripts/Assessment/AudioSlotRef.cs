using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;

/*
 * A reference to one slot the audio progress report lists - not the row, the PLACE: the
 * component or asset that serializes it, the object and field (or list and index) that
 * hold it, so a tool can read it and write it back. The walk in AudioProgressScanner
 * hands these out through WalkSlots, and the same walk records the report's rows, so a
 * slot the report lists is a slot a tool can reach, and no other.
 *
 * Two of these exist: AudioSlotRef for an AudioClip, AudioRoutingRef for an
 * AudioMixerGroup (a profile's routing field, or an AudioSource's output group).
 *
 * Set() writes the value and nothing else. The caller records Undo for a scene host,
 * marks a prefab or asset host dirty and saves it - that is the tool's job, and where
 * the "assets written" log lives (Editor/AudioHandoutTools).
 */

/// <summary>Where a slot's host lives, which decides how a write is persisted.</summary>
public enum AudioHostKind
{
    /// <summary>A component in the open scene: Undo.RecordObject, the owner saves.</summary>
    Scene,
    /// <summary>A component on a prefab asset: SetDirty + SavePrefabAsset(PrefabRoot).</summary>
    Prefab,
    /// <summary>A ScriptableObject such as a MusicStateDefinition: SetDirty + SaveAssets.</summary>
    Asset
}

/// <summary>One AudioClip slot, with enough to read it and write it.</summary>
public sealed class AudioSlotRef
{
    /// <summary>The component or ScriptableObject that serializes the slot. Undo / dirty target.</summary>
    public UnityEngine.Object Host;
    /// <summary>For a Prefab host: the prefab asset's root GameObject (the save target). Null otherwise.</summary>
    public GameObject PrefabRoot;
    public AudioHostKind HostKind;

    /// <summary>The report section this slot is listed under (before FinishGroups' disambiguation).</summary>
    public string GroupName;
    /// <summary>The section's component type name, as AudioGroupReport.TypeName.</summary>
    public string TypeName;
    /// <summary>The section's GameObject / prefab / asset name, as AudioGroupReport.ObjectName.</summary>
    public string ObjectName;
    /// <summary>The row label, the same text the report prints (TrimCategory applied).</summary>
    public string Label;

    /// <summary>The object holding the field - the host itself or a nested [Serializable] instance - or the IList holding the element.</summary>
    public object Container;
    /// <summary>The AudioClip field on Container. Null for a list element and for a direct AudioSource.</summary>
    public FieldInfo Field;
    /// <summary>The element index in Container when it is an IList; -1 otherwise.</summary>
    public int Index = -1;
    /// <summary>For the report's direct AudioSources (the waterfall and its kin): the source whose .clip is the slot.</summary>
    public AudioSource Source;

    public AudioClip Clip
    {
        get
        {
            if (Source != null) return Source.clip;
            if (Field != null) return Field.GetValue(Container) as AudioClip;
            if (Container is IList list && Index >= 0 && Index < list.Count) return list[Index] as AudioClip;
            return null;
        }
    }

    /// <summary>Writes the clip. No Undo, no dirtying, no saving - the caller owns those.</summary>
    public void Set(AudioClip clip)
    {
        if (Source != null) { Source.clip = clip; return; }
        if (Container != null && Container.GetType().IsValueType)
            throw new InvalidOperationException(
                $"'{Label}' is held by a value type ({Container.GetType().Name}); a write through a boxed copy would vanish.");
        if (Field != null) { Field.SetValue(Container, clip); return; }
        if (Container is IList list && Index >= 0 && Index < list.Count) { list[Index] = clip; return; }
        throw new InvalidOperationException($"'{Label}' has no writable location.");
    }

    public override string ToString() => $"{GroupName} ▸ {Label}";
}

/// <summary>One AudioMixerGroup reference: a routing field on a profile or manager, or an AudioSource's output group.</summary>
public sealed class AudioRoutingRef
{
    public UnityEngine.Object Host;
    public GameObject PrefabRoot;
    public AudioHostKind HostKind;

    /// <summary>The section the host belongs to, where the clip walk has one; otherwise the host's type in words.</summary>
    public string GroupName;
    public string TypeName;
    public string ObjectName;
    /// <summary>A readable, stable label: the field path for a routing field, "Output" for an AudioSource.</summary>
    public string Label;

    public object Container;
    public FieldInfo Field;
    public int Index = -1;
    /// <summary>When the reference is an AudioSource's outputAudioMixerGroup.</summary>
    public AudioSource Source;

    public AudioMixerGroup Group
    {
        get
        {
            if (Source != null) return Source.outputAudioMixerGroup;
            if (Field != null) return Field.GetValue(Container) as AudioMixerGroup;
            if (Container is IList list && Index >= 0 && Index < list.Count) return list[Index] as AudioMixerGroup;
            return null;
        }
    }

    public void Set(AudioMixerGroup group)
    {
        if (Source != null) { Source.outputAudioMixerGroup = group; return; }
        if (Container != null && Container.GetType().IsValueType)
            throw new InvalidOperationException(
                $"'{Label}' is held by a value type ({Container.GetType().Name}); a write through a boxed copy would vanish.");
        if (Field != null) { Field.SetValue(Container, group); return; }
        if (Container is IList list && Index >= 0 && Index < list.Count) { list[Index] = group; return; }
        throw new InvalidOperationException($"'{Label}' has no writable location.");
    }

    public override string ToString() => $"{GroupName} ▸ {Label}";
}
