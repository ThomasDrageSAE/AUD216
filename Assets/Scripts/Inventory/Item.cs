using UnityEngine;

// Change from abstract to regular class
public class Item : MonoBehaviour
{
    [SerializeField] protected string itemName;
    [SerializeField] protected Sprite itemIcon;
    [SerializeField] protected string itemDescription;
    
    [Header("Objective")]
    [SerializeField] protected string objectiveId;
    
    protected virtual void Awake()
    {
        // Only show warning if item has a name but no icon
        if (itemIcon == null && !string.IsNullOrEmpty(itemName))
        {
            Debug.LogWarning($"No icon assigned for item: {itemName}");
        }
    }

    private bool collected;

    /// <summary>True while the item sits in an inventory rather than in the world.</summary>
    public bool IsCollected => collected;

    /// <summary>
    /// Raised after an item lands back in the world (end of Drop). WellOffering listens
    /// for drops near the rim. Static, so it is reset on load like the project's other
    /// reset-on-load statics.
    /// </summary>
    public static event System.Action<Item> OnDropped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => OnDropped = null;

    // Set by Drop. Re-enabling a trigger under the player's feet fires OnTriggerEnter
    // on the next physics step, which would collect the item back in the same breath;
    // this window swallows that one event. Standing still past it changes nothing -
    // no new Enter fires until the player steps out of the trigger and back in.
    private float dropProtectionUntil;

    private void OnTriggerEnter(Collider other)
    {
        // Trigger callbacks are dispatched per collider pair, and the player carries both
        // a CharacterController and a CapsuleCollider, so one walk-through can deliver
        // this twice. Without the guard the same item took two slots, played the pickup
        // sound twice, and left a slot holding a destroyed Item once it was consumed.
        if (collected || Time.time < dropProtectionUntil) return;

        if (other.CompareTag("Player"))
        {
            Inventory inventory = other.GetComponent<Inventory>();

            // First refusal for anything that stacks. The grenade is the only such item:
            // a second one found in the world folds into the count the player is already
            // carrying instead of taking a slot of its own. Returning true means the
            // pickup has dealt with itself, sound and destruction included, so nothing
            // below runs; every other item inherits the false and is untouched by this.
            if (inventory != null && TryMergeIntoInventory(inventory)) return;

            if (inventory != null && inventory.AddItem(this))
            {
                collected = true;

                // Play pickup sound
                if (UIAudioManager.Instance != null)
                {
                    UIAudioManager.Instance.PlayPickupSound();
                }
                
                OnPickup();
                
                // Check if this item completes an objective
                if (!string.IsNullOrEmpty(objectiveId) && ObjectiveManager.Instance != null)
                {
                    try
                    {
                        ObjectiveManager.Instance.CompleteObjective(objectiveId);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Failed to complete objective {objectiveId}: {e.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// A chance to fold this pickup into something the inventory already holds instead of
    /// taking a slot for it. The inventory has no stacking of its own - AddItem is "first
    /// empty slot" and nothing else - so an item that stacks answers the question itself,
    /// and one that does not never notices this exists.
    /// </summary>
    /// <returns>
    /// True when the pickup has been handled here and must NOT be added to a slot. The
    /// override owns everything that follows from that, including the pickup sound and
    /// destroying itself, because "merged into the count" and "left on the ground because
    /// the player is full" are both handled and want opposite endings.
    /// </returns>
    protected virtual bool TryMergeIntoInventory(Inventory inventory) => false;

    /// <summary>
    /// Puts a collected item back into the world at the given spot - the mirror of
    /// OnPickup. The item and its inventory entry are the SAME GameObject (OnPickup
    /// only hides it), so dropping is re-showing it: everything OnPickup disabled comes
    /// back on and the collected latch reopens, making it an ordinary pickup again.
    /// </summary>
    public virtual void Drop(Vector3 position)
    {
        transform.position = position;
        collected = false;
        dropProtectionUntil = Time.time + 1f;

        if (GetComponent<Collider>() != null)
            GetComponent<Collider>().enabled = true;

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            r.enabled = true;
        }

        OnDropped?.Invoke(this);
    }

    // Change from abstract to virtual
    public virtual void UseItem()
    {
        // Default implementation does nothing
    }

    protected virtual void OnPickup()
    {
        // Don't disable the whole GameObject, just the renderer and collider
        if (GetComponent<Collider>() != null)
            GetComponent<Collider>().enabled = false;
        if (GetComponent<Renderer>() != null)
            GetComponent<Renderer>().enabled = false;
        
        // If there are child renderers, disable them too
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            r.enabled = false;
        }
    }

    public virtual string GetItemName() => itemName;
    public Sprite GetItemIcon() 
    {
        // Remove the warning here since we already check in Awake
        return itemIcon;
    }
    public string GetItemDescription() => itemDescription;
} 