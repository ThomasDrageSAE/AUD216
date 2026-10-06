using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The round minimap in the top-right corner: a live top-down view of the ground around the
/// player that turns with them - forward is up, the way a car's satnav is drawn - with the
/// player's view cone fixed at the centre, enemies as red dots and objectives as yellow
/// markers. North-up is a checkbox away (<see cref="rotateWithPlayer"/>): then the camera
/// never yaws and the cone turns instead. The Synty lights-and-wires dial with its demo map
/// swapped for a camera - Tools/HUD/Add Minimap builds it and wires everything here.
///
/// The map is a second camera looking straight down from above the player, rendering into a
/// small RenderTexture that a RawImage shows inside the dial's circular mask. Orthographic,
/// so metres are pixels: <see cref="viewRadius"/> metres of ground fill the mask's radius,
/// and the same number places the icons - an enemy 20 m east is drawn 20 m-worth of pixels
/// to the right of the centre, on the same scale the map is drawn at, so dot and ground
/// agree. The camera yaws by the heading, and the icon offsets are turned by the same angle,
/// so the map and the dots turn as one thing.
///
/// Cost, because this level has just had a performance pass: the camera renders 256 x 256
/// with no shadows, no post-processing, no MSAA, and only every <see cref="renderEveryNthFrame"/>
/// frames - a disabled camera keeps its last picture in the texture, so skipping frames costs
/// nothing but freshness. Enemies are re-found four times a second by tag, exactly as
/// EnemyRadar does, and only their positions are read per frame.
///
/// Scene-lifetime component: no DontDestroyOnLoad, and every singleton is null-checked at the
/// dereference (WorldSpaceObjectiveManager.Instance is null for the first frames and after a
/// reload - see the header of ObjectiveManager.cs).
/// </summary>
public class MinimapView : MonoBehaviour
{
    [Header("Wiring (Tools/HUD/Add Minimap fills these)")]

    [Tooltip("Whose surroundings are shown. The Player root.")]
    [SerializeField] private Transform player;

    [Tooltip("The camera looking straight down. Its position and orthographic size are driven from here.")]
    [SerializeField] private Camera minimapCamera;

    [Tooltip("The RawImage inside the dial's mask that shows the camera's picture.")]
    [SerializeField] private RawImage mapImage;

    [Tooltip("The rect the icons are placed in - the dial's circular mask.")]
    [SerializeField] private RectTransform mask;

    [Tooltip("Where enemy and objective icons are parented.")]
    [SerializeField] private RectTransform elements;

    [Tooltip("The player marker's ICN child - the view cone and icon that turn with the heading.")]
    [SerializeField] private RectTransform playerMarker;

    [SerializeField] private GameObject enemyIconPrefab;
    [SerializeField] private GameObject objectiveIconPrefab;

    [Header("View")]

    [Tooltip("On: the map turns with the player so forward is always up and the marker stays " +
             "put (the owner's call, 2026-09-06). Off: north is up and the marker's cone turns.")]
    [SerializeField] private bool rotateWithPlayer = true;

    [Tooltip("Metres of ground from the player to the edge of the map. The map and the icons share it.")]
    [Min(5f)] [SerializeField] private float viewRadius = 35f;

    [Tooltip("How far above the player the camera sits. High enough to clear the tallest tree; " +
             "the far plane below reaches the deepest water.")]
    [SerializeField] private float cameraHeight = 80f;

    [Tooltip("Pixels of the map texture along each side. 256 is plenty for a 165 px circle.")]
    [SerializeField] private int textureSize = 256;

    [Tooltip("Render the map every Nth frame. Measured 2026-09-06: a render costs about 3 ms " +
             "of a frame (terrain, trees and props from above), so 2 was 1.5 ms every frame " +
             "and 3 is about 1 ms - a map refreshed 20 times a second at 60 fps, which nobody " +
             "reading a minimap can tell from 60.")]
    [Min(1)] [SerializeField] private int renderEveryNthFrame = 3;

    [Header("Icons")]

    [Tooltip("Seconds between re-finding the enemies by tag. Their positions update every frame.")]
    [Min(0.05f)] [SerializeField] private float enemyRefreshInterval = 0.25f;

    [Tooltip("Icons for things beyond the edge sit on the rim, this many pixels in from it.")]
    [SerializeField] private float rimInset = 18f;

    private RenderTexture texture;
    private float pixelsPerMetre;
    private float nextEnemyRefresh;

    private readonly List<Transform> enemies = new List<Transform>();
    private readonly List<ZombieAI> enemyAis = new List<ZombieAI>();
    private readonly List<RectTransform> enemyIcons = new List<RectTransform>();
    private readonly Dictionary<string, RectTransform> objectiveIcons = new Dictionary<string, RectTransform>();
    private readonly List<string> staleObjectives = new List<string>();

    /// <summary>The heading in degrees clockwise from north, for the setup tool's play check.</summary>
    public float HeadingDegrees { get; private set; }

    /// <summary>Metres to pixels on the mask, for the setup tool's play check.</summary>
    public float PixelsPerMetre => pixelsPerMetre;

    /// <summary>Whether the map turns with the player, for the play check.</summary>
    public bool RotatesWithPlayer => rotateWithPlayer;

    private void Awake()
    {
        if (player == null)
        {
            GameObject tagged = GameObject.FindWithTag("Player");
            if (tagged != null) player = tagged.transform;
        }

        // Metres to pixels: the mask's radius is viewRadius metres. Read once - the mask is a
        // fixed rect - and after the canvas has laid out, which Awake is early for on the
        // first frame; LateUpdate re-reads it while it is zero.
        pixelsPerMetre = MaskRadius() / viewRadius;

        BuildTexture();
    }

    private void OnDestroy()
    {
        if (minimapCamera != null) minimapCamera.targetTexture = null;
        if (texture != null) texture.Release();
    }

    private void BuildTexture()
    {
        if (minimapCamera == null || mapImage == null) return;

        // Made here rather than an asset: nothing else reads it, and a RenderTexture asset in
        // the project is one more thing to keep in step with textureSize.
        texture = new RenderTexture(Mathf.Max(64, textureSize), Mathf.Max(64, textureSize), 16, RenderTextureFormat.ARGB32)
        {
            name = "Minimap",
            filterMode = FilterMode.Bilinear,
            antiAliasing = 1,
        };
        texture.Create();

        minimapCamera.targetTexture = texture;
        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = viewRadius;
        mapImage.texture = texture;
    }

    private void LateUpdate()
    {
        if (player == null) return;

        if (pixelsPerMetre <= 0f) pixelsPerMetre = MaskRadius() / viewRadius;

        // Heading: clockwise from north, as a bearing is read.
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        if (forward.sqrMagnitude > 1e-6f) HeadingDegrees = Vector3.SignedAngle(Vector3.forward, forward.normalized, Vector3.up);

        // The camera: above the player, looking straight down. Euler(90, yaw, 0) is pitch
        // first (look down, with the camera's up on world north) and then yaw about world Y,
        // which swings that up onto the player's forward - so forward is the top of the
        // picture. With rotateWithPlayer off the yaw is zero and north stays up. Set every
        // frame whether or not the camera renders this one, so a skipped frame's pose is
        // still right when the next render comes round.
        float mapYaw = rotateWithPlayer ? HeadingDegrees : 0f;
        if (minimapCamera != null)
        {
            Vector3 p = player.position;
            minimapCamera.transform.SetPositionAndRotation(
                new Vector3(p.x, p.y + cameraHeight, p.z),
                Quaternion.Euler(90f, mapYaw, 0f));
            minimapCamera.orthographicSize = viewRadius;
            minimapCamera.enabled = renderEveryNthFrame <= 1 || Time.frameCount % renderEveryNthFrame == 0;
        }

        // The marker. Map turning: the cone points up, always - the world turns under it.
        // North up: the cone turns by MINUS the heading (UI is anti-clockwise positive, a
        // bearing is clockwise), so a player facing east has a cone pointing right.
        if (playerMarker != null) playerMarker.localRotation = Quaternion.Euler(0f, 0f, rotateWithPlayer ? 0f : -HeadingDegrees);

        UpdateEnemies();
        UpdateObjectives();
    }

    // ---- enemies ---------------------------------------------------------------

    private void UpdateEnemies()
    {
        if (Time.time >= nextEnemyRefresh)
        {
            nextEnemyRefresh = Time.time + enemyRefreshInterval;
            RefreshEnemies();
        }

        for (int i = 0; i < enemies.Count; i++)
        {
            RectTransform icon = enemyIcons[i];
            Transform enemy = enemies[i];
            bool alive = enemy != null && (enemyAis[i] == null || !enemyAis[i].IsDead());

            if (icon == null) continue;
            if (!alive)
            {
                if (icon.gameObject.activeSelf) icon.gameObject.SetActive(false);
                continue;
            }

            if (!icon.gameObject.activeSelf) icon.gameObject.SetActive(true);
            icon.anchoredPosition = ToMap(enemy.position);
        }
    }

    /// <summary>
    /// The same source as EnemyRadar, for the same reason it gives: the "Enemy" tag is what
    /// WaveManager puts on every spawned zombie, so tag and refresh can never disagree.
    /// Icons are pooled - one per slot, hidden rather than destroyed when its enemy dies.
    /// </summary>
    private void RefreshEnemies()
    {
        GameObject[] tagged = GameObject.FindGameObjectsWithTag("Enemy");

        enemies.Clear();
        enemyAis.Clear();

        foreach (GameObject go in tagged)
        {
            ZombieAI ai = go.GetComponent<ZombieAI>();
            if (ai == null) ai = go.GetComponentInChildren<ZombieAI>();
            if (ai != null && ai.IsDead()) continue;

            enemies.Add(go.transform);
            enemyAis.Add(ai);
        }

        while (enemyIcons.Count < enemies.Count)
        {
            RectTransform icon = MakeIcon(enemyIconPrefab, "Map_Icon_Enemy");
            if (icon == null) break;
            enemyIcons.Add(icon);
        }

        for (int i = enemies.Count; i < enemyIcons.Count; i++)
        {
            if (enemyIcons[i] != null && enemyIcons[i].gameObject.activeSelf) enemyIcons[i].gameObject.SetActive(false);
        }
    }

    // ---- objectives ------------------------------------------------------------

    /// <summary>
    /// The same source as ObjectiveRadar: WorldSpaceObjectiveManager's active markers, one icon
    /// per key, removed when the key goes. Null-guarded at the dereference - the manager is
    /// null for the first frames and after a reload.
    /// </summary>
    private void UpdateObjectives()
    {
        WorldSpaceObjectiveManager manager = WorldSpaceObjectiveManager.Instance;
        if (manager == null) return;

        foreach (KeyValuePair<string, WorldSpaceObjectiveMarker> entry in manager.ActiveMarkers)
        {
            if (entry.Value == null) continue;

            if (!objectiveIcons.TryGetValue(entry.Key, out RectTransform icon) || icon == null)
            {
                icon = MakeIcon(objectiveIconPrefab, "Map_Icon_Objective");
                if (icon == null) return;
                objectiveIcons[entry.Key] = icon;
            }

            if (!icon.gameObject.activeSelf) icon.gameObject.SetActive(true);
            icon.anchoredPosition = ToMap(entry.Value.transform.position);
        }

        staleObjectives.Clear();
        foreach (KeyValuePair<string, RectTransform> entry in objectiveIcons)
        {
            if (!manager.ActiveMarkers.ContainsKey(entry.Key)) staleObjectives.Add(entry.Key);
        }

        foreach (string key in staleObjectives)
        {
            if (objectiveIcons[key] != null) Destroy(objectiveIcons[key].gameObject);
            objectiveIcons.Remove(key);
        }
    }

    // ---- geometry --------------------------------------------------------------

    /// <summary>
    /// World position to a point on the mask: the player is the centre, and anything beyond
    /// the edge sits on the rim so it can still be seen coming. With the map turning, the
    /// offset is turned by the heading so that something dead ahead is straight up - the same
    /// turn the camera made, so dot and ground stay together. North up, east is right.
    /// </summary>
    private Vector2 ToMap(Vector3 world)
    {
        Vector3 p = player.position;
        Vector2 offset = new Vector2(world.x - p.x, world.z - p.z) * pixelsPerMetre;

        if (rotateWithPlayer)
        {
            // Anti-clockwise by the heading: the player's forward, (sin h, cos h) in
            // (east, north), lands on (0, 1). Checked in play with a marker placed ahead.
            float radians = HeadingDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            offset = new Vector2(offset.x * cos - offset.y * sin, offset.x * sin + offset.y * cos);
        }

        float limit = MaskRadius() - rimInset;
        if (limit > 0f && offset.sqrMagnitude > limit * limit) offset = offset.normalized * limit;

        return offset;
    }

    private float MaskRadius()
    {
        if (mask == null) return 0f;
        return Mathf.Min(mask.rect.width, mask.rect.height) * 0.5f;
    }

    private RectTransform MakeIcon(GameObject prefab, string fallbackName)
    {
        if (prefab == null || elements == null) return null;

        GameObject icon = Instantiate(prefab, elements);
        icon.name = fallbackName;

        RectTransform rect = icon.GetComponent<RectTransform>();
        if (rect == null) rect = icon.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        return rect;
    }
}
