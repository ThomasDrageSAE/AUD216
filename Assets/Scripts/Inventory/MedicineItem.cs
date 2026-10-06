using UnityEngine;
using System.Collections;

public class MedicineItem : Item
{
    [SerializeField] private float healAmount = 25f;
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private ParticleSystem healingEffect;

    private void Start()
    {
        if (playerAnimator == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerAnimator = player.GetComponent<Animator>();
            }
        }
    }

    public override void UseItem()
    {
        //Debug.Log($"Using medicine item: {itemName}");

        PlayerStats playerStats = PlayerStats.Instance;
        if (playerStats == null)
        {
            // Resolve the player first. The heal animation, particles and SFX used to
            // run before this check, so when the lookup missed they replayed on every
            // press while the item was never consumed. FoodItem already orders it this
            // way.
            return;
        }

        if (playerAnimator != null)
        {
            playerAnimator.Play("Heal");
        }

        if (healingEffect != null)
        {
            healingEffect.Play();
        }

        // Play medicine used sound
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayMedicineUsedSound();
        }

        playerStats.AddHealth(healAmount);

        if (Inventory.Instance != null)
        {
            int slot = Inventory.Instance.GetSlotForItem(this);
            if (slot != -1)
            {
                Inventory.Instance.RemoveItem(slot);
                Destroy(gameObject);
            }
        }
    }

}
