using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * WellOffering.cs
 *
 * Purpose: Drop an item near the well's rim (double-tap its number key) and the well takes it - a beat
 *          of quiet, then a splash delayed by the depth of the shaft. At the next dawn
 *          the well gives back: usually the item itself, occasionally something better.
 * Used by: the Well prefab; added with Tools > Interactions > Add Well Offering.
 *
 * The splash goes through the InteractionAudioManager under its own "Well Splash"
 * profile, so it is an authorable slot: it appears in the Ctrl+G report, and until a
 * clip is dropped in, Ctrl+J shows a red miss naming exactly what to author. The delay
 * IS the sound design - depthSeconds of silence between swallow and splash is what
 * makes the well feel deep.
 *
 * Listens to Item.OnDropped rather than polling: dropping is the only way an item
 * appears near the rim at runtime. Its own dawn returns re-use Item.Drop, so a guard
 * keeps the well from swallowing what it has just given back.
 */
[DisallowMultipleComponent]
public class WellOffering : MonoBehaviour
{
    [Tooltip("How close to the well a dropped item must land to be taken, in metres")]
    [Min(0.5f)] [SerializeField] private float offerRadius = 3f;

    [Tooltip("Seconds between the drop and the item tumbling in")]
    [Min(0f)] [SerializeField] private float tumbleSeconds = 0.8f;

    [Tooltip("Seconds of fall before the splash - the audible depth of the well")]
    [Min(0f)] [SerializeField] private float depthSeconds = 1.4f;

    [Header("Audio")]
    [SerializeField] private string splashProfileName = "Well Splash";

    [Tooltip("Compile-checked identity. When set, this wins over the name above")]
    [SerializeField] private InteractionId splashProfileId = InteractionId.Unassigned;

    [Header("Dawn return")]
    [Tooltip("Chance an offering comes back as one of Reward Items instead of itself - " +
             "the original is then consumed")]
    [Range(0f, 1f)] [SerializeField] private float rewardChance = 0.35f;

    [Tooltip("DEACTIVATED scene items the well can return. Each is used at most once - " +
             "an active one is already out in the world")]
    [SerializeField] private Item[] rewardItems;

    private string SplashKey => AudioProfileIds.IsAssigned(splashProfileId)
        ? AudioProfileIds.NameOf(splashProfileId)
        : splashProfileName;

    private struct Offering
    {
        public Item item;
        public Vector3 rimPoint;
    }

    private readonly List<Offering> held = new List<Offering>();
    private DayNightCycle dayNight;
    private InteractionAudioManager audioManager;
    private AudioSource wellSource;
    private bool returning;

    private void Start()
    {
        audioManager = FindFirstObjectByType<InteractionAudioManager>();

        dayNight = FindFirstObjectByType<DayNightCycle>();
        if (dayNight != null) dayNight.OnDawnAfterNight += ReturnOfferings;

        // PlaySimpleInteraction dereferences the source it is handed whenever the profile
        // has no custom one, and the splash should come FROM the well - PropSoundInteraction
        // usually created this source already, so this is normally a lookup, not an add.
        wellSource = GetComponent<AudioSource>();
        if (wellSource == null)
        {
            wellSource = gameObject.AddComponent<AudioSource>();
            wellSource.playOnAwake = false;
            wellSource.spatialBlend = 1f;
        }

        Item.OnDropped += OnItemDropped;
    }

    private void OnDestroy()
    {
        Item.OnDropped -= OnItemDropped;
        if (dayNight != null) dayNight.OnDawnAfterNight -= ReturnOfferings;
    }

    private void OnItemDropped(Item item)
    {
        if (returning || item == null) return;

        Vector3 toItem = item.transform.position - transform.position;
        if (toItem.sqrMagnitude > offerRadius * offerRadius) return;

        StartCoroutine(SwallowRoutine(item));
    }

    private IEnumerator SwallowRoutine(Item item)
    {
        Vector3 rimPoint = item.transform.position;

        yield return new WaitForSeconds(tumbleSeconds);

        // Snatched back up (or destroyed) during the tumble: the well misses out.
        if (item == null || item.IsCollected) yield break;

        item.gameObject.SetActive(false);
        held.Add(new Offering { item = item, rimPoint = rimPoint });

        // The offering is made the moment the well takes it, not at the dawn payout.
        // Through the tracker, so an offering made BEFORE day 3 assigns the objective
        // still counts when it appears (retroactive completion, like every other first).
        if (FirstTimeInteractionTracker.Instance != null)
        {
            FirstTimeInteractionTracker.Instance.OnWellOfferingMade();
        }
        else
        {
            ObjectiveManager.Instance?.CompleteObjective(ObjectiveConstants.MAKE_WELL_OFFERING);
        }

        // The silence here is the depth of the shaft.
        yield return new WaitForSeconds(depthSeconds);

        if (audioManager != null)
        {
            audioManager.PlaySimpleInteraction(SplashKey, wellSource);
        }
    }

    private void ReturnOfferings()
    {
        if (held.Count == 0) return;

        // Our own Drop calls below raise Item.OnDropped at the rim; without this the
        // well would swallow everything it had just given back.
        returning = true;

        try
        {
            foreach (Offering offering in held)
            {
                if (offering.item == null) continue;

                Item returned = offering.item;

                if (rewardItems != null && rewardItems.Length > 0 && Random.value < rewardChance)
                {
                    Item reward = rewardItems[Random.Range(0, rewardItems.Length)];

                    // Only a still-deactivated reward is available; an active one is
                    // already out in the world from an earlier dawn.
                    if (reward != null && !reward.gameObject.activeSelf)
                    {
                        Destroy(offering.item.gameObject);
                        returned = reward;
                    }
                }

                returned.gameObject.SetActive(true);
                returned.Drop(offering.rimPoint);
            }
        }
        finally
        {
            returning = false;
        }

        held.Clear();
    }
}
