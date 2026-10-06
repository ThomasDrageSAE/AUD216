using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/*
 * LevelFeatureTools.cs
 *
 * Purpose: Scene-side setup for the engagement features added 2026-09-02 - the dawn
 * chorus, the well offering, and the tape recorders.
 * Menu:    Tools/Audio/... and Tools/Interactions/...
 *
 * Menu items rather than hand edits, for the standing reason: the .unity file on disk is
 * routinely older than what is open in the editor, so a hand edit would be lost on the
 * next save. All three are safe to re-run.
 *
 * ⚠ Binds to serialized fields by literal name (the PropSoundInteraction fields below),
 * like the other editor tools here - renaming one compiles clean and breaks this loudly
 * at run time instead.
 */
public static class LevelFeatureTools
{
    // ---- Dawn chorus ---------------------------------------------------------

    [MenuItem("Tools/Audio/Add Dawn Chorus")]
    private static void AddDawnChorus()
    {
        DawnChorus existing = Object.FindFirstObjectByType<DawnChorus>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Debug.Log("[LevelFeatureTools] The scene already has a Dawn Chorus.", existing);
            EditorGUIUtility.PingObject(existing);
            return;
        }

        // Beside the other UI/notification audio; its own object only when that is absent.
        UIAudioManager uiAudio = Object.FindFirstObjectByType<UIAudioManager>(FindObjectsInactive.Include);
        GameObject host = uiAudio != null ? uiAudio.gameObject : new GameObject("Dawn Chorus");

        DawnChorus chorus = Undo.AddComponent<DawnChorus>(host);
        EditorSceneManager.MarkSceneDirty(host.scene);
        EditorGUIUtility.PingObject(chorus);

        Debug.Log("[LevelFeatureTools] Dawn Chorus added. Author its chorus clips and (optional) " +
                  "stinger, set its Output mixer group, and SAVE THE SCENE.", chorus);
    }

    // ---- Well offering -------------------------------------------------------

    [MenuItem("Tools/Interactions/Add Well Offering")]
    private static void AddWellOffering()
    {
        const string prefabPath = "Assets/Well/Well.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            EditorUtility.DisplayDialog("Well offering",
                $"No prefab at {prefabPath}.\n\nRun Tools > Interactions > Create Well Prefab " +
                "first.", "OK");
            return;
        }

        // On the PREFAB, so the placed instance inherits it - and a targeted edit rather
        // than a rebuild, so authored fields on the prefab survive.
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (root.GetComponent<WellOffering>() != null)
            {
                Debug.Log("[LevelFeatureTools] The Well prefab already has a Well Offering.");
                return;
            }

            root.AddComponent<WellOffering>();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            Debug.Log("[LevelFeatureTools] Well Offering added to the Well prefab. Run " +
                      "Tools > Interactions > Add Missing Audio Profiles for its 'Well Splash' " +
                      "slot, author a splash clip, and SAVE THE SCENE. Optional: assign " +
                      "deactivated scene items as its dawn rewards.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---- Day 3 objectives ----------------------------------------------------

    [MenuItem("Tools/Objectives/Add Day 3 Cooking And Offering Objectives")]
    private static void AddDay3Objectives()
    {
        ObjectiveData cook = EnsureObjectiveAsset(ObjectiveConstants.COOK_A_FISH,
            "Objective Day 3 - CookAFish", "Cook a fish at the campfire");
        ObjectiveData offer = EnsureObjectiveAsset(ObjectiveConstants.MAKE_WELL_OFFERING,
            "Objective Day 3 - MakeWellOffering", "Drop an item into the well as an offering");
        if (cook == null || offer == null) return;

        int changed = 0;
        if (RegisterWithObjectiveManager(cook)) changed++;
        if (RegisterWithObjectiveManager(offer)) changed++;
        if (AddDailyEntry(ObjectiveConstants.COOK_A_FISH, 3, 0.17f)) changed++;
        if (AddDailyEntry(ObjectiveConstants.MAKE_WELL_OFFERING, 3, 0.175f)) changed++;

        AssetDatabase.SaveAssets();
        Debug.Log(changed > 0
            ? $"[LevelFeatureTools] Day 3 objectives wired ({changed} change(s)). Optional: give " +
              $"the two assets icons. SAVE THE SCENE."
            : "[LevelFeatureTools] Day 3 objectives were already fully wired.");
    }

    /// <summary>Loads or creates the ObjectiveData asset beside the existing ones.</summary>
    private static ObjectiveData EnsureObjectiveAsset(string objectiveId, string fileName, string description)
    {
        string path = $"Assets/Inbox/Objectives/{fileName}.asset";
        ObjectiveData existing = AssetDatabase.LoadAssetAtPath<ObjectiveData>(path);
        if (existing != null) return existing;

        ObjectiveData data = ScriptableObject.CreateInstance<ObjectiveData>();
        data.objectiveId = objectiveId;
        data.description = description;
        data.showWorldMarker = false;   // both targets are wherever the player chose to be
        AssetDatabase.CreateAsset(data, path);
        return data;
    }

    /// <summary>Appends the asset to ObjectiveManager.allObjectives unless already there.</summary>
    private static bool RegisterWithObjectiveManager(ObjectiveData data)
    {
        ObjectiveManager manager = Object.FindFirstObjectByType<ObjectiveManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogError("[LevelFeatureTools] No ObjectiveManager in the open scene.");
            return false;
        }

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty list = serialized.FindProperty("allObjectives");
        if (list == null || !list.isArray)
        {
            Debug.LogError("[LevelFeatureTools] ObjectiveManager has no 'allObjectives' list - " +
                           "renamed? This tool binds to it by name.", manager);
            return false;
        }

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) return false;
        }

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
        serialized.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        return true;
    }

    /// <summary>Appends a day/time entry to the DailyObjectiveData ASSET the scene's
    /// DailyObjectiveManager references - found, not hard-coded.</summary>
    private static bool AddDailyEntry(string objectiveId, int day, float timeToAppear)
    {
        DailyObjectiveManager manager =
            Object.FindFirstObjectByType<DailyObjectiveManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogError("[LevelFeatureTools] No DailyObjectiveManager in the open scene.");
            return false;
        }

        SerializedProperty dataRef = new SerializedObject(manager).FindProperty("dailyObjectives");
        DailyObjectiveData asset = dataRef != null ? dataRef.objectReferenceValue as DailyObjectiveData : null;
        if (asset == null)
        {
            Debug.LogError("[LevelFeatureTools] DailyObjectiveManager has no DailyObjectiveData asset assigned.");
            return false;
        }

        SerializedObject serialized = new SerializedObject(asset);
        SerializedProperty entries = serialized.FindProperty("objectives");
        if (entries == null || !entries.isArray) return false;

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty id = entries.GetArrayElementAtIndex(i).FindPropertyRelative("objectiveId");
            if (id != null && id.stringValue == objectiveId) return false;
        }

        entries.InsertArrayElementAtIndex(entries.arraySize);
        SerializedProperty entry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
        entry.FindPropertyRelative("objectiveId").stringValue = objectiveId;
        entry.FindPropertyRelative("dayNumber").intValue = day;
        entry.FindPropertyRelative("timeToAppear").floatValue = timeToAppear;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);
        return true;
    }

    // ---- Heartbeat layer -----------------------------------------------------

    [MenuItem("Tools/Audio/Add Heartbeat Layer")]
    private static void AddHeartbeatLayer()
    {
        HeartbeatLayer existing = Object.FindFirstObjectByType<HeartbeatLayer>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Debug.Log("[LevelFeatureTools] The scene already has a Heartbeat Layer.", existing);
            EditorGUIUtility.PingObject(existing);
            return;
        }

        UIAudioManager uiAudio = Object.FindFirstObjectByType<UIAudioManager>(FindObjectsInactive.Include);
        GameObject host = uiAudio != null ? uiAudio.gameObject : new GameObject("Heartbeat Layer");

        HeartbeatLayer layer = Undo.AddComponent<HeartbeatLayer>(host);
        EditorSceneManager.MarkSceneDirty(host.scene);
        EditorGUIUtility.PingObject(layer);

        Debug.Log("[LevelFeatureTools] Heartbeat Layer added. Author a LOOPING heartbeat clip, " +
                  "set its Output mixer group, optionally the low-health/normal snapshot pair, " +
                  "and SAVE THE SCENE.", layer);
    }

    // ---- Flashlight ----------------------------------------------------------

    [MenuItem("Tools/Items/Create Flashlight Pickup")]
    private static void CreateFlashlightPickup()
    {
        const string folder = "Assets/Flashlight";
        const string prefabPath = folder + "/Flashlight Pickup.prefab";

        // A primitive body, like the tape recorder - swap the visual for any model
        // without touching the item.
        GameObject root = new GameObject("Flashlight Pickup");
        try
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.07f, 0.14f, 0.07f);
            body.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            body.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);   // lying on its side
            Object.DestroyImmediate(body.GetComponent<Collider>());

            // Item collects through its own trigger, the way every pickup here works.
            SphereCollider trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.8f;

            FlashlightItem item = root.AddComponent<FlashlightItem>();
            SerializedObject serialized = new SerializedObject(item);
            SetString(serialized, "itemName", "Flashlight");
            SetString(serialized, "itemDescription", "Press its slot number to toggle.");
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "Flashlight");
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (saved == null)
            {
                Debug.LogError($"[LevelFeatureTools] Could not write {prefabPath}.");
                return;
            }

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            Debug.Log("[LevelFeatureTools] Created " + prefabPath + ". Place it in the level, give it " +
                      "an icon, author the two toggles on the manager's 'Flashlight' profile, " +
                      "and SAVE.", saved);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ---- Flashlight visual + icon --------------------------------------------

    [MenuItem("Tools/Items/Upgrade Flashlight Visual")]
    private static void UpgradeFlashlightVisual()
    {
        const string prefabPath = "Assets/Flashlight/Flashlight Pickup.prefab";
        const string torchPath = "Assets/Inbox/Prefabs/SM_Prop_Torch_01.prefab";
        const string iconPath = "Assets/Flashlight/Torch Inventory Icon.png";
        const string sphereMaterialPath = "Assets/Inbox/Materials/Transparent Material - pickups.mat";

        GameObject torch = AssetDatabase.LoadAssetAtPath<GameObject>(torchPath);
        Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
        Material sphereMaterial = AssetDatabase.LoadAssetAtPath<Material>(sphereMaterialPath);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null || torch == null)
        {
            EditorUtility.DisplayDialog("Flashlight visual",
                "Flashlight Pickup prefab or SM_Prop_Torch_01 prefab not found.", "OK");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            // The placeholder primitive goes; the Synty torch stands in as the visual.
            Transform body = root.transform.Find("Body");
            if (body != null) Object.DestroyImmediate(body.gameObject);

            Transform visual = root.transform.Find("SM_Prop_Torch_01");
            if (visual == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(torch, root.transform);
                instance.name = "SM_Prop_Torch_01";
                visual = instance.transform;

                // A prop model can ship with its own colliders; the pickup's trigger
                // owns collision, and a stray solid collider would snag the player.
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }
            }

            // Inside the sphere, tilted a little so the spin reads.
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.Euler(0f, 0f, 20f);
            visual.localScale = Vector3.one * 0.5f;

            // The translucent pickup sphere, matching the food/weapon pickups: the
            // built-in sphere mesh with the shared pickup material, on a child so the
            // root's trigger radius is not disturbed by any scale.
            Transform sphere = root.transform.Find("Pickup Sphere");
            if (sphere == null)
            {
                GameObject sphereObject = new GameObject("Pickup Sphere");
                sphere = sphereObject.transform;
                sphere.SetParent(root.transform, false);
                sphere.localScale = Vector3.one * 0.55f;

                MeshFilter filter = sphereObject.AddComponent<MeshFilter>();
                filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");

                MeshRenderer renderer = sphereObject.AddComponent<MeshRenderer>();
                if (sphereMaterial != null) renderer.sharedMaterial = sphereMaterial;
            }

            // The same spin the other pickups use, at the same rate.
            RotateObject rotator = root.GetComponent<RotateObject>();
            if (rotator == null)
            {
                rotator = root.AddComponent<RotateObject>();
                SerializedObject rotatorSerialized = new SerializedObject(rotator);
                SerializedProperty speed = rotatorSerialized.FindProperty("degreesPerSecond");
                if (speed != null) speed.floatValue = 150f;
                rotatorSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // The pickups layer, like Pickup - Food.
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = 10;
            }

            // The inventory hotbar renders items BY ICON, so without one the collected
            // flashlight occupied a slot invisibly.
            FlashlightItem item = root.GetComponent<FlashlightItem>();
            if (item != null && icon != null)
            {
                SerializedObject serialized = new SerializedObject(item);
                SerializedProperty iconProperty = serialized.FindProperty("itemIcon");
                if (iconProperty != null)
                {
                    iconProperty.objectReferenceValue = icon;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // The placed pickup should FLOAT like the others - about 1.3m off the ground -
        // not sit on it where the first placement pass grounded it.
        FlashlightItem placed = Object.FindFirstObjectByType<FlashlightItem>(FindObjectsInactive.Include);
        if (placed != null)
        {
            Vector3 position = placed.transform.position;
            if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 30f))
            {
                position = ground.point + Vector3.up * 1.3f;
            }
            else
            {
                position += Vector3.up * 1.3f;
            }

            placed.transform.position = position;
            EditorSceneManager.MarkSceneDirty(placed.gameObject.scene);
        }

        Debug.Log("[LevelFeatureTools] Flashlight pickup restyled: SM_Prop_Torch_01 inside the " +
                  "translucent pickup sphere, spinning at 150 deg/s, floating 1.3m up, icon " +
                  "assigned. Check it in the scene, then SAVE.");
    }

    // ---- Walkie talkie -------------------------------------------------------

    [MenuItem("Tools/Items/Create Walkie Talkie Pickup")]
    private static void CreateWalkieTalkiePickup()
    {
        const string folder = "Assets/Walkie Talkie";
        const string prefabPath = folder + "/Walkie Talkie Pickup.prefab";
        const string radioPath = "Assets/Inbox/Low Poly Models/PolygonExtras/Models/SM_Prop_Radio_01.fbx";
        const string iconPath = folder + "/Walkie Talkie Icon.png";
        const string sphereMaterialPath = "Assets/Inbox/Materials/Transparent Material - pickups.mat";

        GameObject radio = AssetDatabase.LoadAssetAtPath<GameObject>(radioPath);
        Material sphereMaterial = AssetDatabase.LoadAssetAtPath<Material>(sphereMaterialPath);
        if (radio == null)
        {
            EditorUtility.DisplayDialog("Walkie talkie", $"No model at {radioPath}.", "OK");
            return;
        }

        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets", "Walkie Talkie");
        }

        // Owner-supplied art at iconPath wins and is never overwritten; otherwise a
        // transparent render of the model itself is baked once, so the hotbar slot is
        // never invisible (the flashlight lesson - the hotbar renders items BY ICON).
        Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
        if (icon == null) icon = BakeModelIcon(radio, iconPath);

        GameObject root = new GameObject("Walkie Talkie Pickup");
        try
        {
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(radio, root.transform);
            visual.name = "SM_Prop_Radio_01";

            // The pickup's trigger owns collision; a stray solid collider would snag feet.
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            // Scaled to sit inside the pickup sphere whatever the model's native size.
            Bounds bounds = new Bounds(root.transform.position, Vector3.zero);
            bool first = true;
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float fit = largest > 0.0001f ? 0.4f / largest : 1f;
            visual.transform.localScale = Vector3.one * fit;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 20f);   // tilt so the spin reads

            // The translucent pickup sphere, matching the other pickups.
            GameObject sphereObject = new GameObject("Pickup Sphere");
            sphereObject.transform.SetParent(root.transform, false);
            sphereObject.transform.localScale = Vector3.one * 0.55f;
            MeshFilter filter = sphereObject.AddComponent<MeshFilter>();
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            MeshRenderer sphereRenderer = sphereObject.AddComponent<MeshRenderer>();
            if (sphereMaterial != null) sphereRenderer.sharedMaterial = sphereMaterial;

            SphereCollider trigger = root.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.8f;

            RotateObject rotator = root.AddComponent<RotateObject>();
            SerializedObject rotatorSerialized = new SerializedObject(rotator);
            SerializedProperty speed = rotatorSerialized.FindProperty("degreesPerSecond");
            if (speed != null) speed.floatValue = 150f;
            rotatorSerialized.ApplyModifiedPropertiesWithoutUndo();

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = 10;   // the pickups layer, like Pickup - Food
            }

            WalkieTalkieItem item = root.AddComponent<WalkieTalkieItem>();
            SerializedObject serialized = new SerializedObject(item);
            SetString(serialized, "itemName", "Walkie Talkie");
            SetString(serialized, "itemDescription", "Crackles with distant chatter while you carry it.");
            SerializedProperty iconProperty = serialized.FindProperty("itemIcon");
            if (iconProperty != null) iconProperty.objectReferenceValue = icon;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (saved == null)
            {
                Debug.LogError($"[LevelFeatureTools] Could not write {prefabPath}.");
                return;
            }

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            Debug.Log("[LevelFeatureTools] Created " + prefabPath + ". Its chatter clips and the " +
                      "min/max silence between bursts are authored on the manager's " +
                      "'Walkie Talkie' profile (run Add Missing Audio Profiles).", saved);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        PlaceWalkieTalkie(prefabPath);
    }

    private static void PlaceWalkieTalkie(string prefabPath)
    {
        if (Object.FindFirstObjectByType<WalkieTalkieItem>(FindObjectsInactive.Include) != null)
        {
            Debug.Log("[LevelFeatureTools] A Walkie Talkie Pickup is already placed; not adding another.");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return;

        // Next to the flashlight pickup when there is one - the same near-spawn shelf -
        // otherwise at the scene origin. The owner places it properly either way.
        Vector3 position = Vector3.zero;
        FlashlightItem flashlight = Object.FindFirstObjectByType<FlashlightItem>(FindObjectsInactive.Include);
        if (flashlight != null) position = flashlight.transform.position + new Vector3(1.5f, 0f, 0f);

        if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 50f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            position = ground.point + Vector3.up * 1.3f;   // floats like the other pickups
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = position;
        Undo.RegisterCreatedObjectUndo(instance, "Place Walkie Talkie");
        EditorSceneManager.MarkSceneDirty(instance.scene);
        Debug.Log($"[LevelFeatureTools] Walkie Talkie Pickup placed at {position} - move it " +
                  "where you want it, then SAVE.");
    }

    /// <summary>
    /// Renders the model against a transparent background and imports it as a sprite.
    /// AssetPreview thumbnails would do, but they load asynchronously and sit on an
    /// opaque grey backdrop; this is synchronous and matches the cut-out style of the
    /// other inventory icons. Drop a nicer PNG on iconPath and re-run the create tool
    /// to use it instead - an existing file is never overwritten.
    /// </summary>
    private static Sprite BakeModelIcon(GameObject model, string iconPath)
    {
        PreviewRenderUtility preview = new PreviewRenderUtility();
        try
        {
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 50f;
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            preview.ambientColor = new Color(0.4f, 0.4f, 0.4f, 1f);

            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>();
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                if (first) { bounds = filter.sharedMesh.bounds; first = false; }
                else bounds.Encapsulate(filter.sharedMesh.bounds);
            }
            if (first)
            {
                Debug.LogWarning("[LevelFeatureTools] Model has no meshes to render an icon " +
                                 "from; the hotbar slot will be blank until a PNG is supplied at " +
                                 iconPath + " and the tool re-run.");
                return null;
            }

            // A three-quarter view, framed on the mesh bounds.
            Quaternion angle = Quaternion.Euler(20f, 140f, 0f);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            preview.camera.transform.position = bounds.center + angle * (Vector3.back * radius * 2.6f);
            preview.camera.transform.LookAt(bounds.center);

            preview.BeginStaticPreview(new Rect(0f, 0f, 256f, 256f));
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                Renderer renderer = filter.GetComponent<Renderer>();
                Material[] materials = renderer != null ? renderer.sharedMaterials : null;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                {
                    Material material = materials != null && materials.Length > 0
                        ? materials[Mathf.Min(sub, materials.Length - 1)]
                        : null;
                    if (material == null) continue;
                    preview.DrawMesh(filter.sharedMesh, Matrix4x4.identity, material, sub);
                }
            }
            preview.camera.Render();
            Texture2D rendered = preview.EndStaticPreview();
            if (rendered == null)
            {
                Debug.LogWarning("[LevelFeatureTools] Icon render failed; the hotbar slot will " +
                                 "be blank until a PNG is supplied at " + iconPath + ".");
                return null;
            }

            // Some pipelines write alpha 0 for opaque pixels; rebuild it from luminance
            // so the cut-out survives, rather than importing a fully transparent PNG.
            Color32[] pixels = rendered.GetPixels32();
            bool anyAlpha = false;
            foreach (Color32 pixel in pixels)
            {
                if (pixel.a > 0) { anyAlpha = true; break; }
            }
            if (!anyAlpha)
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].r + pixels[i].g + pixels[i].b > 12) pixels[i].a = 255;
                }
                rendered.SetPixels32(pixels);
                rendered.Apply();
            }

            System.IO.File.WriteAllBytes(iconPath, rendered.EncodeToPNG());
            AssetDatabase.ImportAsset(iconPath);

            if (AssetImporter.GetAtPath(iconPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
        }
        finally
        {
            preview.Cleanup();
        }
    }


    // ---- Turntable -----------------------------------------------------------

    [MenuItem("Tools/Interactions/Add Turntable Interaction")]
    private static void AddTurntableInteraction()
    {
        GameObject turntable = FindSceneObjectByName("Turntable");
        if (turntable == null)
        {
            EditorUtility.DisplayDialog("Turntable",
                "No object named 'Turntable' in the open scene.\n\nThe studio deck is " +
                "expected under Studio Room > Outboard Gear Right.", "OK");
            return;
        }

        TurntableInteraction interaction = turntable.GetComponent<TurntableInteraction>();
        if (interaction == null)
        {
            interaction = Undo.AddComponent<TurntableInteraction>(turntable);
        }

        // The prompt reach needs a sphere TRIGGER (InteractionReach resizes it on
        // Start); the deck's own MeshCollider stays solid so the player cannot walk
        // through the gear.
        bool hasTrigger = false;
        foreach (SphereCollider sphere in turntable.GetComponents<SphereCollider>())
        {
            if (sphere.isTrigger) hasTrigger = true;
        }
        if (!hasTrigger)
        {
            SphereCollider trigger = Undo.AddComponent<SphereCollider>(turntable);
            trigger.isTrigger = true;   // radius re-derived in metres by InteractionReach
        }

        if (turntable.GetComponent<AudioSource>() == null)
        {
            AudioSource source = Undo.AddComponent<AudioSource>(turntable);
            source.playOnAwake = false;
            source.spatialBlend = 1f;   // the record plays from the deck
        }

        // The vinyl spins while a track plays, when the deck has one to spin.
        Transform record = turntable.transform.Find("Record");
        if (record != null)
        {
            SerializedObject serialized = new SerializedObject(interaction);
            SerializedProperty recordProperty = serialized.FindProperty("record");
            if (recordProperty != null && recordProperty.objectReferenceValue == null)
            {
                recordProperty.objectReferenceValue = record;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        EditorSceneManager.MarkSceneDirty(turntable.scene);
        Selection.activeObject = turntable;
        EditorGUIUtility.PingObject(turntable);
        Debug.Log("[LevelFeatureTools] Turntable wired: E toggles the record. Run Tools > " +
                  "Interactions > Add Missing Audio Profiles (creates the 'Turntable' " +
                  "section - the tape's replacement), author its tracks, and SAVE THE SCENE.",
                  turntable);
    }

    private static GameObject FindSceneObjectByName(string name)
    {
        foreach (GameObject candidate in Object.FindObjectsByType<GameObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate != null && candidate.name == name) return candidate;
        }

        return null;
    }

    // ---- Campfire cooking ----------------------------------------------------

    [MenuItem("Tools/Interactions/Add Campfire Cooking")]
    private static void AddCampfireCooking()
    {
        int added = 0;

        foreach (CampfireInteraction campfire in Object.FindObjectsByType<CampfireInteraction>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (campfire.GetComponent<CampfireCooking>() != null) continue;

            Undo.AddComponent<CampfireCooking>(campfire.gameObject);
            EditorSceneManager.MarkSceneDirty(campfire.gameObject.scene);
            added++;
        }

        Debug.Log(added > 0
            ? $"[LevelFeatureTools] Campfire Cooking added to {added} campfire(s). Author the " +
              "sizzle LOOP, done chime, collect and burn clips, then SAVE THE SCENE."
            : "[LevelFeatureTools] Every campfire already cooks (or none exist in the scene).");
    }

    // ---- Fish tiers ----------------------------------------------------------

    [MenuItem("Tools/Items/Seed Fish Tiers")]
    private static void SeedFishTiers()
    {
        // The rod is instantiated at runtime from FishingRodItem's prefab reference, so
        // the tiers must be seeded on the PREFAB - asked of the scene, not hard-coded.
        FishingRodItem rodItem = Object.FindFirstObjectByType<FishingRodItem>(FindObjectsInactive.Include);
        if (rodItem == null)
        {
            EditorUtility.DisplayDialog("Fish tiers", "No FishingRodItem in the open scene.", "OK");
            return;
        }

        SerializedObject rodItemSerialized = new SerializedObject(rodItem);
        SerializedProperty prefabProperty = rodItemSerialized.FindProperty("fishingRodPrefab");
        GameObject rodPrefab = prefabProperty != null ? prefabProperty.objectReferenceValue as GameObject : null;
        string prefabPath = rodPrefab != null ? AssetDatabase.GetAssetPath(rodPrefab) : null;
        if (string.IsNullOrEmpty(prefabPath))
        {
            EditorUtility.DisplayDialog("Fish tiers",
                "FishingRodItem has no fishing rod prefab assigned (or it is not an asset).", "OK");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            FishingRod rod = root.GetComponentInChildren<FishingRod>(true);
            if (rod == null)
            {
                Debug.LogError($"[LevelFeatureTools] No FishingRod on {prefabPath}.");
                return;
            }

            SerializedObject serialized = new SerializedObject(rod);
            SerializedProperty tiers = serialized.FindProperty("fishTiers");
            if (tiers == null || !tiers.isArray)
            {
                Debug.LogError("[LevelFeatureTools] FishingRod has no array field 'fishTiers' - " +
                               "renamed? This tool binds to it by name.");
                return;
            }

            // Only ever fills an EMPTY array - authored tiers are never overwritten.
            if (tiers.arraySize > 0)
            {
                Debug.Log("[LevelFeatureTools] The rod already has fish tiers; nothing changed.");
                return;
            }

            tiers.arraySize = 3;
            SeedTier(tiers.GetArrayElementAtIndex(0), "Perch",
                "A common catch. Restores a little hunger.", 0.6f, 25f, 1f, 1.8f);
            SeedTier(tiers.GetArrayElementAtIndex(1), "Silver Bass",
                "A decent catch. Restores a fair amount of hunger.", 0.3f, 40f, 1.15f, 1.4f);
            SeedTier(tiers.GetArrayElementAtIndex(2), "Golden Koi",
                "A rare prize. Restores a huge amount of hunger.", 0.1f, 70f, 1.4f, 0.9f);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            Debug.Log("[LevelFeatureTools] Seeded three fish tiers on the rod prefab (Perch 60% / " +
                      "Silver Bass 30% / Golden Koi 10%). Optional per-tier bite and catch clips " +
                      "and icons can be authored on it now.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void SeedTier(SerializedProperty tier, string name, string description,
                                 float weight, float nutrition, float waitMultiplier, float window)
    {
        tier.FindPropertyRelative("tierName").stringValue = name;
        tier.FindPropertyRelative("description").stringValue = description;
        tier.FindPropertyRelative("weight").floatValue = weight;
        tier.FindPropertyRelative("nutritionValue").floatValue = nutrition;
        tier.FindPropertyRelative("waitTimeMultiplier").floatValue = waitMultiplier;
        tier.FindPropertyRelative("catchWindowSeconds").floatValue = window;
    }

    // ---- Boss controller -----------------------------------------------------

    [MenuItem("Tools/Enemies/Add Boss Controller")]
    private static void AddBossController()
    {
        // The boss is whichever prefab the EnemyAudioManager's Boss-type profile names -
        // asked of the scene rather than hard-coding a path here.
        EnemyAudioManager manager =
            Object.FindFirstObjectByType<EnemyAudioManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Boss controller",
                "No EnemyAudioManager in the open scene.", "OK");
            return;
        }

        GameObject bossPrefab = FindBossPrefab(manager);
        if (bossPrefab == null)
        {
            EditorUtility.DisplayDialog("Boss controller",
                "No enemy profile with type Boss (and a prefab assigned) on the " +
                "EnemyAudioManager. Set one up, or add BossZombieController to the boss " +
                "prefab by hand.", "OK");
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(bossPrefab);
        if (string.IsNullOrEmpty(prefabPath))
        {
            EditorUtility.DisplayDialog("Boss controller",
                "The Boss profile's enemy prefab is a scene object, not a prefab asset - " +
                "add BossZombieController to it directly in the hierarchy instead.", "OK");
            return;
        }

        // Targeted prefab edit, so anything already authored on the boss survives.
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (root.GetComponent<BossZombieController>() != null)
            {
                Debug.Log("[LevelFeatureTools] The boss prefab already has a Boss Controller.");
                return;
            }

            root.AddComponent<BossZombieController>();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            Debug.Log("[LevelFeatureTools] Boss Controller added to '" + bossPrefab.name + "'. " +
                      "Author its clips (entrance roar, phase cracks, slam wind-up/impact, " +
                      "summon bellow), assign the standard zombie prefab as its minion, the " +
                      "optional silence/normal snapshots and a deactivated reward item - then " +
                      "SAVE.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>The prefab of the first Boss-type profile, via SerializedObject - the
    /// profile array is private, and this is the same by-name binding the other tools
    /// use (it fails loudly if the fields are renamed).</summary>
    private static GameObject FindBossPrefab(EnemyAudioManager manager)
    {
        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty profiles = serialized.FindProperty("enemyProfiles");
        if (profiles == null || !profiles.isArray)
        {
            Debug.LogError("[LevelFeatureTools] EnemyAudioManager has no array field " +
                           "'enemyProfiles' - it has been renamed, and this tool binds to it " +
                           "by name.", manager);
            return null;
        }

        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
            SerializedProperty type = profile.FindPropertyRelative("enemyType");
            SerializedProperty prefab = profile.FindPropertyRelative("enemyPrefab");

            if (type != null && prefab != null &&
                type.intValue == (int)EnemyAudioManager.EnemyType.Boss &&
                prefab.objectReferenceValue is GameObject found)
            {
                return found;
            }
        }

        return null;
    }

    // Fails loudly if a bound field is renamed, like the other tools here.
    private static void SetString(SerializedObject serialized, string field, string value)
    {
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"[LevelFeatureTools] PropSoundInteraction has no field '{field}' - " +
                           "it has been renamed, and this tool binds to it by name.");
            return;
        }

        property.stringValue = value;
    }

    private static void SetFloat(SerializedObject serialized, string field, float value)
    {
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"[LevelFeatureTools] PropSoundInteraction has no field '{field}' - " +
                           "it has been renamed, and this tool binds to it by name.");
            return;
        }

        property.floatValue = value;
    }
}
