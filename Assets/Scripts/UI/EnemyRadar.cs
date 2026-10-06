using UnityEngine;
using UnityEngine.UI;

public class EnemyRadar : MonoBehaviour
{
    [Header("References")]
    public Transform player;                    
    public RectTransform radarCanvas;          
    public RectTransform compassBar;           // This will be our parent for indicators
    public GameObject enemyIndicatorPrefab;     
    
    [Header("Settings")]
    public float maxDistance = 50f;            
    public float compassBarWidth;              
    
    private GameObject[] enemies;
    private GameObject[] indicators;
    private ZombieAI[] enemyAIs;              // cached at refresh - no per-frame GetComponent
    private RectTransform[] indicatorRects;   // cached at refresh
    private int lastEnemyCount = 0;

    // Refreshing every frame is expensive; only re-check the enemy count a few
    // times per second. Dead/destroyed enemies are still handled every frame
    // via the cached arrays.
    private float nextCountCheckTime;
    private const float CountCheckInterval = 0.25f;

    void Start()
    {
        if (compassBarWidth == 0)
            compassBarWidth = compassBar.rect.width;

        RefreshEnemies(GameObject.FindGameObjectsWithTag("Enemy"));
        lastEnemyCount = enemies.Length;
    }

    void LateUpdate()
    {
        // Periodically check whether the enemy set changed (spawns).
        // Uses the same "Enemy" tag as the refresh so the two can't disagree
        // (they previously used tag vs layer, forcing a refresh every frame).
        if (Time.time >= nextCountCheckTime)
        {
            nextCountCheckTime = Time.time + CountCheckInterval;
            GameObject[] tagged = GameObject.FindGameObjectsWithTag("Enemy");
            if (tagged.Length != lastEnemyCount)
            {
                RefreshEnemies(tagged);
                lastEnemyCount = tagged.Length;
                return;
            }
        }

        if (enemies == null) return;

        // Update indicators and check for destroyed or dead enemies
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] == null && indicators[i] != null)
            {
                // Enemy was destroyed, remove its indicator
                Destroy(indicators[i]);
                indicators[i] = null;
                continue;
            }

            if (enemies[i] != null && indicators[i] != null)
            {
                // Check if enemy is dead - if so, destroy indicator
                if (enemyAIs[i] != null && enemyAIs[i].IsDead())
                {
                    Destroy(indicators[i]);
                    indicators[i] = null;
                    continue;
                }

                // Update indicator position for living enemies
                UpdateIndicatorPosition(enemies[i], indicators[i], indicatorRects[i]);
            }
        }
    }

    void RefreshEnemies(GameObject[] tagged)
    {
        if (compassBar == null)
        {
            Debug.LogError("Compass Bar is not assigned!");
            return;
        }

        // Clean up existing indicators
        if (indicators != null)
        {
            for (int i = 0; i < indicators.Length; i++)
            {
                if (indicators[i] != null)
                {
                    Destroy(indicators[i]);
                }
            }
        }

        // Keep only living enemies
        var enemyList = new System.Collections.Generic.List<GameObject>();
        var aiList = new System.Collections.Generic.List<ZombieAI>();
        foreach (GameObject obj in tagged)
        {
            ZombieAI zombieAI = GetZombieAI(obj);
            if (zombieAI != null && zombieAI.IsDead()) continue;
            enemyList.Add(obj);
            aiList.Add(zombieAI);
        }

        enemies = enemyList.ToArray();
        enemyAIs = aiList.ToArray();

        // Create new indicators under the compass bar
        indicators = new GameObject[enemies.Length];
        indicatorRects = new RectTransform[enemies.Length];
        for (int i = 0; i < enemies.Length; i++)
        {
            indicators[i] = Instantiate(enemyIndicatorPrefab, compassBar);
            indicators[i].SetActive(true);
            indicatorRects[i] = indicators[i].GetComponent<RectTransform>();
        }
    }

    void UpdateIndicatorPosition(GameObject enemy, GameObject indicator, RectTransform indicatorRect)
    {
        // Get direction to enemy
        Vector3 directionToEnemy = enemy.transform.position - player.position;
        float distanceToEnemy = directionToEnemy.magnitude;

        // Only show indicator if enemy is within range
        if (distanceToEnemy <= maxDistance)
        {
            indicator.SetActive(true);

            // Calculate angle between forward and enemy direction
            Vector3 forwardVector = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
            Vector3 enemyVector = Vector3.ProjectOnPlane(directionToEnemy, Vector3.up).normalized;
            
            // Get the angle between player's forward and direction to enemy
            float angleToEnemy = Vector3.SignedAngle(forwardVector, enemyVector, Vector3.up);
            float indicatorPosition = (angleToEnemy / 180f) * compassBarWidth;

            // Clamp to edges when beyond ±58 degrees
            if (angleToEnemy > 58f)
            {
                indicatorPosition = compassBarWidth * 0.322f; // Adjusted for 58 degrees (58/180)
            }
            else if (angleToEnemy < -58f)
            {
                indicatorPosition = -compassBarWidth * 0.322f; // Adjusted for -58 degrees (-58/180)
            }

            // Update indicator position - keep the original Y position
            float currentY = indicatorRect.anchoredPosition.y;
            indicatorRect.anchoredPosition = new Vector2(indicatorPosition, currentY);

            // Optional: Scale indicator based on distance
            float scale = 1 - (distanceToEnemy / maxDistance);
            indicatorRect.localScale = new Vector3(scale, scale, 1);
        }
        else
        {
            indicator.SetActive(false);
        }
    }

    // Helper method to find ZombieAI component on an enemy GameObject
    private ZombieAI GetZombieAI(GameObject enemy)
    {
        if (enemy == null) return null;
        
        ZombieAI zombieAI = enemy.GetComponent<ZombieAI>();
        if (zombieAI != null) return zombieAI;
        
        zombieAI = enemy.GetComponentInChildren<ZombieAI>();
        if (zombieAI != null) return zombieAI;
        
        zombieAI = enemy.GetComponentInParent<ZombieAI>();
        return zombieAI;
    }

    // Call this method when new enemies spawn or die
    public void OnEnemiesChanged()
    {
        GameObject[] tagged = GameObject.FindGameObjectsWithTag("Enemy");
        RefreshEnemies(tagged);
        lastEnemyCount = tagged.Length;
    }
}