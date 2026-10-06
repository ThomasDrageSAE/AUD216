using UnityEngine;
using TMPro;

/*
 * DebugDisplay.cs
 * 
 * Purpose: Provides in-game debug information display for development and testing
 * Used by: Development tools, WaveManager debugging
 * 
 * Key Features:
 * - Toggleable debug panel with backslash key
 * - Real-time wave system information display
 * - Singleton pattern for global access
 * 
 * Performance Considerations:
 * - Only updates when visible
 * - Minimal impact when hidden
 * 
 * Dependencies:
 * - Requires TextMeshPro
 * - WaveManager for zombie wave information
 */
public class DebugDisplay : MonoBehaviour
{
    public static DebugDisplay Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI debugText;
    [SerializeField] private GameObject debugPanel;
    
    private bool isVisible = false;
    private WaveManager waveManager;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Start hidden
        if (debugPanel != null)
        {
            debugPanel.SetActive(false);
        }

        // Dev diagnostic overlay - disable outside the editor and development builds.
        if (!Application.isEditor && !Debug.isDebugBuild)
        {
            enabled = false;
        }
    }

    private void Start()
    {
        waveManager = FindFirstObjectByType<WaveManager>();
    }

    private void Update()
    {
        // Toggle debug display with backslash key
        if (Input.GetKeyDown(KeyCode.Backslash))
        {
            isVisible = !isVisible;
            debugPanel.SetActive(isVisible);
        }

        if (isVisible)
        {
            UpdateDebugText();
        }
    }

    private void UpdateDebugText()
    {
        if (waveManager == null) return;

        string debugInfo = $"=== Wave Debug Info ===\n" +
                         $"Total Zombies for Night: {waveManager.GetTotalZombiesForNight()}\n" +
                         $"Zombies Currently Alive: {waveManager.GetZombiesAlive()}\n" +
                         $"Zombies Eliminated: {waveManager.GetTotalZombiesForNight() - waveManager.GetZombiesAlive()}\n" +
                         $"Pending Waves: {waveManager.GetPendingWavesCount()}\n" +
                         $"Is Wave Active: {waveManager.IsWaveActive()}\n" +
                         $"Current Wave: {waveManager.GetCurrentWaveIndex() + 1}\n" +
                         $"Night Started: {waveManager.IsNightStarted()}\n" +
                         $"Has Defense Objective: {waveManager.HasZombieDefenseObjective()}";

        debugText.text = debugInfo;
    }
} 