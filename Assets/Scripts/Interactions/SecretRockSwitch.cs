using UnityEngine;
using System.Collections;

public class SecretRockSwitch : MonoBehaviour
{
    [Header("Switch Settings")]
    [SerializeField] private float rotationAngle = 35f;
    [SerializeField] private float startAngle = -10f;
    [SerializeField] private float rotationDuration = 0.5f;
    [SerializeField] private KeyCode interactionKey = KeyCode.E;
    [SerializeField] private float interactionRange = 2f;

    [Header("Rock Door Settings")]
    [SerializeField] private Transform rockDoor;
    [SerializeField] private float doorMoveDistance = 3f;
    [SerializeField] private float doorMoveDuration = 3f;

    [Header("Time Gate Settings")]
    [SerializeField] private DayNightCycle dayNightCycle;
    [SerializeField] private int requiredDay = 2;
    [SerializeField] private float requiredTimeOfDay = 0.1f;

    [Header("Interaction Settings")]
    [SerializeField] private string displayName = "Rock Switch";
    [SerializeField] private string actionMessage = "activate";
    [SerializeField] private string lockedMessage = "Hmm, the rock seems stuck...";

    [Header("References")]
    [SerializeField] private Animator doorAnimator;
    [SerializeField] private AudioSource audioSource;

    [Header("Audio Settings")]
    [SerializeField] private string audioProfileName = "Cave Door"; // Must match exactly the profile name in InteractionAudioManager
    [Tooltip("Compile-checked identity. When set, this wins over the name above - which " +
             "stays as the fallback for anything not yet migrated.")]
    [SerializeField] private InteractionId audioProfileId = InteractionId.Unassigned;

    /// <summary>
    /// The key handed to InteractionAudioManager: the id when one is assigned, otherwise
    /// the authored string. Both resolve through the same lookup.
    /// </summary>
    private string AudioProfileKey => AudioProfileIds.IsAssigned(audioProfileId)
        ? AudioProfileIds.NameOf(audioProfileId)
        : audioProfileName;
    private InteractionAudioManager audioManager;

    [Header("Prompt Settings")]
    [SerializeField] private string inputKey = "E";
    [SerializeField] private string promptMessage = "activate switch";

    [Header("Time Gate Settings")]
    [SerializeField] private bool useTimeGate = true;

    private bool isActivated = false;
    private bool isMoving = false;
    private Quaternion targetRotation;
    private Coroutine rotationCoroutine;
    private Coroutine doorCoroutine;
    private Coroutine promptCoroutine;
    private bool playerInRange = false;
    private Transform playerTransform;
    private Vector3 doorStartPosition;

    private void Start()
    {
        targetRotation = Quaternion.Euler(startAngle, transform.localRotation.eulerAngles.y, transform.localRotation.eulerAngles.z);
        transform.localRotation = targetRotation;

        if (rockDoor != null)
        {
            doorStartPosition = rockDoor.position;
        }
        else
        {
            Debug.LogError("Rock Door not assigned to SecretRockSwitch!");
        }

        if (dayNightCycle == null)
        {
            dayNightCycle = FindFirstObjectByType<DayNightCycle>();
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }

        // Get references
        audioManager = FindFirstObjectByType<InteractionAudioManager>();

        // Ensure we have an AudioSource
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // Register with audio manager
        if (audioManager != null)
        {
            audioManager.RegisterAudioSource(AudioProfileKey, audioSource);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(interactionKey) && PlayerInRange())
        {
            if (CanInteract())
            {
                if (!isMoving)
                {
                    ToggleSwitch();
                }
            }
            else
            {
                // Show locked message
                if (UIManager.Instance != null)
                {
                    UIManager.Instance.DisplayInteractionPrompt(lockedMessage);
                    // Spamming E used to stack these; they then fought over the
                    // prompt text. Same stop-then-start the switch's other two
                    // coroutines already use.
                    if (promptCoroutine != null) StopCoroutine(promptCoroutine);
                    promptCoroutine = StartCoroutine(HidePromptAfterDelay(2f));
                }
            }
        }
    }

    private bool CanInteract()
    {
        if (!useTimeGate) return true;
        
        if (dayNightCycle == null) return false;

        int currentDay = dayNightCycle.GetCurrentDay();
        float currentTime = dayNightCycle.GetTimeOfDay();

        return currentDay > requiredDay || (currentDay == requiredDay && currentTime >= requiredTimeOfDay);
    }

    private IEnumerator HidePromptAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (UIManager.Instance != null)
        {
            if (playerInRange)
            {
                UIManager.Instance.DisplayInteractionPrompt($"Press {inputKey} to {promptMessage} {displayName}");
            }
            else
            {
                UIManager.Instance.HidePrompt();
            }
        }
    }

    private void ToggleSwitch()
    {
        if (rotationCoroutine != null)
        {
            StopCoroutine(rotationCoroutine);
        }

        isActivated = !isActivated;
        
        // Play the interaction sound
        if (audioManager != null)
        {
            audioManager.PlaySimpleInteraction(AudioProfileKey, audioSource);
        }
        
        Quaternion newTarget = isActivated ? 
            Quaternion.Euler(startAngle + rotationAngle, transform.localRotation.eulerAngles.y, transform.localRotation.eulerAngles.z) :
            Quaternion.Euler(startAngle, transform.localRotation.eulerAngles.y, transform.localRotation.eulerAngles.z);
            
        rotationCoroutine = StartCoroutine(RotateSwitch(newTarget));

        // Start door movement
        if (rockDoor != null)
        {
            if (doorCoroutine != null)
            {
                StopCoroutine(doorCoroutine);
            }
            doorCoroutine = StartCoroutine(MoveDoor());
        }
    }

    private IEnumerator RotateSwitch(Quaternion targetRotation)
    {
        Quaternion startRotation = transform.localRotation;
        float elapsedTime = 0f;

        while (elapsedTime < rotationDuration)
        {
            transform.localRotation = Quaternion.Slerp(startRotation, targetRotation, elapsedTime / rotationDuration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.localRotation = targetRotation;
    }

    private IEnumerator MoveDoor()
    {
        isMoving = true;

        Vector3 startPos = rockDoor.position;
        Vector3 targetPos = isActivated ? 
            doorStartPosition + Vector3.down * doorMoveDistance : 
            doorStartPosition;

        float elapsedTime = 0f;

        while (elapsedTime < doorMoveDuration)
        {
            rockDoor.position = Vector3.Lerp(startPos, targetPos, elapsedTime / doorMoveDuration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        rockDoor.position = targetPos;
        isMoving = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            if (UIManager.Instance != null && !isMoving)
            {
                UIManager.Instance.DisplayInteractionPrompt($"Press E to {actionMessage} {displayName}");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            if (UIManager.Instance != null)
            {
                UIManager.Instance.HidePrompt();
            }
        }
    }

    private bool PlayerInRange()
    {
        if (!playerInRange || playerTransform == null)
            return false;

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
        return distanceToPlayer <= interactionRange;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRange);

        if (rockDoor != null)
        {
            // Draw the door's movement path
            Gizmos.color = Color.blue;
            Vector3 doorStart = rockDoor.position;
            Vector3 doorEnd = doorStart + Vector3.down * doorMoveDistance;
            Gizmos.DrawLine(doorStart, doorEnd);
            Gizmos.DrawWireSphere(doorEnd, 0.2f);
        }
    }
}