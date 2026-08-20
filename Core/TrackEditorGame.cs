using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RetroRacer.Data;
using RetroRacer.Ui;
using RetroRacer.World;

namespace RetroRacer.Core;

public sealed class TrackEditorGame : Game
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const int InspectorWidth = 320;
    private const float MinZoom = 0.18f;
    private const float MaxZoom = 5.0f;
    private const float MinElevationViewScale = 1.0f;
    private const float MaxElevationViewScale = 12.0f;
    private static readonly WidthHandle[] WidthHandleValues =
    [
        WidthHandle.LeftRoad,
        WidthHandle.RightRoad,
        WidthHandle.LeftGrass,
        WidthHandle.RightGrass,
        WidthHandle.LeftWall,
        WidthHandle.RightWall
    ];

    private readonly GraphicsDeviceManager _graphics;
    private readonly int? _autoExitMilliseconds;
    private TrackDefinitionFile _trackFile;
    private TrackDefinition _definition;
    private TrackControlPoint[] _controlPoints;
    private TrackBankPoint[] _bankProfile;
    private TrackSegmentShape[] _segmentShapes;
    private TrackWidthPoint[] _widthPoints;
    private float _roadWidthMeters;
    private Vector2 _cameraCenter;
    private float _zoom = 1f;
    private EditorViewMode _viewMode = EditorViewMode.TopDown;
    private float _orbitYawRadians = -0.68f;
    private float _orbitPitchRadians = 0.72f;
    private float _elevationViewScale = 4.0f;
    private EditorEditMode _editMode = EditorEditMode.Point;
    private int _selectedPoint;
    private int _draggedPoint = -1;
    private WidthHandle _draggedWidthHandle = WidthHandle.None;
    private bool _dirty;
    private bool _showHelp = true;
    private string _statusText = "READY";
    private float _statusSeconds;
    private TimeSpan _elapsed;

    private SpriteBatch? _spriteBatch;
    private Texture2D? _pixel;
    private PixelFont? _font;
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private bool _hasPreviousInput;

    private TrackEditorGame(TrackDefinitionFile trackFile, int? autoExitMilliseconds)
    {
        if (trackFile.Definition.ControlPoints is not { Length: >= 4 } points)
        {
            throw new InvalidDataException("The track editor needs an editable custom spline track with at least four control points.");
        }

        _trackFile = trackFile;
        _definition = trackFile.Definition;
        _controlPoints = points.ToArray();
        _bankProfile = trackFile.Definition.BankProfileDegrees?.ToArray() ?? [new TrackBankPoint(0f, 0f), new TrackBankPoint(1f, 0f)];
        _segmentShapes = NormalizeSegmentShapes(trackFile.Definition.SegmentShapes, _controlPoints.Length);
        _roadWidthMeters = trackFile.Definition.RoadHalfWidthMeters * 2f;
        _widthPoints = NormalizeWidthPoints(trackFile.Definition.WidthPoints, _controlPoints.Length, _roadWidthMeters);
        _autoExitMilliseconds = autoExitMilliseconds;

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = WindowWidth,
            PreferredBackBufferHeight = WindowHeight,
            SynchronizeWithVerticalRetrace = true
        };

        Window.Title = $"R Type Honda Racing Track Editor - {_definition.DisplayName}";
        Window.AllowUserResizing = false;
        IsMouseVisible = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        FitView();
    }

    public static TrackEditorGame CreateFromArgs(string[] args)
    {
        string? trackPath = null;
        int? autoExitMilliseconds = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--track-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                trackPath = args[++i];
            }
            else if (args[i].Equals("--auto-exit-ms", StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Length &&
                     int.TryParse(args[i + 1], out int parsed))
            {
                autoExitMilliseconds = Math.Max(1, parsed);
                i++;
            }
        }

        TrackDefinitionFile trackFile = string.IsNullOrWhiteSpace(trackPath)
            ? TrackDefinitionFileLoader.LoadFiles(TrackDefinitionFileLoader.DefaultTrackDirectory)
                .OrderBy(file => file.SortOrder)
                .ThenBy(file => file.Definition.DisplayName)
                .FirstOrDefault()
            : TrackDefinitionFileLoader.LoadFile(trackPath);

        if (string.IsNullOrWhiteSpace(trackFile.SourcePath))
        {
            throw new FileNotFoundException($"No editable tracks were found in {TrackDefinitionFileLoader.DefaultTrackDirectory}.");
        }

        return new TrackEditorGame(trackFile, autoExitMilliseconds);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _font = new PixelFont(_pixel);
    }

    protected override void UnloadContent()
    {
        _spriteBatch?.Dispose();
        _pixel?.Dispose();
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();
        if (!_hasPreviousInput)
        {
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            _hasPreviousInput = true;
        }

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _elapsed += gameTime.ElapsedGameTime;
        _statusSeconds = MathF.Max(0f, _statusSeconds - dt);

        if (_autoExitMilliseconds is int autoExitMs && _elapsed.TotalMilliseconds >= autoExitMs)
        {
            Exit();
        }

        if (IsNewKey(keyboard, Keys.Escape))
        {
            Exit();
        }

        if (IsCtrlDown(keyboard) && IsNewKey(keyboard, Keys.S))
        {
            Save();
        }

        if (IsNewKey(keyboard, Keys.H))
        {
            _showHelp = !_showHelp;
        }

        if (IsNewKey(keyboard, Keys.V))
        {
            _viewMode = _viewMode == EditorViewMode.TopDown ? EditorViewMode.Orbit3D : EditorViewMode.TopDown;
            SetStatus(_viewMode == EditorViewMode.Orbit3D ? "3D VIEW" : "TOP VIEW");
        }

        if (IsNewKey(keyboard, Keys.X))
        {
            _elevationViewScale = Math.Clamp(_elevationViewScale + 0.5f, MinElevationViewScale, MaxElevationViewScale);
            SetStatus("HEIGHT SCALE");
        }
        else if (IsNewKey(keyboard, Keys.Z))
        {
            _elevationViewScale = Math.Clamp(_elevationViewScale - 0.5f, MinElevationViewScale, MaxElevationViewScale);
            SetStatus("HEIGHT SCALE");
        }

        if (IsNewKey(keyboard, Keys.Home))
        {
            FitView();
            SetStatus("VIEW FIT");
        }

        UpdatePointSelection(keyboard);
        UpdatePointEditing(keyboard);
        UpdateMouseEditing(mouse);

        _previousKeyboard = keyboard;
        _previousMouse = mouse;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_spriteBatch is null || _pixel is null || _font is null)
        {
            return;
        }

        GraphicsDevice.Clear(new Color(10, 12, 13));
        Rectangle canvas = CanvasBounds;

        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);

        DrawPanel(_spriteBatch, canvas, new Color(18, 23, 24));
        if (_viewMode == EditorViewMode.Orbit3D)
        {
            DrawGrid3D(_spriteBatch, canvas);
            DrawTrackPreview3D(_spriteBatch, canvas);
        }
        else
        {
            DrawGrid(_spriteBatch, canvas);
            DrawTrackPreview(_spriteBatch, canvas);
        }

        DrawControlPoints(_spriteBatch, canvas);
        DrawInspector(_spriteBatch, _font);
        _spriteBatch.End();

        base.Draw(gameTime);
    }

    private Rectangle CanvasBounds => new(16, 44, WindowWidth - InspectorWidth - 44, WindowHeight - 64);

    private void UpdatePointSelection(KeyboardState keyboard)
    {
        if (IsNewKey(keyboard, Keys.A))
        {
            _selectedPoint = (_selectedPoint - 1 + _controlPoints.Length) % _controlPoints.Length;
            SetStatus("POINT SELECTED");
        }
        else if (IsNewKey(keyboard, Keys.D))
        {
            _selectedPoint = (_selectedPoint + 1) % _controlPoints.Length;
            SetStatus("POINT SELECTED");
        }

        if (IsNewKey(keyboard, Keys.I))
        {
            InsertPointAfterSelection();
        }

        if (IsNewKey(keyboard, Keys.Delete) && _controlPoints.Length > 4)
        {
            DeleteSelectedPoint();
        }

        if (!IsCtrlDown(keyboard) && IsNewKey(keyboard, Keys.S))
        {
            ToggleSelectedSegmentShape();
        }

        if (IsNewKey(keyboard, Keys.Tab))
        {
            _editMode = _editMode switch
            {
                EditorEditMode.Point => EditorEditMode.LeftRoad,
                EditorEditMode.LeftRoad => EditorEditMode.RightRoad,
                EditorEditMode.RightRoad => EditorEditMode.LeftGrass,
                EditorEditMode.LeftGrass => EditorEditMode.RightGrass,
                EditorEditMode.RightGrass => EditorEditMode.LeftWall,
                EditorEditMode.LeftWall => EditorEditMode.RightWall,
                _ => EditorEditMode.Point
            };
            SetStatus($"MODE {_editMode}");
        }
    }

    private void UpdatePointEditing(KeyboardState keyboard)
    {
        float elevationStep = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? 2.5f : 0.5f;
        if (IsNewKey(keyboard, Keys.E))
        {
            AdjustSelectedElevation(elevationStep);
        }
        else if (IsNewKey(keyboard, Keys.Q))
        {
            AdjustSelectedElevation(-elevationStep);
        }

        float bankStep = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? 1.0f : 0.25f;
        if (IsNewKey(keyboard, Keys.R))
        {
            AdjustBankAtSelectedPoint(bankStep);
        }
        else if (IsNewKey(keyboard, Keys.F))
        {
            AdjustBankAtSelectedPoint(-bankStep);
        }

        if (IsNewKey(keyboard, Keys.OemPlus) || IsNewKey(keyboard, Keys.Add))
        {
            AdjustSelectedWidth(1f);
        }
        else if (IsNewKey(keyboard, Keys.OemMinus) || IsNewKey(keyboard, Keys.Subtract))
        {
            AdjustSelectedWidth(-1f);
        }
    }

    private void UpdateMouseEditing(MouseState mouse)
    {
        Rectangle canvas = CanvasBounds;
        Vector2 mousePoint = new(mouse.X, mouse.Y);
        bool overCanvas = canvas.Contains(mouse.X, mouse.Y);

        int wheelDelta = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (wheelDelta != 0 && overCanvas)
        {
            float oldZoom = _zoom;
            _zoom = Math.Clamp(_zoom * MathF.Pow(1.12f, wheelDelta / 120f), MinZoom, MaxZoom);
            if (_viewMode == EditorViewMode.TopDown)
            {
                Vector2 before = ScreenToWorld(mousePoint, canvas, oldZoom);
                Vector2 after = ScreenToWorld(mousePoint, canvas, _zoom);
                _cameraCenter += before - after;
            }
        }

        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released && overCanvas)
        {
            WidthHandle handle = FindNearestWidthHandle(mousePoint, canvas, 18f);
            if (handle != WidthHandle.None)
            {
                _draggedWidthHandle = handle;
                SetStatus($"DRAG {handle}");
            }
            else
            {
                int nearest = FindNearestControlPoint(mousePoint, canvas, 16f);
                if (nearest >= 0)
                {
                    _selectedPoint = nearest;
                    _draggedPoint = nearest;
                    SetStatus("DRAG POINT");
                }
            }
        }

        if (mouse.LeftButton == ButtonState.Released)
        {
            _draggedPoint = -1;
            _draggedWidthHandle = WidthHandle.None;
        }

        if (mouse.LeftButton == ButtonState.Pressed && _draggedWidthHandle != WidthHandle.None && overCanvas)
        {
            Vector2 world = _viewMode == EditorViewMode.Orbit3D
                ? ScreenToWorldAtElevation(mousePoint, canvas, _controlPoints[_selectedPoint].ElevationMeters)
                : ScreenToWorld(mousePoint, canvas);
            DragSelectedWidthHandle(_draggedWidthHandle, world);
        }
        else if (mouse.LeftButton == ButtonState.Pressed && _draggedPoint >= 0 && overCanvas)
        {
            TrackControlPoint point = _controlPoints[_draggedPoint];
            Vector2 world = _viewMode == EditorViewMode.Orbit3D
                ? ScreenToWorldAtElevation(mousePoint, canvas, point.ElevationMeters)
                : ScreenToWorld(mousePoint, canvas);
            _controlPoints[_draggedPoint] = new TrackControlPoint(world.X, world.Y, point.ElevationMeters);
            _selectedPoint = _draggedPoint;
            MarkDirty("POINT MOVED");
        }

        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Pressed)
        {
            Vector2 delta = new(mouse.X - _previousMouse.X, mouse.Y - _previousMouse.Y);
            if (_viewMode == EditorViewMode.Orbit3D)
            {
                _orbitYawRadians -= delta.X * 0.007f;
                _orbitPitchRadians = Math.Clamp(_orbitPitchRadians + delta.Y * 0.006f, 0.12f, 1.28f);
            }
            else
            {
                _cameraCenter -= new Vector2(delta.X, -delta.Y) / MathF.Max(0.001f, _zoom);
            }
        }
    }

    private void DrawGrid(SpriteBatch spriteBatch, Rectangle canvas)
    {
        if (_pixel is null)
        {
            return;
        }

        Vector2 topLeft = ScreenToWorld(new Vector2(canvas.Left, canvas.Top), canvas);
        Vector2 bottomRight = ScreenToWorld(new Vector2(canvas.Right, canvas.Bottom), canvas);
        float left = MathF.Min(topLeft.X, bottomRight.X);
        float right = MathF.Max(topLeft.X, bottomRight.X);
        float bottom = MathF.Min(topLeft.Y, bottomRight.Y);
        float top = MathF.Max(topLeft.Y, bottomRight.Y);
        float step = _zoom < 0.45f ? 200f : _zoom < 0.9f ? 100f : 50f;
        Color minor = new(45, 54, 54, 150);
        Color major = new(70, 86, 82, 185);

        for (float x = MathF.Floor(left / step) * step; x <= right; x += step)
        {
            Color color = MathF.Abs(x) < 0.1f ? major : minor;
            DrawLine(spriteBatch, WorldToScreen(new Vector2(x, bottom), canvas), WorldToScreen(new Vector2(x, top), canvas), 1f, color);
        }

        for (float z = MathF.Floor(bottom / step) * step; z <= top; z += step)
        {
            Color color = MathF.Abs(z) < 0.1f ? major : minor;
            DrawLine(spriteBatch, WorldToScreen(new Vector2(left, z), canvas), WorldToScreen(new Vector2(right, z), canvas), 1f, color);
        }
    }

    private void DrawGrid3D(SpriteBatch spriteBatch, Rectangle canvas)
    {
        if (_pixel is null)
        {
            return;
        }

        float minX = _controlPoints.Min(point => point.X) - 160f;
        float maxX = _controlPoints.Max(point => point.X) + 160f;
        float minZ = _controlPoints.Min(point => point.Z) - 160f;
        float maxZ = _controlPoints.Max(point => point.Z) + 160f;
        float step = _zoom < 0.45f ? 200f : _zoom < 0.9f ? 100f : 50f;
        Color minor = new(45, 54, 54, 125);
        Color major = new(76, 92, 88, 170);

        for (float x = MathF.Floor(minX / step) * step; x <= maxX; x += step)
        {
            Color color = MathF.Abs(x) < 0.1f ? major : minor;
            DrawLine(
                spriteBatch,
                ProjectWorldPoint(new Vector3(x, 0f, minZ), canvas),
                ProjectWorldPoint(new Vector3(x, 0f, maxZ), canvas),
                1f,
                color);
        }

        for (float z = MathF.Floor(minZ / step) * step; z <= maxZ; z += step)
        {
            Color color = MathF.Abs(z) < 0.1f ? major : minor;
            DrawLine(
                spriteBatch,
                ProjectWorldPoint(new Vector3(minX, 0f, z), canvas),
                ProjectWorldPoint(new Vector3(maxX, 0f, z), canvas),
                1f,
                color);
        }
    }

    private void DrawTrackPreview(SpriteBatch spriteBatch, Rectangle canvas)
    {
        Vector2[] preview = BuildPreviewSpline(8);
        if (preview.Length < 2)
        {
            return;
        }

        Color road = new(72, 76, 75);
        Color roadEdge = new(168, 178, 172);
        Color center = new(230, 228, 202, 185);

        for (int i = 0; i < preview.Length; i++)
        {
            Vector2 a = WorldToScreen(preview[i], canvas);
            Vector2 b = WorldToScreen(preview[(i + 1) % preview.Length], canvas);
            float roadPixels = Math.Clamp((TotalRoadWidth(SampleWidthAtPreviewIndex(i, preview.Length)) +
                                          TotalRoadWidth(SampleWidthAtPreviewIndex((i + 1) % preview.Length, preview.Length))) * 0.5f * _zoom, 4f, 44f);
            DrawLine(spriteBatch, a, b, roadPixels + 4f, roadEdge);
            DrawLine(spriteBatch, a, b, roadPixels, road);
            if (i % 3 == 0)
            {
                DrawLine(spriteBatch, a, b, 1f, center);
            }
        }

        foreach (float marker in _definition.SectorMarkers)
        {
            if (TryGetFrameAtProgress(preview, marker, out Vector2 point, out Vector2 tangent))
            {
                Vector2 normal = new(-tangent.Y, tangent.X);
                Vector2 a = WorldToScreen(point - normal * (_roadWidthMeters * 0.75f), canvas);
                Vector2 b = WorldToScreen(point + normal * (_roadWidthMeters * 0.75f), canvas);
                DrawLine(spriteBatch, a, b, 3f, new Color(250, 214, 98, 210));
            }
        }

        DrawStraightSegmentHighlights(spriteBatch, canvas);
        DrawWidthBoundaries(spriteBatch, canvas, preview);
    }

    private void DrawTrackPreview3D(SpriteBatch spriteBatch, Rectangle canvas)
    {
        Vector3[] preview = BuildPreviewSpline3D(8);
        if (preview.Length < 2)
        {
            return;
        }

        Color road = new(72, 76, 75);
        Color roadEdge = new(168, 178, 172);
        Color center = new(230, 228, 202, 185);

        for (int i = 0; i < preview.Length; i++)
        {
            Vector2 a = ProjectWorldPoint(preview[i], canvas);
            Vector2 b = ProjectWorldPoint(preview[(i + 1) % preview.Length], canvas);
            float roadPixels = Math.Clamp((TotalRoadWidth(SampleWidthAtPreviewIndex(i, preview.Length)) +
                                          TotalRoadWidth(SampleWidthAtPreviewIndex((i + 1) % preview.Length, preview.Length))) * 0.5f * _zoom * 0.85f, 4f, 38f);
            DrawLine(spriteBatch, a, b, roadPixels + 4f, roadEdge);
            DrawLine(spriteBatch, a, b, roadPixels, road);
            if (i % 3 == 0)
            {
                DrawLine(spriteBatch, a, b, 1f, center);
            }
        }

        foreach (float marker in _definition.SectorMarkers)
        {
            if (TryGetFrameAtProgress(preview, marker, out Vector3 point, out Vector2 tangent))
            {
                Vector2 normal = new(-tangent.Y, tangent.X);
                Vector3 a3 = new(point.X - normal.X * (_roadWidthMeters * 0.75f), point.Y, point.Z - normal.Y * (_roadWidthMeters * 0.75f));
                Vector3 b3 = new(point.X + normal.X * (_roadWidthMeters * 0.75f), point.Y, point.Z + normal.Y * (_roadWidthMeters * 0.75f));
                DrawLine(spriteBatch, ProjectWorldPoint(a3, canvas), ProjectWorldPoint(b3, canvas), 3f, new Color(250, 214, 98, 220));
            }
        }

        DrawStraightSegmentHighlights3D(spriteBatch, canvas);
        DrawWidthBoundaries3D(spriteBatch, canvas, preview);
    }

    private void DrawStraightSegmentHighlights(SpriteBatch spriteBatch, Rectangle canvas)
    {
        for (int i = 0; i < _controlPoints.Length; i++)
        {
            if (GetSegmentShape(i) != TrackSegmentShapeKind.Straight)
            {
                continue;
            }

            TrackControlPoint from = _controlPoints[i];
            TrackControlPoint to = _controlPoints[(i + 1) % _controlPoints.Length];
            Vector2 a = WorldToScreen(new Vector2(from.X, from.Z), canvas);
            Vector2 b = WorldToScreen(new Vector2(to.X, to.Z), canvas);
            DrawLine(spriteBatch, a, b, i == _selectedPoint ? 5f : 3f, new Color(86, 210, 230, i == _selectedPoint ? 235 : 170));
        }
    }

    private void DrawStraightSegmentHighlights3D(SpriteBatch spriteBatch, Rectangle canvas)
    {
        for (int i = 0; i < _controlPoints.Length; i++)
        {
            if (GetSegmentShape(i) != TrackSegmentShapeKind.Straight)
            {
                continue;
            }

            TrackControlPoint from = _controlPoints[i];
            TrackControlPoint to = _controlPoints[(i + 1) % _controlPoints.Length];
            Vector2 a = ProjectControlPoint(from, canvas);
            Vector2 b = ProjectControlPoint(to, canvas);
            DrawLine(spriteBatch, a, b, i == _selectedPoint ? 6f : 4f, new Color(86, 210, 230, i == _selectedPoint ? 245 : 180));
        }
    }

    private void DrawWidthBoundaries(SpriteBatch spriteBatch, Rectangle canvas, Vector2[] preview)
    {
        DrawBoundaryPair(spriteBatch, canvas, preview, TrackBoundaryOverlay.Road, new Color(232, 232, 214, 120), 1f);
        DrawBoundaryPair(spriteBatch, canvas, preview, TrackBoundaryOverlay.Grass, new Color(92, 210, 126, 145), 1f);
        DrawBoundaryPair(spriteBatch, canvas, preview, TrackBoundaryOverlay.Wall, new Color(230, 86, 76, 190), 2f);
    }

    private void DrawBoundaryPair(
        SpriteBatch spriteBatch,
        Rectangle canvas,
        Vector2[] preview,
        TrackBoundaryOverlay overlay,
        Color color,
        float lineWidth)
    {
        if (preview.Length < 2)
        {
            return;
        }

        for (int i = 0; i < preview.Length; i++)
        {
            int next = (i + 1) % preview.Length;
            Vector2 normal0 = GetPreviewLeftNormal(preview, i);
            Vector2 normal1 = GetPreviewLeftNormal(preview, next);
            TrackWidthPoint width0 = SampleWidthAtPreviewIndex(i, preview.Length);
            TrackWidthPoint width1 = SampleWidthAtPreviewIndex(next, preview.Length);
            float leftOffset0 = GetBoundaryOffset(width0, overlay, TrackSide.Left);
            float leftOffset1 = GetBoundaryOffset(width1, overlay, TrackSide.Left);
            float rightOffset0 = GetBoundaryOffset(width0, overlay, TrackSide.Right);
            float rightOffset1 = GetBoundaryOffset(width1, overlay, TrackSide.Right);
            Vector2 p0 = preview[i];
            Vector2 p1 = preview[next];
            DrawLine(spriteBatch, WorldToScreen(p0 + normal0 * leftOffset0, canvas), WorldToScreen(p1 + normal1 * leftOffset1, canvas), lineWidth, color);
            DrawLine(spriteBatch, WorldToScreen(p0 - normal0 * rightOffset0, canvas), WorldToScreen(p1 - normal1 * rightOffset1, canvas), lineWidth, color);
        }
    }

    private void DrawWidthBoundaries3D(SpriteBatch spriteBatch, Rectangle canvas, Vector3[] preview)
    {
        DrawBoundaryPair3D(spriteBatch, canvas, preview, TrackBoundaryOverlay.Road, new Color(232, 232, 214, 120), 1f);
        DrawBoundaryPair3D(spriteBatch, canvas, preview, TrackBoundaryOverlay.Grass, new Color(92, 210, 126, 145), 1f);
        DrawBoundaryPair3D(spriteBatch, canvas, preview, TrackBoundaryOverlay.Wall, new Color(230, 86, 76, 190), 2f);
    }

    private void DrawBoundaryPair3D(
        SpriteBatch spriteBatch,
        Rectangle canvas,
        Vector3[] preview,
        TrackBoundaryOverlay overlay,
        Color color,
        float lineWidth)
    {
        if (preview.Length < 2)
        {
            return;
        }

        for (int i = 0; i < preview.Length; i++)
        {
            int next = (i + 1) % preview.Length;
            Vector2 normal0 = GetPreviewLeftNormal(preview, i);
            Vector2 normal1 = GetPreviewLeftNormal(preview, next);
            TrackWidthPoint width0 = SampleWidthAtPreviewIndex(i, preview.Length);
            TrackWidthPoint width1 = SampleWidthAtPreviewIndex(next, preview.Length);
            float leftOffset0 = GetBoundaryOffset(width0, overlay, TrackSide.Left);
            float leftOffset1 = GetBoundaryOffset(width1, overlay, TrackSide.Left);
            float rightOffset0 = GetBoundaryOffset(width0, overlay, TrackSide.Right);
            float rightOffset1 = GetBoundaryOffset(width1, overlay, TrackSide.Right);
            Vector3 p0 = preview[i];
            Vector3 p1 = preview[next];
            Vector3 left0 = new(normal0.X, 0f, normal0.Y);
            Vector3 left1 = new(normal1.X, 0f, normal1.Y);
            DrawLine(spriteBatch, ProjectWorldPoint(p0 + left0 * leftOffset0, canvas), ProjectWorldPoint(p1 + left1 * leftOffset1, canvas), lineWidth, color);
            DrawLine(spriteBatch, ProjectWorldPoint(p0 - left0 * rightOffset0, canvas), ProjectWorldPoint(p1 - left1 * rightOffset1, canvas), lineWidth, color);
        }
    }

    private void DrawControlPoints(SpriteBatch spriteBatch, Rectangle canvas)
    {
        for (int i = 0; i < _controlPoints.Length; i++)
        {
            TrackControlPoint point = _controlPoints[i];
            Vector2 screen = _viewMode == EditorViewMode.Orbit3D
                ? ProjectControlPoint(point, canvas)
                : WorldToScreen(new Vector2(point.X, point.Z), canvas);
            if (_viewMode == EditorViewMode.Orbit3D && point.ElevationMeters > 0.01f)
            {
                DrawLine(
                    spriteBatch,
                    ProjectWorldPoint(new Vector3(point.X, 0f, point.Z), canvas),
                    screen,
                    i == _selectedPoint ? 2f : 1f,
                    new Color(120, 146, 140, i == _selectedPoint ? 220 : 135));
            }

            bool selected = i == _selectedPoint;
            Color color = selected ? new Color(255, 226, 105) : i == 0 ? new Color(98, 230, 126) : new Color(220, 226, 218);
            int size = selected ? 14 : 9;
            FillRect(spriteBatch, new Rectangle((int)(screen.X - size * 0.5f), (int)(screen.Y - size * 0.5f), size, size), color);
            DrawRect(spriteBatch, new Rectangle((int)(screen.X - size * 0.5f) - 1, (int)(screen.Y - size * 0.5f) - 1, size + 2, size + 2), new Color(10, 12, 13, 220));
        }

        DrawSelectedWidthHandles(spriteBatch, canvas);
    }

    private void DrawSelectedWidthHandles(SpriteBatch spriteBatch, Rectangle canvas)
    {
        TrackControlPoint center = _controlPoints[_selectedPoint];
        Vector2 centerScreen = _viewMode == EditorViewMode.Orbit3D
            ? ProjectControlPoint(center, canvas)
            : WorldToScreen(new Vector2(center.X, center.Z), canvas);
        foreach (WidthHandle handle in WidthHandleValues)
        {
            if (handle == WidthHandle.None)
            {
                continue;
            }

            Vector2 handleScreen = WidthHandleToScreen(handle, canvas);
            Color color = WidthHandleColor(handle);
            DrawLine(spriteBatch, centerScreen, handleScreen, 1f, new Color((int)color.R, (int)color.G, (int)color.B, 120));
            int size = handle == _draggedWidthHandle ? 13 : 10;
            FillRect(spriteBatch, new Rectangle((int)(handleScreen.X - size * 0.5f), (int)(handleScreen.Y - size * 0.5f), size, size), color);
            DrawRect(spriteBatch, new Rectangle((int)(handleScreen.X - size * 0.5f) - 1, (int)(handleScreen.Y - size * 0.5f) - 1, size + 2, size + 2), new Color(8, 10, 11, 230));
        }
    }

    private WidthHandle FindNearestWidthHandle(Vector2 screenPoint, Rectangle canvas, float maxDistancePixels)
    {
        WidthHandle nearest = WidthHandle.None;
        float nearestDistance = maxDistancePixels * maxDistancePixels;
        foreach (WidthHandle handle in WidthHandleValues)
        {
            if (handle == WidthHandle.None)
            {
                continue;
            }

            Vector2 handleScreen = WidthHandleToScreen(handle, canvas);
            float distance = Vector2.DistanceSquared(screenPoint, handleScreen);
            if (distance < nearestDistance)
            {
                nearest = handle;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private Vector2 WidthHandleToScreen(WidthHandle handle, Rectangle canvas)
    {
        Vector2 world = WidthHandleToWorld(handle);
        if (_viewMode == EditorViewMode.Orbit3D)
        {
            return ProjectWorldPoint(new Vector3(world.X, _controlPoints[_selectedPoint].ElevationMeters, world.Y), canvas);
        }

        return WorldToScreen(world, canvas);
    }

    private Vector2 WidthHandleToWorld(WidthHandle handle)
    {
        TrackControlPoint center = _controlPoints[_selectedPoint];
        Vector2 left = GetControlPointLeftNormal(_selectedPoint);
        TrackWidthPoint width = GetWidthPoint(_selectedPoint);
        float offset = handle switch
        {
            WidthHandle.LeftRoad => width.LeftRoadWidthMeters,
            WidthHandle.RightRoad => -width.RightRoadWidthMeters,
            WidthHandle.LeftGrass => width.LeftRoadWidthMeters + width.LeftGrassWidthMeters,
            WidthHandle.RightGrass => -width.RightRoadWidthMeters - width.RightGrassWidthMeters,
            WidthHandle.LeftWall => width.LeftRoadWidthMeters + width.LeftWallOffsetMeters,
            WidthHandle.RightWall => -width.RightRoadWidthMeters - width.RightWallOffsetMeters,
            _ => 0f
        };
        return new Vector2(center.X, center.Z) + left * offset;
    }

    private static Color WidthHandleColor(WidthHandle handle)
    {
        return handle switch
        {
            WidthHandle.LeftRoad or WidthHandle.RightRoad => new Color(232, 232, 214),
            WidthHandle.LeftGrass or WidthHandle.RightGrass => new Color(92, 210, 126),
            WidthHandle.LeftWall or WidthHandle.RightWall => new Color(230, 86, 76),
            _ => Color.White
        };
    }

    private void DrawInspector(SpriteBatch spriteBatch, PixelFont font)
    {
        Rectangle panel = new(WindowWidth - InspectorWidth, 0, InspectorWidth, WindowHeight);
        DrawPanel(spriteBatch, panel, new Color(5, 7, 8, 235));

        TrackControlPoint selected = _controlPoints[_selectedPoint];
        TrackWidthPoint selectedWidth = GetWidthPoint(_selectedPoint);
        float bank = SampleBank(GetSelectedProgress());
        TrackSegmentShapeKind segmentShape = GetSegmentShape(_selectedPoint);
        TrackGeometryMetrics metrics = TrackScene.MeasureGeometry(BuildCurrentDefinition());
        Color title = new(236, 242, 232);
        Color text = new(184, 196, 190);
        Color accent = new(250, 235, 142);
        int x = panel.X + 22;
        int y = 24;

        font.Draw(spriteBatch, "TRACK EDITOR", x, y, 2, title);
        y += 30;
        font.Draw(spriteBatch, _definition.DisplayName, x, y, 1, accent);
        y += 20;
        font.Draw(spriteBatch, _dirty ? "DIRTY YES" : "DIRTY NO", x, y, 1, _dirty ? accent : new Color(120, 210, 138));
        y += 20;
        if (_statusSeconds > 0f)
        {
            font.Draw(spriteBatch, _statusText, x, y, 1, accent);
        }
        else
        {
            font.Draw(spriteBatch, "READY", x, y, 1, text);
        }

        y += 34;
        font.Draw(spriteBatch, $"POINT {_selectedPoint + 1}/{_controlPoints.Length}", x, y, 1, title);
        y += 18;
        font.Draw(spriteBatch, $"MODE {_editMode}", x, y, 1, _editMode == EditorEditMode.Point ? text : accent);
        y += 18;
        font.Draw(spriteBatch, $"X {selected.X:0.0}", x, y, 1, text);
        y += 14;
        font.Draw(spriteBatch, $"Z {selected.Z:0.0}", x, y, 1, text);
        y += 14;
        font.Draw(spriteBatch, $"ELEV {selected.ElevationMeters:0.0} M", x, y, 1, text);
        y += 14;
        font.Draw(spriteBatch, $"BANK {bank:0.00} DEG", x, y, 1, text);
        y += 22;
        font.Draw(spriteBatch, $"SEG {segmentShape}", x, y, 1, segmentShape == TrackSegmentShapeKind.Straight ? new Color(86, 210, 230) : text);
        y += 14;
        font.Draw(spriteBatch, $"VIEW {(_viewMode == EditorViewMode.Orbit3D ? "3D" : "TOP")}", x, y, 1, _viewMode == EditorViewMode.Orbit3D ? new Color(86, 210, 230) : text);
        y += 14;
        if (_viewMode == EditorViewMode.Orbit3D)
        {
            font.Draw(spriteBatch, $"HEIGHT X{_elevationViewScale:0.0}", x, y, 1, text);
            y += 14;
        }

        font.Draw(spriteBatch, $"L ROAD {selectedWidth.LeftRoadWidthMeters:0.0} M", x, y, 1, _editMode == EditorEditMode.LeftRoad ? accent : text);
        y += 14;
        font.Draw(spriteBatch, $"R ROAD {selectedWidth.RightRoadWidthMeters:0.0} M", x, y, 1, _editMode == EditorEditMode.RightRoad ? accent : text);
        y += 14;
        font.Draw(spriteBatch, $"L GRASS {selectedWidth.LeftGrassWidthMeters:0.0} M", x, y, 1, _editMode == EditorEditMode.LeftGrass ? accent : text);
        y += 14;
        font.Draw(spriteBatch, $"R GRASS {selectedWidth.RightGrassWidthMeters:0.0} M", x, y, 1, _editMode == EditorEditMode.RightGrass ? accent : text);
        y += 14;
        font.Draw(spriteBatch, $"L WALL {selectedWidth.LeftWallOffsetMeters:0.0} M", x, y, 1, _editMode == EditorEditMode.LeftWall ? accent : text);
        y += 14;
        font.Draw(spriteBatch, $"R WALL {selectedWidth.RightWallOffsetMeters:0.0} M", x, y, 1, _editMode == EditorEditMode.RightWall ? accent : text);
        y += 14;
        font.Draw(spriteBatch, $"TARGET {_definition.LengthMeters} M", x, y, 1, text);
        y += 14;
        font.Draw(spriteBatch, $"MEASURE {metrics.LengthMeters:0.0} M", x, y, 1, text);
        y += 14;
        font.Draw(spriteBatch, $"RANGE {metrics.ElevationDifferenceMeters:0.0} M", x, y, 1, text);

        y += 34;
        font.Draw(spriteBatch, "CONTROLS", x, y, 1, title);
        y += 18;
        if (_showHelp)
        {
            string[] lines =
            [
                "LEFT DRAG POINT/HANDLE",
                _viewMode == EditorViewMode.Orbit3D ? "RIGHT DRAG TILT" : "RIGHT DRAG PAN",
                "WHEEL ZOOM",
                "V TOP 3D VIEW",
                "Z X HEIGHT SCALE",
                "TAB EDIT MODE",
                "A D SELECT POINT",
                "S CURVE STRAIGHT",
                "I INSERT AFTER",
                "DEL REMOVE POINT",
                "Q E ELEVATION",
                "F R BANK",
                "PLUS MINUS VALUE",
                "HOME FIT VIEW",
                "CTRL S SAVE",
                "H HIDE HELP",
                "ESC EXIT"
            ];

            foreach (string line in lines)
            {
                font.Draw(spriteBatch, line, x, y, 1, text);
                y += 14;
            }
        }
        else
        {
            font.Draw(spriteBatch, "H SHOW HELP", x, y, 1, text);
        }
    }

    private void InsertPointAfterSelection()
    {
        int nextIndex = (_selectedPoint + 1) % _controlPoints.Length;
        TrackControlPoint current = _controlPoints[_selectedPoint];
        TrackControlPoint next = _controlPoints[nextIndex];
        TrackControlPoint inserted = new(
            (current.X + next.X) * 0.5f,
            (current.Z + next.Z) * 0.5f,
            (current.ElevationMeters + next.ElevationMeters) * 0.5f);

        List<TrackControlPoint> points = _controlPoints.ToList();
        points.Insert(_selectedPoint + 1, inserted);
        _controlPoints = points.ToArray();
        InsertSegmentShapeAfterSelection();
        InsertWidthPointAfterSelection();
        _selectedPoint++;
        MarkDirty("POINT INSERTED");
    }

    private void DeleteSelectedPoint()
    {
        List<TrackControlPoint> points = _controlPoints.ToList();
        points.RemoveAt(_selectedPoint);
        _controlPoints = points.ToArray();
        DeleteSegmentShapeAtSelection();
        DeleteWidthPointAtSelection();
        _selectedPoint = Math.Clamp(_selectedPoint, 0, _controlPoints.Length - 1);
        MarkDirty("POINT REMOVED");
    }

    private void ToggleSelectedSegmentShape()
    {
        TrackSegmentShapeKind current = GetSegmentShape(_selectedPoint);
        SetSegmentShape(
            _selectedPoint,
            current == TrackSegmentShapeKind.Straight ? TrackSegmentShapeKind.Curve : TrackSegmentShapeKind.Straight);
        MarkDirty(GetSegmentShape(_selectedPoint) == TrackSegmentShapeKind.Straight ? "SEG STRAIGHT" : "SEG CURVE");
    }

    private void InsertSegmentShapeAfterSelection()
    {
        TrackSegmentShapeKind inheritedShape = GetSegmentShape(_selectedPoint);
        List<TrackSegmentShape> shapes = _segmentShapes.ToList();
        shapes.Insert(_selectedPoint + 1, new TrackSegmentShape(_selectedPoint + 1, inheritedShape));
        _segmentShapes = ReindexSegmentShapes(shapes, _controlPoints.Length);
        SetSegmentShape(_selectedPoint, inheritedShape);
    }

    private void DeleteSegmentShapeAtSelection()
    {
        List<TrackSegmentShape> shapes = _segmentShapes.ToList();
        if (_selectedPoint >= 0 && _selectedPoint < shapes.Count)
        {
            shapes.RemoveAt(_selectedPoint);
        }

        _segmentShapes = ReindexSegmentShapes(shapes, _controlPoints.Length);
    }

    private void InsertWidthPointAfterSelection()
    {
        TrackWidthPoint inherited = GetWidthPoint(_selectedPoint);
        List<TrackWidthPoint> points = _widthPoints.ToList();
        points.Insert(_selectedPoint + 1, inherited with { ControlPoint = _selectedPoint + 1 });
        _widthPoints = ReindexWidthPoints(points, _controlPoints.Length, AverageRoadWidthMeters());
    }

    private void DeleteWidthPointAtSelection()
    {
        List<TrackWidthPoint> points = _widthPoints.ToList();
        if (_selectedPoint >= 0 && _selectedPoint < points.Count)
        {
            points.RemoveAt(_selectedPoint);
        }

        _widthPoints = ReindexWidthPoints(points, _controlPoints.Length, AverageRoadWidthMeters());
    }

    private void AdjustSelectedElevation(float delta)
    {
        TrackControlPoint point = _controlPoints[_selectedPoint];
        _controlPoints[_selectedPoint] = point with
        {
            ElevationMeters = MathF.Max(0f, point.ElevationMeters + delta)
        };
        MarkDirty("ELEVATION");
    }

    private void AdjustBankAtSelectedPoint(float delta)
    {
        float progress = GetSelectedProgress();
        List<TrackBankPoint> points = _bankProfile.OrderBy(point => point.Progress).ToList();
        int index = points.FindIndex(point => MathF.Abs(point.Progress - progress) <= 0.025f);
        if (index < 0)
        {
            points.Add(new TrackBankPoint(progress, SampleBank(progress)));
            points = points.OrderBy(point => point.Progress).ToList();
            index = points.FindIndex(point => MathF.Abs(point.Progress - progress) <= 0.0001f);
        }

        TrackBankPoint bankPoint = points[index];
        points[index] = bankPoint with
        {
            BankDegrees = Math.Clamp(bankPoint.BankDegrees + delta, -12f, 12f)
        };
        _bankProfile = points.ToArray();
        MarkDirty("BANK");
    }

    private void AdjustSelectedWidth(float direction)
    {
        if (_editMode == EditorEditMode.Point)
        {
            return;
        }

        float step = Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift) ? 2.5f : 0.5f;
        TrackWidthPoint point = GetWidthPoint(_selectedPoint);
        TrackWidthPoint adjusted = _editMode switch
        {
            EditorEditMode.LeftRoad => point with
            {
                LeftRoadWidthMeters = Math.Clamp(point.LeftRoadWidthMeters + direction * step, 2f, 42f)
            },
            EditorEditMode.RightRoad => point with
            {
                RightRoadWidthMeters = Math.Clamp(point.RightRoadWidthMeters + direction * step, 2f, 42f)
            },
            EditorEditMode.LeftGrass => point with
            {
                LeftGrassWidthMeters = Math.Clamp(point.LeftGrassWidthMeters + direction * step, 0f, 90f)
            },
            EditorEditMode.RightGrass => point with
            {
                RightGrassWidthMeters = Math.Clamp(point.RightGrassWidthMeters + direction * step, 0f, 90f)
            },
            EditorEditMode.LeftWall => point with
            {
                LeftWallOffsetMeters = Math.Clamp(point.LeftWallOffsetMeters + direction * step, 0.5f, 95f)
            },
            EditorEditMode.RightWall => point with
            {
                RightWallOffsetMeters = Math.Clamp(point.RightWallOffsetMeters + direction * step, 0.5f, 95f)
            },
            _ => point
        };

        SetWidthPoint(_selectedPoint, adjusted);
        _roadWidthMeters = AverageRoadWidthMeters();
        MarkDirty(_editMode switch
        {
            EditorEditMode.LeftRoad or EditorEditMode.RightRoad => "ROAD WIDTH",
            EditorEditMode.LeftGrass or EditorEditMode.RightGrass => "GRASS WIDTH",
            EditorEditMode.LeftWall or EditorEditMode.RightWall => "WALL OFFSET",
            _ => "WIDTH"
        });
    }

    private void DragSelectedWidthHandle(WidthHandle handle, Vector2 world)
    {
        TrackControlPoint center = _controlPoints[_selectedPoint];
        Vector2 left = GetControlPointLeftNormal(_selectedPoint);
        Vector2 fromCenter = world - new Vector2(center.X, center.Z);
        float signedOffset = Vector2.Dot(fromCenter, left);
        TrackWidthPoint width = GetWidthPoint(_selectedPoint);

        TrackWidthPoint adjusted = handle switch
        {
            WidthHandle.LeftRoad => width with
            {
                LeftRoadWidthMeters = Math.Clamp(signedOffset, 2f, 42f)
            },
            WidthHandle.RightRoad => width with
            {
                RightRoadWidthMeters = Math.Clamp(-signedOffset, 2f, 42f)
            },
            WidthHandle.LeftGrass => width with
            {
                LeftGrassWidthMeters = Math.Clamp(signedOffset - width.LeftRoadWidthMeters, 0f, 90f)
            },
            WidthHandle.RightGrass => width with
            {
                RightGrassWidthMeters = Math.Clamp(-signedOffset - width.RightRoadWidthMeters, 0f, 90f)
            },
            WidthHandle.LeftWall => width with
            {
                LeftWallOffsetMeters = Math.Clamp(signedOffset - width.LeftRoadWidthMeters, 0.5f, 95f)
            },
            WidthHandle.RightWall => width with
            {
                RightWallOffsetMeters = Math.Clamp(-signedOffset - width.RightRoadWidthMeters, 0.5f, 95f)
            },
            _ => width
        };

        SetWidthPoint(_selectedPoint, adjusted);
        _roadWidthMeters = AverageRoadWidthMeters();
        MarkDirty($"HANDLE {handle}");
    }

    private void Save()
    {
        TrackDefinition current = BuildCurrentDefinition();
        TrackDefinitionFileLoader.SaveFile(current, _trackFile.SortOrder, _trackFile.SourcePath);
        _definition = current;
        _trackFile = new TrackDefinitionFile(current, _trackFile.SortOrder, _trackFile.SourcePath);
        _dirty = false;
        SetStatus("SAVED");
    }

    private TrackDefinition BuildCurrentDefinition()
    {
        float minElevation = _controlPoints.Min(point => point.ElevationMeters);
        float maxElevation = _controlPoints.Max(point => point.ElevationMeters);
        float elevationDifference = MathF.Max(0f, maxElevation - minElevation);
        TrackWidthPoint[] widthPoints = ReindexWidthPoints(_widthPoints, _controlPoints.Length, AverageRoadWidthMeters());
        return _definition with
        {
            RoadHalfWidthMeters = AverageRoadWidthMeters() * 0.5f,
            ElevationDifferenceMeters = elevationDifference,
            ControlPoints = _controlPoints.ToArray(),
            BankProfileDegrees = _bankProfile.OrderBy(point => point.Progress).ToArray(),
            SegmentShapes = ReindexSegmentShapes(_segmentShapes, _controlPoints.Length),
            WidthPoints = widthPoints
        };
    }

    private void FitView()
    {
        float minX = _controlPoints.Min(point => point.X);
        float maxX = _controlPoints.Max(point => point.X);
        float minZ = _controlPoints.Min(point => point.Z);
        float maxZ = _controlPoints.Max(point => point.Z);
        _cameraCenter = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
        float width = MathF.Max(1f, maxX - minX);
        float height = MathF.Max(1f, maxZ - minZ);
        Rectangle canvas = CanvasBounds;
        _zoom = Math.Clamp(MathF.Min(canvas.Width / (width + 120f), canvas.Height / (height + 120f)), MinZoom, MaxZoom);
    }

    private int FindNearestControlPoint(Vector2 screenPoint, Rectangle canvas, float maxDistancePixels)
    {
        int nearest = -1;
        float nearestDistance = maxDistancePixels * maxDistancePixels;
        for (int i = 0; i < _controlPoints.Length; i++)
        {
            TrackControlPoint point = _controlPoints[i];
            Vector2 screen = _viewMode == EditorViewMode.Orbit3D
                ? ProjectControlPoint(point, canvas)
                : WorldToScreen(new Vector2(point.X, point.Z), canvas);
            float distance = Vector2.DistanceSquared(screenPoint, screen);
            if (distance < nearestDistance)
            {
                nearest = i;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private Vector2[] BuildPreviewSpline(int samplesPerSegment)
    {
        Vector2[] controls = _controlPoints.Select(point => new Vector2(point.X, point.Z)).ToArray();
        Vector2[] points = new Vector2[controls.Length * samplesPerSegment];
        int index = 0;
        for (int i = 0; i < controls.Length; i++)
        {
            Vector2 p0 = controls[(i - 1 + controls.Length) % controls.Length];
            Vector2 p1 = controls[i];
            Vector2 p2 = controls[(i + 1) % controls.Length];
            Vector2 p3 = controls[(i + 2) % controls.Length];
            bool straight = GetSegmentShape(i) == TrackSegmentShapeKind.Straight;

            for (int sample = 0; sample < samplesPerSegment; sample++)
            {
                float t = sample / (float)samplesPerSegment;
                points[index++] = straight
                    ? Vector2.Lerp(p1, p2, t)
                    : Vector2.CatmullRom(p0, p1, p2, p3, t);
            }
        }

        return points;
    }

    private Vector3[] BuildPreviewSpline3D(int samplesPerSegment)
    {
        Vector3[] controls = _controlPoints
            .Select(point => new Vector3(point.X, point.ElevationMeters, point.Z))
            .ToArray();
        Vector3[] points = new Vector3[controls.Length * samplesPerSegment];
        int index = 0;
        for (int i = 0; i < controls.Length; i++)
        {
            Vector3 p0 = controls[(i - 1 + controls.Length) % controls.Length];
            Vector3 p1 = controls[i];
            Vector3 p2 = controls[(i + 1) % controls.Length];
            Vector3 p3 = controls[(i + 2) % controls.Length];
            bool straight = GetSegmentShape(i) == TrackSegmentShapeKind.Straight;

            for (int sample = 0; sample < samplesPerSegment; sample++)
            {
                float t = sample / (float)samplesPerSegment;
                points[index++] = straight
                    ? Vector3.Lerp(p1, p2, t)
                    : Vector3.CatmullRom(p0, p1, p2, p3, t);
            }
        }

        return points;
    }

    private TrackSegmentShapeKind GetSegmentShape(int fromControlPoint)
    {
        if (fromControlPoint < 0 || fromControlPoint >= _segmentShapes.Length)
        {
            return TrackSegmentShapeKind.Curve;
        }

        return _segmentShapes[fromControlPoint].Shape;
    }

    private void SetSegmentShape(int fromControlPoint, TrackSegmentShapeKind shape)
    {
        if (fromControlPoint < 0 || fromControlPoint >= _segmentShapes.Length)
        {
            return;
        }

        _segmentShapes[fromControlPoint] = new TrackSegmentShape(fromControlPoint, shape);
    }

    private TrackWidthPoint GetWidthPoint(int controlPoint)
    {
        if (controlPoint < 0 || controlPoint >= _widthPoints.Length)
        {
            return TrackWidthPointFromSymmetric(controlPoint, Math.Clamp(_roadWidthMeters, 4f, 84f), 26f, 1.45f);
        }

        return _widthPoints[controlPoint];
    }

    private void SetWidthPoint(int controlPoint, TrackWidthPoint point)
    {
        if (controlPoint < 0 || controlPoint >= _widthPoints.Length)
        {
            return;
        }

        _widthPoints[controlPoint] = point with
        {
            ControlPoint = controlPoint,
            LeftRoadWidthMeters = Math.Clamp(point.LeftRoadWidthMeters, 2f, 42f),
            RightRoadWidthMeters = Math.Clamp(point.RightRoadWidthMeters, 2f, 42f),
            LeftGrassWidthMeters = Math.Clamp(point.LeftGrassWidthMeters, 0f, 90f),
            RightGrassWidthMeters = Math.Clamp(point.RightGrassWidthMeters, 0f, 90f),
            LeftWallOffsetMeters = Math.Clamp(point.LeftWallOffsetMeters, 0.5f, 95f),
            RightWallOffsetMeters = Math.Clamp(point.RightWallOffsetMeters, 0.5f, 95f)
        };
    }

    private TrackWidthPoint SampleWidthAtPreviewIndex(int previewIndex, int previewCount)
    {
        if (previewCount <= 0 || _controlPoints.Length == 0)
        {
            return TrackWidthPointFromSymmetric(0, Math.Clamp(_roadWidthMeters, 4f, 84f), 26f, 1.45f);
        }

        float controlPosition = previewIndex / (float)previewCount * _controlPoints.Length;
        int a = Math.Clamp((int)MathF.Floor(controlPosition), 0, _controlPoints.Length - 1);
        int b = (a + 1) % _controlPoints.Length;
        float t = controlPosition - MathF.Floor(controlPosition);
        TrackWidthPoint widthA = GetWidthPoint(a);
        TrackWidthPoint widthB = GetWidthPoint(b);
        return new TrackWidthPoint(
            a,
            MathHelper.Lerp(widthA.LeftRoadWidthMeters, widthB.LeftRoadWidthMeters, t),
            MathHelper.Lerp(widthA.RightRoadWidthMeters, widthB.RightRoadWidthMeters, t),
            MathHelper.Lerp(widthA.LeftGrassWidthMeters, widthB.LeftGrassWidthMeters, t),
            MathHelper.Lerp(widthA.RightGrassWidthMeters, widthB.RightGrassWidthMeters, t),
            MathHelper.Lerp(widthA.LeftWallOffsetMeters, widthB.LeftWallOffsetMeters, t),
            MathHelper.Lerp(widthA.RightWallOffsetMeters, widthB.RightWallOffsetMeters, t));
    }

    private float AverageRoadWidthMeters()
    {
        return _widthPoints.Length == 0 ? Math.Clamp(_roadWidthMeters, 4f, 84f) : _widthPoints.Average(TotalRoadWidth);
    }

    private static float TotalRoadWidth(TrackWidthPoint width)
    {
        return width.LeftRoadWidthMeters + width.RightRoadWidthMeters;
    }

    private static float GetBoundaryOffset(TrackWidthPoint width, TrackBoundaryOverlay overlay, TrackSide side)
    {
        float road = side == TrackSide.Left ? width.LeftRoadWidthMeters : width.RightRoadWidthMeters;
        return overlay switch
        {
            TrackBoundaryOverlay.Grass => road + (side == TrackSide.Left ? width.LeftGrassWidthMeters : width.RightGrassWidthMeters),
            TrackBoundaryOverlay.Wall => road + (side == TrackSide.Left ? width.LeftWallOffsetMeters : width.RightWallOffsetMeters),
            _ => road
        };
    }

    private static Vector2 GetPreviewLeftNormal(Vector2[] preview, int index)
    {
        Vector2 previous = preview[(index - 1 + preview.Length) % preview.Length];
        Vector2 next = preview[(index + 1) % preview.Length];
        Vector2 tangent = next - previous;
        if (tangent.LengthSquared() <= 0.0001f)
        {
            return Vector2.UnitX;
        }

        tangent.Normalize();
        return new Vector2(-tangent.Y, tangent.X);
    }

    private Vector2 GetControlPointLeftNormal(int index)
    {
        TrackControlPoint previous = _controlPoints[(index - 1 + _controlPoints.Length) % _controlPoints.Length];
        TrackControlPoint next = _controlPoints[(index + 1) % _controlPoints.Length];
        Vector2 tangent = new(next.X - previous.X, next.Z - previous.Z);
        if (tangent.LengthSquared() <= 0.0001f)
        {
            return Vector2.UnitX;
        }

        tangent.Normalize();
        return new Vector2(-tangent.Y, tangent.X);
    }

    private static Vector2 GetPreviewLeftNormal(Vector3[] preview, int index)
    {
        Vector3 previous = preview[(index - 1 + preview.Length) % preview.Length];
        Vector3 next = preview[(index + 1) % preview.Length];
        Vector2 tangent = new(next.X - previous.X, next.Z - previous.Z);
        if (tangent.LengthSquared() <= 0.0001f)
        {
            return Vector2.UnitX;
        }

        tangent.Normalize();
        return new Vector2(-tangent.Y, tangent.X);
    }

    private static TrackSegmentShape[] NormalizeSegmentShapes(
        IReadOnlyList<TrackSegmentShape>? segmentShapes,
        int controlPointCount)
    {
        TrackSegmentShape[] normalized = new TrackSegmentShape[Math.Max(0, controlPointCount)];
        for (int i = 0; i < normalized.Length; i++)
        {
            normalized[i] = new TrackSegmentShape(i, TrackSegmentShapeKind.Curve);
        }

        if (segmentShapes is null)
        {
            return normalized;
        }

        foreach (TrackSegmentShape segment in segmentShapes)
        {
            if (segment.FromControlPoint >= 0 && segment.FromControlPoint < normalized.Length)
            {
                normalized[segment.FromControlPoint] = new TrackSegmentShape(segment.FromControlPoint, segment.Shape);
            }
        }

        return normalized;
    }

    private static TrackSegmentShape[] ReindexSegmentShapes(
        IReadOnlyList<TrackSegmentShape> segmentShapes,
        int controlPointCount)
    {
        TrackSegmentShape[] reindexed = new TrackSegmentShape[Math.Max(0, controlPointCount)];
        for (int i = 0; i < reindexed.Length; i++)
        {
            TrackSegmentShapeKind shape = i < segmentShapes.Count ? segmentShapes[i].Shape : TrackSegmentShapeKind.Curve;
            reindexed[i] = new TrackSegmentShape(i, shape);
        }

        return reindexed;
    }

    private static TrackWidthPoint[] NormalizeWidthPoints(
        IReadOnlyList<TrackWidthPoint>? widthPoints,
        int controlPointCount,
        float defaultRoadWidthMeters)
    {
        TrackWidthPoint[] normalized = new TrackWidthPoint[Math.Max(0, controlPointCount)];
        for (int i = 0; i < normalized.Length; i++)
        {
            normalized[i] = TrackWidthPointFromSymmetric(i, defaultRoadWidthMeters, 26f, 1.45f);
        }

        if (widthPoints is null)
        {
            return normalized;
        }

        foreach (TrackWidthPoint point in widthPoints)
        {
            if (point.ControlPoint >= 0 && point.ControlPoint < normalized.Length)
            {
                normalized[point.ControlPoint] = ClampWidthPoint(point, defaultRoadWidthMeters);
            }
        }

        return normalized;
    }

    private static TrackWidthPoint[] ReindexWidthPoints(
        IReadOnlyList<TrackWidthPoint> widthPoints,
        int controlPointCount,
        float defaultRoadWidthMeters)
    {
        TrackWidthPoint[] reindexed = new TrackWidthPoint[Math.Max(0, controlPointCount)];
        for (int i = 0; i < reindexed.Length; i++)
        {
            TrackWidthPoint point = i < widthPoints.Count
                ? widthPoints[i]
                : TrackWidthPointFromSymmetric(i, defaultRoadWidthMeters, 26f, 1.45f);
            reindexed[i] = ClampWidthPoint(point with { ControlPoint = i }, defaultRoadWidthMeters);
        }

        return reindexed;
    }

    private static TrackWidthPoint TrackWidthPointFromSymmetric(
        int controlPoint,
        float roadWidthMeters,
        float grassWidthMeters,
        float wallOffsetMeters)
    {
        float halfRoad = Math.Clamp(roadWidthMeters, 4f, 84f) * 0.5f;
        return new TrackWidthPoint(
            controlPoint,
            halfRoad,
            halfRoad,
            Math.Clamp(grassWidthMeters, 0f, 90f),
            Math.Clamp(grassWidthMeters, 0f, 90f),
            Math.Clamp(wallOffsetMeters, 0.5f, 95f),
            Math.Clamp(wallOffsetMeters, 0.5f, 95f));
    }

    private static TrackWidthPoint ClampWidthPoint(TrackWidthPoint point, float defaultRoadWidthMeters)
    {
        float halfDefaultRoad = Math.Clamp(defaultRoadWidthMeters, 4f, 84f) * 0.5f;
        return new TrackWidthPoint(
            point.ControlPoint,
            Math.Clamp(point.LeftRoadWidthMeters <= 0f ? halfDefaultRoad : point.LeftRoadWidthMeters, 2f, 42f),
            Math.Clamp(point.RightRoadWidthMeters <= 0f ? halfDefaultRoad : point.RightRoadWidthMeters, 2f, 42f),
            Math.Clamp(point.LeftGrassWidthMeters <= 0f ? 26f : point.LeftGrassWidthMeters, 0f, 90f),
            Math.Clamp(point.RightGrassWidthMeters <= 0f ? 26f : point.RightGrassWidthMeters, 0f, 90f),
            Math.Clamp(point.LeftWallOffsetMeters <= 0f ? 1.45f : point.LeftWallOffsetMeters, 0.5f, 95f),
            Math.Clamp(point.RightWallOffsetMeters <= 0f ? 1.45f : point.RightWallOffsetMeters, 0.5f, 95f));
    }

    private bool TryGetFrameAtProgress(Vector2[] points, float progress, out Vector2 point, out Vector2 tangent)
    {
        point = Vector2.Zero;
        tangent = Vector2.UnitX;
        if (points.Length < 2)
        {
            return false;
        }

        float total = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            total += Vector2.Distance(points[i], points[(i + 1) % points.Length]);
        }

        float target = Math.Clamp(progress, 0f, 1f) * total;
        float distance = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Length];
            float segment = Vector2.Distance(a, b);
            if (distance + segment >= target)
            {
                float t = MathHelper.Clamp((target - distance) / MathF.Max(0.001f, segment), 0f, 1f);
                point = Vector2.Lerp(a, b, t);
                tangent = b - a;
                if (tangent.LengthSquared() <= 0.0001f)
                {
                    tangent = Vector2.UnitX;
                }
                else
                {
                    tangent.Normalize();
                }

                return true;
            }

            distance += segment;
        }

        return false;
    }

    private bool TryGetFrameAtProgress(Vector3[] points, float progress, out Vector3 point, out Vector2 tangent)
    {
        point = Vector3.Zero;
        tangent = Vector2.UnitX;
        if (points.Length < 2)
        {
            return false;
        }

        float total = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            total += Vector2.Distance(new Vector2(points[i].X, points[i].Z), new Vector2(points[(i + 1) % points.Length].X, points[(i + 1) % points.Length].Z));
        }

        float target = Math.Clamp(progress, 0f, 1f) * total;
        float distance = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[(i + 1) % points.Length];
            float segment = Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
            if (distance + segment >= target)
            {
                float t = MathHelper.Clamp((target - distance) / MathF.Max(0.001f, segment), 0f, 1f);
                point = Vector3.Lerp(a, b, t);
                tangent = new Vector2(b.X - a.X, b.Z - a.Z);
                if (tangent.LengthSquared() <= 0.0001f)
                {
                    tangent = Vector2.UnitX;
                }
                else
                {
                    tangent.Normalize();
                }

                return true;
            }

            distance += segment;
        }

        return false;
    }

    private float GetSelectedProgress()
    {
        return _selectedPoint / (float)_controlPoints.Length;
    }

    private float SampleBank(float progress)
    {
        if (_bankProfile.Length == 0)
        {
            return 0f;
        }

        TrackBankPoint[] points = _bankProfile.OrderBy(point => point.Progress).ToArray();
        progress = Wrap01(progress);
        for (int i = 0; i < points.Length; i++)
        {
            TrackBankPoint current = points[i];
            TrackBankPoint next = points[(i + 1) % points.Length];
            float start = current.Progress;
            float end = next.Progress;
            float valueProgress = progress;
            if (i == points.Length - 1)
            {
                end += 1f;
                if (valueProgress < start)
                {
                    valueProgress += 1f;
                }
            }

            if (valueProgress < start || valueProgress > end)
            {
                continue;
            }

            float t = MathHelper.Clamp((valueProgress - start) / MathF.Max(0.001f, end - start), 0f, 1f);
            t = t * t * (3f - 2f * t);
            return MathHelper.Lerp(current.BankDegrees, next.BankDegrees, t);
        }

        return points[^1].BankDegrees;
    }

    private Vector2 WorldToScreen(Vector2 world, Rectangle canvas)
    {
        return new Vector2(
            canvas.Center.X + (world.X - _cameraCenter.X) * _zoom,
            canvas.Center.Y - (world.Y - _cameraCenter.Y) * _zoom);
    }

    private Vector2 ScreenToWorld(Vector2 screen, Rectangle canvas)
    {
        return ScreenToWorld(screen, canvas, _zoom);
    }

    private Vector2 ScreenToWorld(Vector2 screen, Rectangle canvas, float zoom)
    {
        return new Vector2(
            _cameraCenter.X + (screen.X - canvas.Center.X) / MathF.Max(0.001f, zoom),
            _cameraCenter.Y - (screen.Y - canvas.Center.Y) / MathF.Max(0.001f, zoom));
    }

    private Vector2 ProjectControlPoint(TrackControlPoint point, Rectangle canvas)
    {
        return ProjectWorldPoint(new Vector3(point.X, point.ElevationMeters, point.Z), canvas);
    }

    private Vector2 ProjectWorldPoint(Vector3 world, Rectangle canvas)
    {
        float cosYaw = MathF.Cos(_orbitYawRadians);
        float sinYaw = MathF.Sin(_orbitYawRadians);
        float cosPitch = MathF.Cos(_orbitPitchRadians);
        float sinPitch = MathF.Sin(_orbitPitchRadians);
        Vector3 relative = new(
            world.X - _cameraCenter.X,
            world.Y * _elevationViewScale,
            world.Z - _cameraCenter.Y);

        float rotatedX = relative.X * cosYaw - relative.Z * sinYaw;
        float rotatedZ = relative.X * sinYaw + relative.Z * cosYaw;
        float projectedY = relative.Y * cosPitch - rotatedZ * sinPitch;

        return new Vector2(
            canvas.Center.X + rotatedX * _zoom,
            canvas.Center.Y - projectedY * _zoom);
    }

    private Vector2 ScreenToWorldAtElevation(Vector2 screen, Rectangle canvas, float elevationMeters)
    {
        float cosYaw = MathF.Cos(_orbitYawRadians);
        float sinYaw = MathF.Sin(_orbitYawRadians);
        float cosPitch = MathF.Cos(_orbitPitchRadians);
        float sinPitch = MathF.Max(0.001f, MathF.Sin(_orbitPitchRadians));
        float rotatedX = (screen.X - canvas.Center.X) / MathF.Max(0.001f, _zoom);
        float projectedY = -(screen.Y - canvas.Center.Y) / MathF.Max(0.001f, _zoom);
        float relativeY = elevationMeters * _elevationViewScale;
        float rotatedZ = (relativeY * cosPitch - projectedY) / sinPitch;
        float relativeX = cosYaw * rotatedX + sinYaw * rotatedZ;
        float relativeZ = -sinYaw * rotatedX + cosYaw * rotatedZ;

        return new Vector2(_cameraCenter.X + relativeX, _cameraCenter.Y + relativeZ);
    }

    private void MarkDirty(string status)
    {
        _dirty = true;
        SetStatus(status);
    }

    private void SetStatus(string status)
    {
        _statusText = status;
        _statusSeconds = 1.4f;
    }

    private void DrawPanel(SpriteBatch spriteBatch, Rectangle rectangle, Color fill)
    {
        FillRect(spriteBatch, rectangle, fill);
        DrawRect(spriteBatch, rectangle, new Color(210, 210, 190, 80));
    }

    private void FillRect(SpriteBatch spriteBatch, Rectangle rectangle, Color color)
    {
        if (_pixel is not null)
        {
            spriteBatch.Draw(_pixel, rectangle, color);
        }
    }

    private void DrawRect(SpriteBatch spriteBatch, Rectangle rectangle, Color color)
    {
        if (_pixel is null)
        {
            return;
        }

        spriteBatch.Draw(_pixel, new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, 1), color);
        spriteBatch.Draw(_pixel, new Rectangle(rectangle.X, rectangle.Bottom - 1, rectangle.Width, 1), color);
        spriteBatch.Draw(_pixel, new Rectangle(rectangle.X, rectangle.Y, 1, rectangle.Height), color);
        spriteBatch.Draw(_pixel, new Rectangle(rectangle.Right - 1, rectangle.Y, 1, rectangle.Height), color);
    }

    private void DrawLine(SpriteBatch spriteBatch, Vector2 a, Vector2 b, float width, Color color)
    {
        if (_pixel is null)
        {
            return;
        }

        Vector2 direction = b - a;
        float length = direction.Length();
        if (length <= 0.01f)
        {
            return;
        }

        float angle = MathF.Atan2(direction.Y, direction.X);
        spriteBatch.Draw(
            _pixel,
            a,
            null,
            color,
            angle,
            new Vector2(0f, 0.5f),
            new Vector2(length, MathF.Max(1f, width)),
            SpriteEffects.None,
            0f);
    }

    private static bool IsCtrlDown(KeyboardState keyboard)
    {
        return keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
    }

    private bool IsNewKey(KeyboardState keyboard, Keys key)
    {
        return keyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);
    }

    private static float Wrap01(float value)
    {
        value %= 1f;
        return value < 0f ? value + 1f : value;
    }

    private enum EditorViewMode
    {
        TopDown,
        Orbit3D
    }

    private enum EditorEditMode
    {
        Point,
        LeftRoad,
        RightRoad,
        LeftGrass,
        RightGrass,
        LeftWall,
        RightWall
    }

    private enum TrackBoundaryOverlay
    {
        Road,
        Grass,
        Wall
    }

    private enum TrackSide
    {
        Left,
        Right
    }

    private enum WidthHandle
    {
        None,
        LeftRoad,
        RightRoad,
        LeftGrass,
        RightGrass,
        LeftWall,
        RightWall
    }
}
