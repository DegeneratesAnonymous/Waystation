// BuildMenuController.cs
// Attach to a GameObject that has a UIDocument component.
// Assign the UIDocument in the inspector, or it will be found automatically.
//
// Setup:
//   1. Create a UI Document asset (GameObject → UI → UI Document)
//   2. Set Source Asset = BuildMenu.uxml
//   3. Add a Panel Settings asset, assign BuildMenu.uss as the theme stylesheet
//   4. Attach this script to the same GameObject
//
// Or use with a runtime panel — call BuildMenuController.Show() / .Hide() from
// your game manager.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class BuildMenuController : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("UI Document")]
    public UIDocument uiDocument;

    // ── Category item data (populate from ScriptableObjects in production) ─
    [System.Serializable]
    public class BuildItem
    {
        public string name;
        public string cost;
        /// <summary>
        /// Registry key for this item (e.g. "buildable.wall").
        /// Matched against ContentRegistry.Buildables when starting ghost placement.
        /// Falls back to a normalised form of <see cref="name"/> when empty.
        /// </summary>
        public string buildableId;
    }

    [System.Serializable]
    public class BuildCategory
    {
        public string id;           // matches data-category in UXML
        public string displayName;
        public List<BuildItem> items;
    }

    [Header("Categories")]
    public List<BuildCategory> categories = new()
    {
        new BuildCategory { id="structure",  displayName="STRUCTURE",  items=new(){
            new(){name="Wall",    cost="40 Fe"},
            new(){name="Floor",   cost="20 Fe"},
            new(){name="Door",    cost="80 Fe"},
            new(){name="Window",  cost="60 Fe"},
            new(){name="Column",  cost="30 Fe"},
        }},
        new BuildCategory { id="electrical", displayName="ELECTRICAL", items=new(){
            new(){name="Wire",      cost="10 Fe"},
            new(){name="Generator", cost="200 Fe"},
            new(){name="Battery",   cost="120 Fe"},
            new(){name="Switch",    cost="40 Fe"},
            new(){name="Light",     cost="30 Fe"},
        }},
        new BuildCategory { id="objects",    displayName="OBJECTS",    items=new(){
            new(){name="Bed",     cost="80 Fe"},
            new(){name="Locker",  cost="60 Fe"},
            new(){name="Console", cost="150 Fe"},
            new(){name="Table",   cost="40 Fe"},
            new(){name="Chair",   cost="20 Fe"},
        }},
        new BuildCategory { id="production", displayName="PRODUCTION", items=new(){
            new(){name="Refinery",   cost="400 Fe"},
            new(){name="Fabricator", cost="300 Fe"},
            new(){name="Assembler",  cost="250 Fe"},
            new(){name="Smelter",    cost="350 Fe"},
        }},
        new BuildCategory { id="plumbing",   displayName="PLUMBING",   items=new(){
            new(){name="Pipe",   cost="15 Fe"},
            new(){name="Pump",   cost="80 Fe"},
            new(){name="Tank",   cost="100 Fe"},
            new(){name="Valve",  cost="40 Fe"},
            new(){name="Filter", cost="60 Fe"},
        }},
        new BuildCategory { id="security",   displayName="SECURITY",   items=new(){
            new(){name="Turret",        cost="300 Fe"},
            new(){name="Camera",        cost="80 Fe"},
            new(){name="Door Lock",     cost="60 Fe"},
            new(){name="Motion Sensor", cost="50 Fe"},
        }},
    };

    // ── Placement event ────────────────────────────────────────────────────
    /// <summary>
    /// Fired when the player selects a build item from the menu.
    /// Arguments: categoryId (e.g. "structure"), buildableId (e.g. "buildable.wall").
    /// Subscribe from the active HUD controller to begin ghost placement.
    /// </summary>
    public static event Action<string, string> OnBuildItemSelected;

    // ── Private state ──────────────────────────────────────────────────────
    private VisualElement _root;
    private VisualElement _contentArea;
    private Label         _contentLabel;
    private VisualElement _subItems;
    private Label         _statusHint;
    private string        _activeCategory;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    void OnEnable()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        _root = uiDocument.rootVisualElement;

        CacheElements();
        BindButtons();
        SetStatusHint("SELECT CATEGORY");
    }

    // ── Cache ──────────────────────────────────────────────────────────────
    void CacheElements()
    {
        _contentArea  = _root.Q<VisualElement>("content-area");
        _contentLabel = _root.Q<Label>("content-label");
        _subItems     = _root.Q<VisualElement>("sub-items");
        _statusHint   = _root.Q<Label>("status-hint");
    }

    // ── Bind all interactive elements ──────────────────────────────────────
    void BindButtons()
    {
        // Close button
        _root.Q<Button>("close-btn")?.RegisterCallback<ClickEvent>(_ => Hide());

        // Deconstruct / Queue toggle
        var btnDeconstruct = _root.Q<Button>("btn-deconstruct");
        var btnQueue       = _root.Q<Button>("btn-queue");
        btnDeconstruct?.RegisterCallback<ClickEvent>(_ =>
        {
            btnDeconstruct.EnableInClassList("action-btn--active",
                !btnDeconstruct.ClassListContains("action-btn--active"));
        });

        // Category buttons
        foreach (var cat in categories)
        {
            var btn = _root.Q<Button>("cat-" + cat.id);
            if (btn == null) continue;

            // Update count label to match data
            var countLabel = btn.Q<Label>(className: "cat-count");
            if (countLabel != null) countLabel.text = cat.items.Count.ToString();

            btn.RegisterCallback<ClickEvent>(_ => OnCategoryClicked(cat.id, btn));
        }
    }

    // ── Category click handler ─────────────────────────────────────────────
    void OnCategoryClicked(string categoryId, Button clickedBtn)
    {
        // Deselect all
        foreach (var cat in categories)
        {
            var b = _root.Q<Button>("cat-" + cat.id);
            b?.RemoveFromClassList("cat-btn--selected");
        }

        // Toggle if already active
        if (_activeCategory == categoryId)
        {
            _activeCategory = null;
            CloseContentArea();
            SetStatusHint("SELECT CATEGORY");
            return;
        }

        // Select clicked
        _activeCategory = categoryId;
        clickedBtn.AddToClassList("cat-btn--selected");

        // Populate sub-items
        var data = categories.Find(c => c.id == categoryId);
        if (data != null) PopulateSubItems(data);

        OpenContentArea();
        SetStatusHint(categoryId.ToUpper());
    }

    // ── Sub-item population ────────────────────────────────────────────────
    void PopulateSubItems(BuildCategory cat)
    {
        _subItems.Clear();
        _contentLabel.text = $"{cat.displayName}  ·  {cat.items.Count} ITEMS";

        foreach (var item in cat.items)
        {
            var row = new VisualElement();
            row.AddToClassList("sub-item");

            var nameLabel = new Label(item.name.ToUpper());
            nameLabel.AddToClassList("sub-item-name");

            var costLabel = new Label(item.cost);
            costLabel.AddToClassList("sub-item-cost");

            row.Add(nameLabel);
            row.Add(costLabel);

            // Click to select/place
            row.RegisterCallback<ClickEvent>(_ => OnItemSelected(cat.id, item));
            _subItems.Add(row);
        }
    }

    // ── Item selected ──────────────────────────────────────────────────────
    void OnItemSelected(string categoryId, BuildItem item)
    {
        string resolvedId = !string.IsNullOrEmpty(item.buildableId)
            ? item.buildableId
            // Fallback: derive from display name using the registry naming convention
            // (lowercase, spaces replaced by underscores, prefixed "buildable.").
            // Set item.buildableId explicitly when the default items are replaced
            // with registry-driven data to avoid relying on this transformation.
            : "buildable." + item.name.ToLower().Replace(" ", "_");

        Debug.Log($"[BuildMenu] Selected: {categoryId} / {item.name} ({item.cost}) → {resolvedId}");
        OnBuildItemSelected?.Invoke(categoryId, resolvedId);
    }

    // ── Content area expand / collapse ────────────────────────────────────
    void OpenContentArea()
    {
        _contentArea.AddToClassList("content-area--open");
    }

    void CloseContentArea()
    {
        _contentArea.RemoveFromClassList("content-area--open");
    }

    // ── Status hint ───────────────────────────────────────────────────────
    void SetStatusHint(string text)
    {
        if (_statusHint != null) _statusHint.text = text;
    }

    // ── Show / Hide (call from game manager) ──────────────────────────────
    public void Show()
    {
        _root.Q("build-menu")?.RemoveFromClassList("hidden");
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        // Or keep active and just hide the panel:
        // _root.Q("build-menu")?.AddToClassList("hidden");
    }

    // ── Queue badge update (call from build queue system) ─────────────────
    public void SetQueueCount(int count)
    {
        var badge = _root.Q<Label>("queue-badge");
        if (badge != null) badge.text = count.ToString();
    }
}
