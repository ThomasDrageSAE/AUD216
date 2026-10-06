using UnityEngine;

public class TriggerManager : MonoBehaviour
{
    public AmbienceManager.AmbienceState targetState;
    private AmbienceManager ambienceManager;
    private MusicManager musicManager;

    // Helper property to determine if the ambience state should use indoor music
    private bool ShouldUseIndoorMusic => targetState == AmbienceManager.AmbienceState.Cave;

    void Start()
    {
        ambienceManager = FindFirstObjectByType<AmbienceManager>();
        musicManager = FindFirstObjectByType<MusicManager>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Handle ambience state change
            if (ambienceManager != null)
            {
                ambienceManager.ChangeState(targetState);
            }

            // Handle music state change. SetIndoorState is the only music call a trigger
            // makes: the manager derives the state from its flags, so combat is never
            // stomped by crossing a zone (the old direct ChangeState here ignored
            // isFighting and dropped Fighting -> Exploring mid-fight).
            if (musicManager != null)
            {
                musicManager.SetIndoorState(ShouldUseIndoorMusic);
            }
        }
    }
}
