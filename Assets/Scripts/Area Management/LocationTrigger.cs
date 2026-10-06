using UnityEngine;

public class LocationTrigger : MonoBehaviour
{
    public bool isIndoor = false;
    private MusicManager musicManager;

    void Start()
    {
        musicManager = FindFirstObjectByType<MusicManager>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && musicManager != null)
        {
            if (isIndoor)
            {
                musicManager.SetIndoorState(true);
            }
            else
            {
                musicManager.SetIndoorState(false);
            }
        }
    }
} 