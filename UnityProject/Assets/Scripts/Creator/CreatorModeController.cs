using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Creator.TileEditor;
using Waystation.Creator.TileEditor.Tools;
using Waystation.Creator.TileEditor.Palette;
using Waystation.Creator.TileEditor.Preview;
using Waystation.Creator.TileEditor.Export;
using Waystation.Creator.TileEditor.WallBitmask;
using Waystation.Core;

namespace Waystation.Creator
{
    public class CreatorModeController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private AssetLibrary _library;
        private TileEditorController _editor;
        private ColourPalette _palette;
        private ContextPreviewRenderer _contextPreview;
        private RoomPreviewRenderer _roomPreview;
        private PreviewDebouncer _previewDebouncer;
        private BitmaskAutoGenerator _bitmaskGenerator;
        private DevMode.DevModeOverlay _devOverlay;

        // UI panel references
        private VisualElement _root;
        private VisualElement _libraryPanel;
        private VisualElement _canvasPanel;
        private VisualElement _inspectorPanel;
        private VisualElement _toolBar;
        private VisualElement _palettePanel;
        private VisualElement _statusBar;

        // State
        private bool _isEditing;
        private AssetDefinition _currentAsset;

        private void Awake()
        {
            if (!FeatureFlags.UseCreatorMode)
            {
                gameObject.SetActive(false);
                return;
            }
        }

        private void Start()
        {
            _library = new AssetLibrary();
            _editor = new TileEditorController();
            _palette = new ColourPalette();
            _contextPreview = new ContextPreviewRenderer();
            _roomPreview = new RoomPreviewRenderer();
            _previewDebouncer = new PreviewDebouncer(0.1f);
            _devOverlay = new DevMode.DevModeOverlay();

            RegisterTools();
            _library.LoadAll();

            if (_uiDocument != null)
            {
                _root = _uiDocument.rootVisualElement;
                BindUI();
            }

            // Show library by default
            ShowLibrary();

            // Check onboarding
            if (CreatorSettings.FirstLaunch)
                ShowOnboarding();
        }

        private void Update()
        {
            _previewDebouncer.Update(Time.deltaTime);

            if (_isEditing)
                HandleShortcuts();
        }

        private void OnDestroy()
        {
            _editor?.Dispose();
            _contextPreview?.Dispose();
            _roomPreview?.Dispose();
        }

        private void RegisterTools()
        {
            _editor.RegisterTool(new TileTool_Pencil());
            _editor.RegisterTool(new TileTool_Eraser());
            _editor.RegisterTool(new TileTool_Eyedropper());
            _editor.RegisterTool(new TileTool_Fill());
            _editor.RegisterTool(new TileTool_Line());
            _editor.RegisterTool(new TileTool_Rectangle());
            _editor.RegisterTool(new TileTool_Select());
            _editor.RegisterTool(new TileTool_Ellipse());
            _editor.RegisterTool(new TileTool_Lasso());
            _editor.RegisterTool(new TileTool_Move());
            _editor.RegisterTool(new TileTool_MirrorPaint());
            _editor.RegisterTool(new TileTool_Symmetry());
            _editor.RegisterTool(new TileTool_MagicWand());

            _editor.SwitchTool("pencil");
        }

        private void BindUI()
        {
            _libraryPanel = _root.Q("library-panel");
            _canvasPanel = _root.Q("canvas-panel");
            _inspectorPanel = _root.Q("inspector-panel");
            _toolBar = _root.Q("toolbar");
            _palettePanel = _root.Q("palette-panel");
            _statusBar = _root.Q("status-bar");

            // Library buttons
            _root.Q<Button>("btn-new-floor")?.RegisterCallback<ClickEvent>(_ => CreateAsset("floor_tile"));
            _root.Q<Button>("btn-new-wall")?.RegisterCallback<ClickEvent>(_ => CreateAsset("wall_tile"));
            _root.Q<Button>("btn-new-furniture")?.RegisterCallback<ClickEvent>(_ => CreateAsset("furniture"));
            _root.Q<Button>("btn-back-to-library")?.RegisterCallback<ClickEvent>(_ => BackToLibrary());
            _root.Q<Button>("btn-save")?.RegisterCallback<ClickEvent>(_ => SaveCurrentAsset());
            _root.Q<Button>("btn-export")?.RegisterCallback<ClickEvent>(_ => ExportCurrentAsset());

            // Tool buttons
            BindToolButton("btn-pencil", "pencil");
            BindToolButton("btn-eraser", "eraser");
            BindToolButton("btn-eyedropper", "eyedropper");
            BindToolButton("btn-fill", "fill");
            BindToolButton("btn-line", "line");
            BindToolButton("btn-rect", "rectangle");
            BindToolButton("btn-select", "select");
            BindToolButton("btn-ellipse", "ellipse");
            BindToolButton("btn-move", "move");
            BindToolButton("btn-mirror", "mirrorpaint");
            BindToolButton("btn-symmetry", "symmetry");
            BindToolButton("btn-wand", "magicwand");

            // Undo/redo
            _root.Q<Button>("btn-undo")?.RegisterCallback<ClickEvent>(_ => _editor.Undo());
            _root.Q<Button>("btn-redo")?.RegisterCallback<ClickEvent>(_ => _editor.Redo());

            // Colour swap
            _root.Q<Button>("btn-swap-colours")?.RegisterCallback<ClickEvent>(_ => _editor.SwapColours());

            // Editor events
            _editor.OnCanvasChanged += () => _previewDebouncer.Request(UpdatePreviews);
            _editor.OnToolChanged += UpdateToolUI;
            _editor.OnDirtyStateChanged += UpdateTitleBar;
        }

        private void BindToolButton(string buttonName, string toolName)
        {
            _root.Q<Button>(buttonName)?.RegisterCallback<ClickEvent>(_ => _editor.SwitchTool(toolName));
        }

        private void HandleShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.P)) _editor.SwitchTool("pencil");
            if (Input.GetKeyDown(KeyCode.E)) _editor.SwitchTool("eraser");
            if (Input.GetKeyDown(KeyCode.I)) _editor.SwitchTool("eyedropper");
            if (Input.GetKeyDown(KeyCode.G)) _editor.SwitchTool("fill");
            if (Input.GetKeyDown(KeyCode.L)) _editor.SwitchTool("line");
            if (Input.GetKeyDown(KeyCode.R)) _editor.SwitchTool("rectangle");
            if (Input.GetKeyDown(KeyCode.S) && !Input.GetKey(KeyCode.LeftControl)) _editor.SwitchTool("select");
            if (Input.GetKeyDown(KeyCode.O)) _editor.SwitchTool("ellipse");
            if (Input.GetKeyDown(KeyCode.M)) _editor.SwitchTool("move");
            if (Input.GetKeyDown(KeyCode.H)) _editor.SwitchTool("mirrorpaint");
            if (Input.GetKeyDown(KeyCode.W)) _editor.SwitchTool("magicwand");

            // Ctrl shortcuts
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                if (Input.GetKeyDown(KeyCode.Z))
                {
                    if (Input.GetKey(KeyCode.LeftShift))
                        _editor.Redo();
                    else
                        _editor.Undo();
                }
                if (Input.GetKeyDown(KeyCode.S)) SaveCurrentAsset();
            }

            // X = swap colours
            if (Input.GetKeyDown(KeyCode.X)) _editor.SwapColours();
        }

        // --- Library actions ---

        public void ShowLibrary()
        {
            _isEditing = false;
            _libraryPanel?.RemoveFromClassList("hidden");
            _canvasPanel?.AddToClassList("hidden");
            _inspectorPanel?.AddToClassList("hidden");
        }

        public void CreateAsset(string type)
        {
            var def = _library.Create(type);
            OpenAsset(def);

            if (CreatorSettings.FirstAsset)
            {
                CreatorSettings.FirstAsset = false;
                // Could trigger guided tooltip here
            }
        }

        public void OpenAsset(AssetDefinition def)
        {
            if (_isEditing && _editor.IsDirty)
            {
                // TODO: show unsaved changes modal
            }

            _currentAsset = def;
            _editor.OpenAsset(def);
            _isEditing = true;

            // Load persisted variant data
            string dir = _library.GetAssetDirectory(def);
            int variantCount = GetVariantCount(def.type);
            for (int i = 0; i < variantCount; i++)
            {
                var pixels = CanvasPersistence.LoadVariant(dir, i,
                    _editor.Canvas.Width, _editor.Canvas.Height);
                if (pixels != null)
                    _editor.SetVariantPixels(i, pixels);
            }

            // Restore editor state
            if (def.editor_state != null)
            {
                if (!string.IsNullOrEmpty(def.editor_state.last_tool))
                    _editor.SwitchTool(def.editor_state.last_tool);
                _editor.Canvas.Zoom = def.editor_state.zoom_level;
            }

            // Initialize bitmask generator for walls
            if (def.type == "wall_tile")
                _bitmaskGenerator = new BitmaskAutoGenerator(_editor);

            _libraryPanel?.AddToClassList("hidden");
            _canvasPanel?.RemoveFromClassList("hidden");
            _inspectorPanel?.RemoveFromClassList("hidden");

            UpdateTitleBar();
            UpdatePreviews();
        }

        public void BackToLibrary()
        {
            if (_isEditing && _editor.IsDirty)
            {
                // TODO: show unsaved changes modal
                SaveCurrentAsset();
            }
            ShowLibrary();
        }

        // --- Save / Export ---

        public void SaveCurrentAsset()
        {
            if (_currentAsset == null) return;

            string dir = _library.GetAssetDirectory(_currentAsset);
            int variantCount = GetVariantCount(_currentAsset.type);

            for (int i = 0; i < variantCount; i++)
            {
                var pixels = _editor.GetVariantPixels(i);
                if (pixels != null)
                    CanvasPersistence.SaveVariant(dir, i,
                        pixels, _editor.Canvas.Width, _editor.Canvas.Height);
            }

            // Save thumbnail
            CanvasPersistence.SaveThumbnail(dir, _editor.Canvas.Texture);

            // Update editor state
            _currentAsset.editor_state.last_tool = _editor.ActiveTool?.Name?.ToLowerInvariant() ?? "pencil";
            _currentAsset.editor_state.zoom_level = (int)_editor.Canvas.Zoom;
            _currentAsset.editor_state.active_variant = _editor.ActiveVariantIndex;

            _library.Save(_currentAsset);
            _editor.ClearDirty();
        }

        public void ExportCurrentAsset()
        {
            if (_currentAsset == null) return;
            SaveCurrentAsset();

            string dir = _library.GetAssetDirectory(_currentAsset);

            // Validate
            ValidationResult validation;
            switch (_currentAsset.type)
            {
                case "wall_tile":
                    validation = ExportValidator.ValidateWall(_editor);
                    break;
                case "furniture":
                    validation = ExportValidator.ValidateFurniture(_editor, _currentAsset);
                    break;
                default:
                    validation = ExportValidator.ValidateFloor(_editor);
                    break;
            }

            if (!validation.IsValid)
            {
                Debug.LogWarning($"Export validation failed: {string.Join(", ", validation.Errors)}");
                return;
            }

            // Export atlas and sidecar
            Texture2D atlas = null;
            string sidecarJson = null;

            switch (_currentAsset.type)
            {
                case "floor_tile":
                    atlas = TileAtlasExporter.ExportFloorAtlas(
                        _editor.GetVariantPixels(0),
                        _editor.GetVariantPixels(1),
                        _editor.GetVariantPixels(2));
                    sidecarJson = TileSidecarExporter.ExportFloorSidecar(_currentAsset);
                    break;
                case "wall_tile":
                    atlas = TileAtlasExporter.ExportWallAtlas(i => _editor.GetVariantPixels(i));
                    sidecarJson = TileSidecarExporter.ExportWallSidecar(_currentAsset);
                    break;
                case "furniture":
                    // Simplified for now
                    atlas = TileAtlasExporter.ExportFloorAtlas(
                        _editor.GetVariantPixels(0), null, null);
                    sidecarJson = TileSidecarExporter.ExportFurnitureSidecar(_currentAsset, 1, 1);
                    break;
            }

            if (atlas != null)
            {
                byte[] png = TileAtlasExporter.EncodeAtlasToPNG(atlas);
                System.IO.File.WriteAllBytes(
                    System.IO.Path.Combine(dir, _currentAsset.id + ".png"), png);
                Object.Destroy(atlas);
            }

            if (sidecarJson != null)
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dir, "sidecar.json"), sidecarJson);
            }

            Debug.Log($"[Creator] Exported {_currentAsset.name} to {dir}");
        }

        // --- Helpers ---

        private int GetVariantCount(string type)
        {
            switch (type)
            {
                case "wall_tile": return 16;
                case "floor_tile": return 3; // Normal, Damaged, Destroyed
                case "furniture": return 24; // 4 dirs × 6 states max
                default: return 1;
            }
        }

        private void UpdatePreviews()
        {
            _contextPreview.UpdatePreview(_editor.Canvas.Pixels,
                _editor.Canvas.Width, _editor.Canvas.Height);
        }

        private void UpdateToolUI()
        {
            // Update active tool highlighting in toolbar
            if (_toolBar == null) return;
            foreach (var btn in _toolBar.Query<Button>().ToList())
                btn.RemoveFromClassList("tool-active");

            string activeName = _editor.ActiveTool?.Name?.ToLowerInvariant();
            if (activeName != null)
                _toolBar.Q<Button>($"btn-{activeName}")?.AddToClassList("tool-active");
        }

        private void UpdateTitleBar()
        {
            var titleLabel = _root?.Q<Label>("editor-title");
            if (titleLabel != null && _currentAsset != null)
            {
                titleLabel.text = _currentAsset.name + (_editor.IsDirty ? " •" : "");
            }
        }

        private void ShowOnboarding()
        {
            CreatorSettings.FirstLaunch = false;
            // Onboarding UI handled by OnboardingController
        }

        // --- Public accessors for UI panels ---

        public TileEditorController Editor => _editor;
        public AssetLibrary Library => _library;
        public ColourPalette Palette => _palette;
        public AssetDefinition CurrentAsset => _currentAsset;
        public BitmaskAutoGenerator BitmaskGenerator => _bitmaskGenerator;
        public DevMode.DevModeOverlay DevOverlay => _devOverlay;
    }
}
