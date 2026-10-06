using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/*
 * ItemInventoryUI.cs
 * 
 * Purpose: Manages the visual representation of the player's inventory
 * Used by: Inventory system, item management
 * 
 * Key Features:
 * - Dynamic slot updates
 * - Item icon management
 * - Event-driven updates
 * - Automatic initialization
 * 
 * Performance Considerations:
 * - Event-based updates only when needed
 * - Efficient slot management
 * - Smart reference validation
 * 
 * Dependencies:
 * - Requires Inventory component
 * - Unity UI Image components
 * - Item system integration
 */
public class ItemInventoryUI : MonoBehaviour
{
    [SerializeField] private Image[] itemSlots = new Image[5];
    public IReadOnlyList<Image> ItemSlots => itemSlots;
    private Inventory inventory;

    private void Start()
    {
        // Check if all slots are assigned
        if (itemSlots.Length != 5)
        {
            Debug.LogError("ItemInventoryUI: Please assign exactly 5 item slots in the Inspector!");
            return;
        }

        foreach (Image slot in itemSlots)
        {
            if (slot == null)
            {
                Debug.LogError("ItemInventoryUI: One or more item slots are not assigned!");
                return;
            }
        }

        // Get reference to inventory
        inventory = FindFirstObjectByType<Inventory>();
        if (inventory == null)
        {
            Debug.LogError("ItemInventoryUI: Could not find Inventory component!");
            return;
        }

        // Subscribe to inventory events
        inventory.OnItemAdded += UpdateSlot;
        inventory.OnItemRemoved += ClearSlot;

        // Initialize slots
        InitializeSlots();
    }

    private void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.OnItemAdded -= UpdateSlot;
            inventory.OnItemRemoved -= ClearSlot;
        }
    }

    private void InitializeSlots()
    {
        for (int i = 0; i < itemSlots.Length; i++)
        {
            Item item = inventory.GetItem(i);
            if (item != null)
            {
                UpdateSlot(i, item);
            }
            else
            {
                ClearSlot(i);
            }
        }
    }

    private void UpdateSlot(int slot, Item item)
    {
        if (slot < 0 || slot >= itemSlots.Length) return;

        Sprite icon = item.GetItemIcon();
        if (icon != null)
        {
            itemSlots[slot].sprite = icon;
            itemSlots[slot].color = Color.white;
        }
        else
        {
            Debug.LogWarning($"No icon found for item {item.GetItemName()} in slot {slot}");
            itemSlots[slot].sprite = null;
            itemSlots[slot].color = new Color(1, 1, 1, 0); // Make transparent
        }
    }

    private void ClearSlot(int slot)
    {
        if (slot < 0 || slot >= itemSlots.Length) return;
        
        itemSlots[slot].sprite = null;
        itemSlots[slot].color = new Color(1, 1, 1, 0); // Make transparent
    }
} 