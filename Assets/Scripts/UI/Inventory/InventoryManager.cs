using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using JetBrains.Annotations;

/// <summary> Primary user interface manager. </summary>
public class InventoryManager : MonoBehaviour
{
#region Editor

    [ Header("Game Objects") ]
    
    [Tooltip("Preview grid for the inventory.")]
    public GameObject previewGridGameObject;
    [Tooltip("Target under which items should be created.")]
    public GameObject createDestination;
    [Tooltip("Main scene camera")]
    public GameObject mainCamera;
    
    [ Header("Inventory Settings") ]
    [Tooltip("Show highlight even when outside of the inventory area?")]
    public bool highlightWhenOutside = true;
    
#endregion // Editor

#region Internal
    
    /// <summary> Singleton instance of the UIManager. </summary>
    private static InventoryManager sInstance;
    
    /// <summary> Getter for the singleton UIManager object. </summary>
    public static InventoryManager Instance
    { get { return sInstance; } }

    /// <summary> Root element of the inventory. </summary>
    private VisualElement mRoot;
    /// <summary> Root element of the inventory grid. </summary>
    private VisualElement mInventoryGrid;
    
    /// <summary> Label displaying the current amount of currency. </summary>
    private Label mCurrencyValue;
    
    /// <summary> Label displaying the item name. </summary>
    private Label mItemDetailName;
    /// <summary> Label displaying the item description. </summary>
    private Label mItemDetailDescription;
    /// <summary> Label displaying the item cost. </summary>
    private Label mItemDetailCost;
    /// <summary> The item creation button. </summary>
    private Button mItemCreateButton;
    
    /// <summary> Element used to highlight the selected grid location. </summary>
    private VisualElement mGridHighlight;

    /// <summary> List of items currently within the inventory grid. </summary>
    private List<VisualElement> mCurrentItems = new List<VisualElement>();
    
    /// <summary> Currently available currency. </summary>
    private int mAvailableCurrency = 42;
    
#endregion // Internal

#region Interface

    /// <summary> Id the inventory initialized and ready for use? </summary>
    public bool inventoryReady
    { get; private set; }
    /// <summary> Dimension of a single item slot in pixels. </summary>
    public Vector2Int slotDimension 
    { get; private set; }
    /// <summary> Dimension of the inventory grid. </summary>
    public Vector2Int gridDimension 
    { get; private set; }
    /// <summary> Preview grid used for generating the item images.. </summary>
    public PreviewGrid previewGrid 
    { get => previewGridGameObject.GetComponent<PreviewGrid>(); }
    
    // TODO - Move these to PlayerInventory.
    /// <summary> Reference to the currently selected item. </summary>
    public ItemVisual selectedItem
    { get; private set; }

    /// <summary> Currently available currency. </summary>
    public int availableCurrency
    {
        get => mAvailableCurrency;
        set => SetAvailableCurrency(value);
    }

#endregion // Internal

    /// <summary> Called when the script instance is first loaded. </summary>
    private void Awake()
    {
        // Initialize the singleton instance, if no other exists.
        if (sInstance != null && sInstance != this)
        { Destroy(gameObject); }
        else
        { sInstance = this; SetupInventory(); }
    }
    
    /// <summary> Called before the first frame update. </summary>
    void Start()
    { }

    /// <summary> Update called once per frame. </summary>
    void Update()
    {
        if (!inventoryReady)
        { return; }

        mCurrencyValue.text = $"{availableCurrency}";
    }

    /// <summary> Initialize and setup the inventory UI. </summary>
    private async void SetupInventory()
    {
        inventoryReady = false;
        
        mRoot = GetComponentInChildren<UIDocument>().rootVisualElement;
        mRoot.style.visibility = Visibility.Hidden;
        //mRoot.style.display = DisplayStyle.None;
        
        mInventoryGrid = mRoot.Q<VisualElement>("InventoryGrid");

        mCurrencyValue = mRoot.Q<Label>("CurrencyValue");
        
        var itemDetails = mRoot.Q<VisualElement>("ItemDetailContainer");
        mItemDetailName = itemDetails.Q<Label>("ItemDetailName");
        mItemDetailDescription = itemDetails.Q<Label>("ItemDetailDescription");
        mItemDetailCost = itemDetails.Q<Label>("ItemDetailCost");


        mItemCreateButton = itemDetails.Q<Button>("ItemDetailButtonCreate");
        mItemCreateButton.clicked += () => CreateItem();
        
        
        await UniTask.WaitForEndOfFrame();

        var gridSlots = mInventoryGrid.Children();
        var gridCols = gridSlots.Distinct(slot => slot.worldBound.x).Count();
        var gridRows = gridSlots.Distinct(slot => slot.worldBound.y).Count();
        gridDimension = new Vector2Int(gridCols, gridRows);
        
        var firstSlot = gridSlots.First();
        slotDimension = new Vector2Int(
            Mathf.RoundToInt(firstSlot.worldBound.width),
            Mathf.RoundToInt(firstSlot.worldBound.height)
        );
        
        mGridHighlight = new VisualElement
        {
            name = "Highlight",
            style = {
                position = Position.Absolute,
                visibility = Visibility.Hidden
            }
        };
        mGridHighlight.AddToClassList("inventory_slot_highlight");
        mInventoryGrid.Add(mGridHighlight);
        
        UpdateSelectedItem(null);
        
        inventoryReady = true;
    }

    /// <summary> Is the inventory currently visible? </summary>
    public bool InventoryVisible()
    { return mRoot.style.visibility != Visibility.Hidden; }

    /// <summary> Hide the inventory. </summary>
    public void HideInventory()
    {
        if (inventoryReady)
        { mRoot.style.visibility = Visibility.Hidden; }
    }
    
    /// <summary> Hide the inventory. </summary>
    public async void DisplayInventory()
    {
        if (inventoryReady)
        {
            // Fit the inventory UI into the current camera's viewport.
            inventoryReady = false;
            var camera = mainCamera.GetComponent<Camera>();
            var originalDimensions = new Vector2(
                mRoot.resolvedStyle.width,
                mRoot.resolvedStyle.height
            );
            var padding = new Vector2(
                camera.rect.x * originalDimensions.x, 
                camera.rect.y * originalDimensions.y
            );
            mRoot.style.borderLeftWidth = mRoot.style.borderRightWidth = padding.x;
            mRoot.style.borderTopWidth = mRoot.style.borderBottomWidth = padding.y;
            
            await UniTask.WaitForEndOfFrame();
            
            // Recalculate size properties.
            var firstSlot = mInventoryGrid.Children().First();
            slotDimension = new Vector2Int(
                Mathf.RoundToInt(firstSlot.resolvedStyle.width),
                Mathf.RoundToInt(firstSlot.resolvedStyle.height)
            );
            
            // Finally, make the inventory UI visible.
            mRoot.style.visibility = Visibility.Visible; 
            inventoryReady = true;
        }
    }
    
    /// <summary> Get grid position based on the local position vector. </summary>
    public Vector2Int GetItemGridPosition(Vector2 localPosition)
    {
        var gridPosition = new Vector2Int(
            Mathf.RoundToInt(localPosition.x / slotDimension.x), 
            Mathf.RoundToInt(localPosition.y / slotDimension.y)
        );

        return gridPosition;
    }
    
    /// <summary> Get grid position based on the local position bounds. </summary>
    public Vector2Int GetItemGridPosition(Rect localBounds)
    { return GetItemGridPosition(new Vector2(localBounds.x, localBounds.y)); }

    /// <summary> Get grid-based position of the provided item. It should already be placed within the grid!</summary>
    public Vector2Int GetItemGridPosition(VisualElement item)
    { return GetItemGridPosition(item.localBound); }

    /// <summary> Add given item to the inventory grid and return its position. </summary>
    public Vector2Int AddItemToGrid(VisualElement item)
    {
        if (!mCurrentItems.Contains(item))
        { mCurrentItems.Add(item); mInventoryGrid.Add(item); } 
        return GetItemGridPosition(item);
    }

    /// <summary> Remove given item from the inventory grid and return its position. </summary>
    public Vector2Int RemoveItemFromGrid(VisualElement item)
    {
        var itemPosition = GetItemGridPosition(item);
        if (mCurrentItems.Contains(item))
        { mCurrentItems.Remove(item); mInventoryGrid.Remove(item); }
        return itemPosition;
    }
    
    /// <summary> Remove all items from the inventory grid. </summary>
    public void RemoveAllItemsFromGrid()
    {
        foreach (var item in mCurrentItems)
        { mInventoryGrid.Remove(item); }
        mCurrentItems.Clear();
    }
    
    /// <summary> Display the highlight over selected location and return potential placement outcome. </summary>
    public (bool canPlace, Vector2 position) ShowPlacementTarget(ItemVisual draggedItem)
    {
        if (!mInventoryGrid.layout.Contains(new Vector2(draggedItem.localBound.xMax, draggedItem.localBound.yMax)))
        { // We are outside of the inventory area, hide the highlight.
            if (highlightWhenOutside)
            { // When outside highlighting, we can use the last valid position.
                return (canPlace: true, position: mGridHighlight.worldBound.position);
            }
            else
            { // Else, just remove the highlight and report inability to place.
                mGridHighlight.style.visibility = Visibility.Hidden;
                return (canPlace: false, position: Vector2.zero);
            }
        }
        
        // Locate the target slot based on collisions with the existing items.
        var overlappingItems = mInventoryGrid.Children().Where(x =>
            x.layout.Overlaps(draggedItem.layout) && x != draggedItem).OrderBy(x =>
            Vector2.Distance(x.worldBound.position,
                draggedItem.worldBound.position));
        
        if (overlappingItems.Count() > 0)
        { // Overlapping at least one slot.
            // The slot will always be first, followed by the highlight and items.
            var targetSlot = overlappingItems.First();

            // Set the highlight size and position it over the target location.
            mGridHighlight.style.width = draggedItem.style.width;
            mGridHighlight.style.height = draggedItem.style.height;
            mGridHighlight.style.left = targetSlot.layout.position.x;
            mGridHighlight.style.top = targetSlot.layout.position.y;
            
            var itemOverlaps = mCurrentItems.Where(
                x => x.layout.Overlaps(mGridHighlight.layout)
            ).ToArray();
            
            if (itemOverlaps.Count() > 1)
            { // Place is already occupied.
                mGridHighlight.style.visibility = Visibility.Hidden;
                return (canPlace: false, position: Vector2.zero);
            }
            else
            { // Place is free to be used.
                mGridHighlight.style.visibility = Visibility.Visible;
                return (canPlace: true, targetSlot.worldBound.position);
            }
        }
        else
        { // Not overlapping any slots.
            return (canPlace: false, position: Vector2.zero);
        }
    }

    /// <summary> Move the highlight to be on top of all other elements. </summary>
    public void MoveHighlightToTop()
    { mGridHighlight.BringToFront(); }
    
    /// <summary> Update the currently displayed item description from given item. </summary>
    public void UpdateSelectedItem([CanBeNull] ItemVisual item = null)
    {
        
        if (item == null)
        { // We have no item selected -> Provide some default information.
            mItemDetailName.text = "Principa Physica";
            mItemDetailDescription.text = "Please select item from inventory...";
            mItemDetailCost.text = "-";

            mItemCreateButton.SetEnabled(false);
        }
        else
        { // We have item selected -> Use the item information.

            mItemDetailName.text = item.definition.readableName;
            mItemDetailDescription.text = item.definition.readableDescription;
            mItemDetailCost.text = item.definition.cost.ToString();

            if(item.definition.cost > availableCurrency) {
                mItemCreateButton.SetEnabled(false);
            }
            else {
                mItemCreateButton.SetEnabled(true);
            }
        }
        
        selectedItem = item;
    }

    /// <summary> Set the current amount of currency available. </summary>
    public void SetAvailableCurrency(int currency)
    {
        mAvailableCurrency = currency;
        if (selectedItem != null)
        { mItemCreateButton.SetEnabled(selectedItem.definition.cost <= availableCurrency); }
    }

    /// <summary> Function called by the "Create" button. Returns whether the operation was successful. </summary>
    public bool CreateItem()
    {

        var itemDefinition = selectedItem?.definition;

        if(selectedItem != null) {
            if(availableCurrency >= itemDefinition.cost) {
                Instantiate(itemDefinition.prefab, createDestination.transform);
                availableCurrency -= itemDefinition.cost;
                return true;
            }
        }
        
        return false;
    }
}
