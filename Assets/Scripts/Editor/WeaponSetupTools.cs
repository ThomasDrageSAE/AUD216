using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/*
 * WeaponSetupTools.cs
 *
 * Purpose: Finishes the scene-side setup for the weapon reload / ammo feature and the
 *          bottom-right current-weapon panel.
 * Menu:    Tools/Weapons/...
 *
 * All three jobs are scene edits, and they are done from a menu item rather than by editing
 * the scene file so Unity itself writes them - the .unity file on disk is routinely older
 * than what is open in the editor, and a hand edit would be lost the next time the owner
 * saves.
 *
 * ⚠ Binds to serialized fields by literal name ("m_WeaponProfiles", "reloadActionSounds",
 * "ammoText", "weaponName", "maxAmmo", "hudIcon", "itemIcon", and CurrentWeaponHUD's
 * "canvasGroup" / "icon" / "ammoLabel"). Renaming any of those compiles clean and breaks
 * this silently, like the other editors here - see CODE_STRUCTURE's editor-tooling warning.
 */

public static class WeaponSetupTools
{
    private const string PlaceholderClipPath = AudioProgressScanner.DemoSoundsFolder + "Beep to replace.wav";
    private const string ReadoutName = "Ammo Readout";
    private const string PreferredCanvas = "Canvas - Stats";

    /// <summary>
    /// Only used when the open scene has no WeaponController to ask - a scene can
    /// legitimately be open without the player loaded, and doing nothing at all would look
    /// like the tool had run and found no work.
    /// </summary>
    private static readonly string[] FallbackMagazineNames = { "SMG", "Revolver" };

    // ---- Ammo readout --------------------------------------------------------

    [MenuItem("Tools/Weapons/Create Ammo Readout")]
    private static void CreateAmmoReadout()
    {
        Canvas canvas = FindHudCanvas();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Ammo readout",
                "No screen-space Canvas in the open scene to put the readout on.", "OK");
            return;
        }

        TextMeshProUGUI readout = FindExistingReadout(canvas);
        bool created = false;

        if (readout == null)
        {
            GameObject host = new GameObject(ReadoutName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(host, "Create Ammo Readout");
            host.transform.SetParent(canvas.transform, false);

            readout = host.AddComponent<TextMeshProUGUI>();

            // Bottom-right, clear of the stat bars on the left and the hotbar in the middle.
            RectTransform rect = host.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-60f, 60f);
            rect.sizeDelta = new Vector2(340f, 70f);

            readout.alignment = TextAlignmentOptions.BottomRight;
            readout.fontSize = 46f;
            readout.color = Color.white;
            readout.raycastTarget = false;      // never eat a click meant for the world

            // Match whatever the rest of the HUD is set in, rather than the TMP default.
            TMP_FontAsset hudFont = FindHudFont(canvas);
            if (hudFont != null) readout.font = hudFont;

            created = true;
        }

        readout.text = "12 / 12";   // overwritten at runtime; just makes it visible now

        int wired = AssignReadoutToFirearms(readout);

        EditorUtility.SetDirty(canvas);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

        Selection.activeObject = readout.gameObject;
        EditorGUIUtility.PingObject(readout.gameObject);

        Debug.Log($"[WeaponSetupTools] {(created ? "Created" : "Reused")} the '{ReadoutName}' under " +
                  $"'{canvas.name}' and assigned it to {wired} firearm(s). It stays hidden until a gun " +
                  "is raised. SAVE THE SCENE.", readout);
    }

    private static Canvas FindHudCanvas()
    {
        Canvas fallback = null;

        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                                                                  FindObjectsSortMode.None))
        {
            if (canvas.renderMode == RenderMode.WorldSpace) continue;
            if (canvas.name == PreferredCanvas) return canvas;
            if (fallback == null) fallback = canvas;
        }

        return fallback;
    }

    private static TextMeshProUGUI FindExistingReadout(Canvas canvas)
    {
        foreach (TextMeshProUGUI text in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.gameObject.name == ReadoutName) return text;
        }
        return null;
    }

    private static TMP_FontAsset FindHudFont(Canvas canvas)
    {
        foreach (TextMeshProUGUI text in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.font != null) return text.font;
        }
        return null;
    }

    /// <summary>Points every firearm's ammoText at the readout. Returns how many were wired.</summary>
    private static int AssignReadoutToFirearms(TextMeshProUGUI readout)
    {
        int wired = 0;

        foreach (WeaponController weapon in Object.FindObjectsByType<WeaponController>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedObject serialized = new SerializedObject(weapon);
            SerializedProperty nameProperty = serialized.FindProperty("weaponName");
            SerializedProperty textProperty = serialized.FindProperty("ammoText");

            if (nameProperty == null || textProperty == null) continue;
            if (!weapon.UsesMagazine) continue;

            textProperty.objectReferenceValue = readout;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(weapon);
            wired++;
        }

        return wired;
    }

    // ---- Current weapon panel -----------------------------------------------

    private const string PanelName = "Current Weapon";
    private const string HudScreenSpacePath = "Game Management/Canvases/Canvas - Interface/HUD/ScreenSpace";
    private const string BottomRightName = "Bottom Right";
    private const string GuideCornerName = "Bottom Left";
    private const string PanelPrefabPath =
        "Assets/Inbox/Low Poly Models/InterfaceApocalypseHUD/Prefabs/Player_Health_Equipment/HUD_CurrentWeapon_Rack01.prefab";
    private const string PanelIconChild = "Icon_CurrentWeapon";
    private const string PanelLabelChild = "Label_AmmoCount";
    private const string PanelTotalChild = "Label_AmmoTotal";   // Minimal prefab only

    /// <summary>
    /// Which Synty variant of a pickup's hotbar icon the panel shows. Every weapon icon
    /// ships as X, X_Side, X_Underlay and X_Clean; the rusty box in the pack's FPS demo
    /// shows the full-colour side view. (The Minimal variant used the white _Underlay
    /// silhouette instead.)
    /// </summary>
    private const string HudIconSuffix = "_Side";

    /// <summary>
    /// Where the panel sits inside its corner: the right margin mirrors the objective
    /// list's left one (95), and the bottom edge lines up with the hotbar (101). Same
    /// 3840x2160 reference resolution as the Synty demo, so the prefab is used at scale 1.
    /// </summary>
    private static readonly Vector2 PanelOffset = new Vector2(-95f, 101f);

    /// <summary>
    /// Builds the bottom-right current-weapon panel from the Synty Apocalypse HUD pack's
    /// HUD_CurrentWeapon_Rack01 prefab (the rusty box the pack's own FPS demo uses; the
    /// owner tried the Minimal variant on 2026-09-03 and came back to this one as the
    /// better style match), adds and wires CurrentWeaponHUD, and gives every weapon pickup
    /// the side-on icon the demo shows in that box. Re-runnable: a panel built from this
    /// prefab is re-wired, one built from another Synty variant is replaced, and an
    /// authored pickup icon is kept. Switching variants is these two constants plus a re-run.
    /// </summary>
    [MenuItem("Tools/Weapons/Create Current Weapon Panel")]
    private static void CreateCurrentWeaponPanel()
    {
        GameObject screenSpace = GameObject.Find(HudScreenSpacePath);
        if (screenSpace == null)
        {
            EditorUtility.DisplayDialog("Current weapon panel",
                $"'{HudScreenSpacePath}' is not in the open scene - is the level loaded?", "OK");
            return;
        }

        const string undoLabel = "Create Current Weapon Panel";

        // The HUD is laid out as one container per screen corner; this is the only corner
        // the scene did not have. Same rect as the others, mirrored to the right.
        Transform bottomRight = screenSpace.transform.Find(BottomRightName);
        if (bottomRight == null)
        {
            GameObject corner = new GameObject(BottomRightName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(corner, undoLabel);
            corner.transform.SetParent(screenSpace.transform, false);

            RectTransform rect = corner.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(600f, 600f);

            // The sibling corners carry a design-time guide Image; copy it so the new
            // corner reads the same way in the editor.
            Transform guideCorner = screenSpace.transform.Find(GuideCornerName);
            Image guide = guideCorner != null ? guideCorner.GetComponent<Image>() : null;
            if (guide != null)
            {
                Image copy = corner.AddComponent<Image>();
                copy.sprite = guide.sprite;
                copy.color = guide.color;
                copy.raycastTarget = guide.raycastTarget;
                copy.enabled = guide.enabled;
            }

            bottomRight = corner.transform;
        }

        Transform panel = bottomRight.Find(PanelName);
        bool created = false;
        bool replaced = false;

        if (panel != null)
        {
            // Built from a different Synty variant (the panel has been switched between the
            // rusty box and the Minimal readout): rebuild rather than re-wire, so a style
            // switch is one re-run. A panel that is not a prefab instance at all was
            // hand-made - keep and re-wire it.
            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(panel.gameObject);
            if (!string.IsNullOrEmpty(source) &&
                !string.Equals(source, PanelPrefabPath, System.StringComparison.OrdinalIgnoreCase))
            {
                Undo.DestroyObjectImmediate(panel.gameObject);
                panel = null;
                replaced = true;
            }
        }

        if (panel == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Current weapon panel",
                    $"The Synty prefab is missing:\n{PanelPrefabPath}", "OK");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, screenSpace.scene);
            Undo.RegisterCreatedObjectUndo(instance, undoLabel);
            instance.name = PanelName;
            instance.transform.SetParent(bottomRight, false);

            RectTransform rect = instance.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = PanelOffset;

            panel = instance.transform;
            created = true;
        }

        // Behaviour and its wiring, by the child names the Synty prefab uses.
        CurrentWeaponHUD hud = panel.GetComponent<CurrentWeaponHUD>();
        if (hud == null) hud = Undo.AddComponent<CurrentWeaponHUD>(panel.gameObject);

        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = Undo.AddComponent<CanvasGroup>(panel.gameObject);

        Image icon = FindChild<Image>(panel, PanelIconChild);
        TextMeshProUGUI label = FindChild<TextMeshProUGUI>(panel, PanelLabelChild);
        TextMeshProUGUI total = FindChild<TextMeshProUGUI>(panel, PanelTotalChild);   // optional
        if (icon == null || label == null)
        {
            Debug.LogWarning($"[WeaponSetupTools] The panel has no '{PanelIconChild}' / " +
                             $"'{PanelLabelChild}' child - the Synty prefab has been renamed or " +
                             "the instance edited. Wire CurrentWeaponHUD by hand.", panel);
        }

        SerializedObject serialized = new SerializedObject(hud);
        serialized.FindProperty("canvasGroup").objectReferenceValue = group;
        serialized.FindProperty("icon").objectReferenceValue = icon;
        serialized.FindProperty("ammoLabel").objectReferenceValue = label;
        serialized.FindProperty("ammoTotalLabel").objectReferenceValue = total;
        serialized.ApplyModifiedProperties();

        int iconsSet = SeedHudIcons(out Sprite previewIcon, out int previewAmmo);

        // Design-time preview: the prefab ships showing a grenade launcher and "99".
        // Show what the player will actually see; the runtime repaints it anyway.
        if (created)
        {
            if (icon != null && previewIcon != null)
            {
                Undo.RecordObject(icon, undoLabel);
                icon.sprite = previewIcon;
            }
            if (label != null && previewAmmo > 0)
            {
                Undo.RecordObject(label, undoLabel);
                label.text = previewAmmo.ToString();
            }
            if (total != null && previewAmmo > 0)
            {
                Undo.RecordObject(total, undoLabel);
                total.text = "/ " + previewAmmo;
            }
        }

        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(screenSpace.scene);

        Selection.activeObject = panel.gameObject;
        EditorGUIUtility.PingObject(panel.gameObject);

        Debug.Log($"[WeaponSetupTools] {(replaced ? "Replaced" : created ? "Created" : "Reused")} the '{PanelName}' panel " +
                  $"under '{BottomRightName}' and wired CurrentWeaponHUD; set HUD icons on " +
                  $"{iconsSet} weapon pickup(s). It stays hidden until a weapon is raised. " +
                  "SAVE THE SCENE.", panel);
    }

    private static T FindChild<T>(Transform root, string childName) where T : Component
    {
        foreach (T candidate in root.GetComponentsInChildren<T>(true))
        {
            if (candidate.gameObject.name == childName) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Gives each weapon pickup the HudIconSuffix variant of its hotbar icon for the panel.
    /// Fills an empty hudIcon, and swaps one this tool seeded earlier (any sibling variant
    /// of the hotbar icon) when the wanted variant changes; anything else is an authored
    /// choice and is kept. Also hands back the first firearm's icon and magazine size for
    /// the design-time preview.
    /// </summary>
    private static int SeedHudIcons(out Sprite firstFirearmIcon, out int firstFirearmAmmo)
    {
        int set = 0;
        firstFirearmIcon = null;
        firstFirearmAmmo = 0;

        foreach (WeaponItem item in Object.FindObjectsByType<WeaponItem>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedObject serialized = new SerializedObject(item);
            SerializedProperty hudIcon = serialized.FindProperty("hudIcon");
            SerializedProperty itemIcon = serialized.FindProperty("itemIcon");
            if (hudIcon == null || itemIcon == null) continue;

            Sprite hotbarIcon = itemIcon.objectReferenceValue as Sprite;
            Sprite wanted = HudVariant(hotbarIcon);
            Sprite current = hudIcon.objectReferenceValue as Sprite;

            if (wanted != null && current != wanted &&
                (current == null || IsVariantOf(current, hotbarIcon)))
            {
                hudIcon.objectReferenceValue = wanted;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(item);
                set++;
            }

            WeaponController weapon = item.GetWeaponController();
            if (firstFirearmIcon == null && weapon != null && weapon.UsesMagazine)
            {
                firstFirearmIcon = item.GetHudIcon();
                SerializedProperty maxAmmo = new SerializedObject(weapon).FindProperty("maxAmmo");
                firstFirearmAmmo = maxAmmo != null ? maxAmmo.intValue : 0;
            }
        }

        return set;
    }

    private static readonly string[] VariantSuffixes = { "_Side", "_Underlay", "_Clean" };

    /// <summary>The icon's asset path minus extension and any Synty variant suffix.</summary>
    private static string VariantStem(Sprite icon)
    {
        if (icon == null) return null;

        string path = AssetDatabase.GetAssetPath(icon);
        if (string.IsNullOrEmpty(path)) return null;

        string stem = path.Substring(0, path.Length - System.IO.Path.GetExtension(path).Length);
        foreach (string suffix in VariantSuffixes)
        {
            if (stem.EndsWith(suffix)) return stem.Substring(0, stem.Length - suffix.Length);
        }
        return stem;
    }

    private static Sprite HudVariant(Sprite hotbarIcon)
    {
        string stem = VariantStem(hotbarIcon);
        if (stem == null) return null;

        string extension = System.IO.Path.GetExtension(AssetDatabase.GetAssetPath(hotbarIcon));
        return AssetDatabase.LoadAssetAtPath<Sprite>(stem + HudIconSuffix + extension);
    }

    private static bool IsVariantOf(Sprite candidate, Sprite hotbarIcon)
    {
        string a = VariantStem(candidate);
        string b = VariantStem(hotbarIcon);
        return a != null && b != null && string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
    }

    // ---- Reload sounds -------------------------------------------------------

    /// <summary>
    /// The reload takes ported from Liam Chapman's 26T2 AUD216 submission, at the owner's
    /// request - this project recorded none of its own. The Revolver gets both of his takes
    /// so the reload varies between them; the SMG has a single take. A firearm with no entry
    /// here, or whose clip is missing from disk, falls back to the placeholder beep.
    /// </summary>
    private static string[] ReloadClipPathsFor(string weaponName)
    {
        if (weaponName.Contains("SMG"))
        {
            return new[] { "Assets/Audio/Weapons/SMGReload_SFX.wav" };
        }

        if (weaponName.Contains("Revolver"))
        {
            // His second take (RevolverReload_SFX.mp3) was dropped: Unity's importer reported
            // it truncated, and at 44.1 kHz it did not match the two 96 kHz/24-bit recordings.
            return new[] { "Assets/Audio/Weapons/Revolver_Reload_SFX.wav" };
        }

        return new string[0];
    }

    /// <summary>
    /// Fills the firearms' reloadActionSounds with those takes. Only ever fills a slot that
    /// is genuinely empty, so authored audio is never overwritten, and it falls back to the
    /// placeholder beep rather than leaving a slot empty - an empty slot reads as
    /// unimplemented in the Ctrl+G report, a beep reads as a placeholder awaiting audio.
    /// </summary>
    [MenuItem("Tools/Weapons/Seed Reload Sounds")]
    private static void SeedReloadSounds()
    {
        WeaponAudioManager manager = Object.FindFirstObjectByType<WeaponAudioManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Reload sounds",
                "No WeaponAudioManager in the open scene.", "OK");
            return;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty("m_WeaponProfiles");
        if (profiles == null || !profiles.isArray)
        {
            Debug.LogError("[WeaponSetupTools] WeaponAudioManager has no array field " +
                           "'m_WeaponProfiles' - it has been renamed, and this tool binds to it " +
                           "by name.", manager);
            return;
        }

        AudioClip placeholder = AssetDatabase.LoadAssetAtPath<AudioClip>(PlaceholderClipPath);
        HashSet<string> magazineWeapons = MagazineWeaponNames();
        List<string> seeded = new List<string>();
        List<string> unmuted = new List<string>();

        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
            SerializedProperty name = profile.FindPropertyRelative("weaponName");
            SerializedProperty sounds = profile.FindPropertyRelative("reloadActionSounds");

            if (name == null || sounds == null || !sounds.isArray) continue;

            string weaponName = name.stringValue;
            if (!magazineWeapons.Contains(AudioProfileKeys.Normalize(weaponName))) continue;

            // A reload at volume 0 is inaudible however good the clip is, and every firearm
            // in this scene serialized 0 - the profiles predate reloadVolume, so they never
            // picked up the class default of 1. Repaired before the authored-audio skip
            // below, because a profile that already has clips is just as silent.
            SerializedProperty volume = profile.FindPropertyRelative("reloadVolume");
            if (volume != null && volume.propertyType == SerializedPropertyType.Float &&
                volume.floatValue <= 0f)
            {
                volume.floatValue = 1f;
                unmuted.Add(weaponName.Trim());
            }

            // Never overwrite authored audio - only fill a slot that is genuinely empty.
            if (sounds.arraySize > 0) continue;

            List<AudioClip> clips = new List<AudioClip>();
            foreach (string path in ReloadClipPathsFor(weaponName))
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                {
                    clips.Add(clip);
                }
                else
                {
                    Debug.LogWarning($"[WeaponSetupTools] Reload clip missing at {path} - " +
                                     $"{weaponName.Trim()} falls back to the placeholder beep.",
                                     manager);
                }
            }

            bool usedPlaceholder = false;
            if (clips.Count == 0)
            {
                if (placeholder == null)
                {
                    Debug.LogWarning("[WeaponSetupTools] No reload clip, and no placeholder at " +
                                     $"{PlaceholderClipPath} - {weaponName.Trim()} left empty.",
                                     manager);
                    continue;
                }

                clips.Add(placeholder);
                usedPlaceholder = true;
            }

            sounds.arraySize = clips.Count;
            for (int c = 0; c < clips.Count; c++)
            {
                sounds.GetArrayElementAtIndex(c).objectReferenceValue = clips[c];
            }

            seeded.Add($"{weaponName.Trim()} ({clips.Count}" +
                       (usedPlaceholder ? ", placeholder" : " take(s)") + ")");
        }

        if (seeded.Count == 0 && unmuted.Count == 0)
        {
            Debug.Log("[WeaponSetupTools] Reload slots already hold clips and are audible; " +
                      "nothing changed.", manager);
            return;
        }

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

        string report = seeded.Count > 0
            ? $"Seeded reload sounds on: {string.Join(", ", seeded)}."
            : "Reload slots already held clips.";
        if (unmuted.Count > 0)
        {
            report += $" Raised reloadVolume from 0 to 1 on: {string.Join(", ", unmuted)} - " +
                      "they would have reloaded silently.";
        }

        Debug.Log($"[WeaponSetupTools] {report} SAVE THE SCENE.", manager);
    }

    /// <summary>
    /// Profile names of the weapons in the open scene that carry a magazine, asked of the
    /// weapons themselves via WeaponController.UsesMagazine rather than matched against a
    /// list kept here. The axe and the crossbow answer false.
    /// </summary>
    private static HashSet<string> MagazineWeaponNames()
    {
        var names = new HashSet<string>();

        WeaponController[] weapons = Object.FindObjectsByType<WeaponController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (WeaponController weapon in weapons)
        {
            if (!weapon.UsesMagazine) continue;

            string name = AudioProfileKeys.Normalize(weapon.ProfileName);
            if (!string.IsNullOrEmpty(name)) names.Add(name);
        }

        if (names.Count == 0)
        {
            Debug.LogWarning("[WeaponSetupTools] No WeaponController with a magazine in the " +
                             "open scene, so falling back to the built-in names (" +
                             string.Join(", ", FallbackMagazineNames) + "). Load the player " +
                             "to have the weapons answer for themselves.");

            foreach (string fallback in FallbackMagazineNames)
            {
                names.Add(AudioProfileKeys.Normalize(fallback));
            }
        }

        return names;
    }
}
