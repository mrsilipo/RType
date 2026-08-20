using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RetroRacer.Audio;
using RetroRacer.Camera;
using RetroRacer.Data;
using RetroRacer.Input;
using RetroRacer.Rendering;
using RetroRacer.Telemetry;
using RetroRacer.Ui;
using RetroRacer.Vehicle;
using RetroRacer.World;

namespace RetroRacer.Core;

public sealed class RacingGame : Game
{
    public const int InternalWidth = 1920;
    public const int InternalHeight = 1080;
    private const int TimeAttackLapCount = 3;
    private const int MainMenuOptionCount = 4;
    private const int MainMenuArcadeIndex = 0;
    private const int MainMenuQuitIndex = 3;
    private const int ArcadeMenuOptionCount = 4;
    private const int ArcadeMenuSingleRaceIndex = 0;
    private const int ArcadeMenuTimeTrialIndex = 1;
    private const int ArcadeMenuBackIndex = 3;

    private static readonly Color LetterboxColor = new(8, 9, 10);
    private static readonly Matrix UiScaleMatrix = Matrix.CreateScale(
        InternalWidth / (float)UiLayout.Width,
        InternalHeight / (float)UiLayout.Height,
        1f);
    private static readonly CarMenuOption[] CarOptions =
    [
        new("EK9 REFERENCE", "Data/Vehicles/ek9_reference_2000.json"),
        new("R33 GTR REF", "Data/Vehicles/r33_gtr_reference_1995.json")
    ];

    private readonly GraphicsDeviceManager _graphics;
    private readonly GameLaunchOptions _launchOptions;
    private readonly RacingInputReader _inputReader;
    private readonly SurfaceLibrary _surfaceLibrary;
    private readonly SimulationEngineParameters _simulationEngine;
    private readonly TrackDefinition[] _trackOptions;

    private SpriteBatch? _spriteBatch;
    private RenderTarget2D? _renderTarget;
    private GeneratedTextures? _textures;
    private TrackScene? _track;
    private SceneRenderer? _sceneRenderer;
    private SimpleVehicleSimulator? _vehicle;
    private ChaseCamera? _camera;
    private HudRenderer? _hud;
    private MenuRenderer? _menu;
    private MenuSoundSystem? _menuSounds;
    private VehicleAudioSystem? _vehicleAudio;
    private RaceSession? _raceSession;
    private readonly RaceRunTelemetryLogger _telemetryLogger = new();

    private VehicleInput _latestInput;
    private RacingControls _latestControls;
    private GameFlowState _flowState = GameFlowState.MainMenu;
    private bool _showDebug;
    private bool _paused;
    private TimeSpan _elapsedSinceStart;
    private TimeSpan _preRaceElapsed;
    private TimeSpan _raceElapsed;
    private double _fpsTimer;
    private int _framesThisSecond;
    private int _framesPerSecond;
    private int _mainSelection;
    private int _eventSelection;
    private int _carSelection;
    private int _transmissionSelection;
    private int _trackSelection;
    private int _directionSelection;
    private int _resultsSelection;
    private bool _showTransmissionPopup;
    private bool _showDirectionPopup;
    private MouseState _previousMouse;
    private Vector2? _uiMousePosition;
    private Vector2? _previousUiMousePosition;
    private bool _mouseLeftClicked;
    private bool _mouseRightClicked;
    private bool _mouseMovedThisFrame;
    private bool _mouseStateInitialized;

    public RacingGame(GameLaunchOptions launchOptions)
    {
        _launchOptions = launchOptions;
        _inputReader = new RacingInputReader(ControlSchemeLoader.Load(_launchOptions.ControlSchemePath));
        _surfaceLibrary = SurfaceLibraryLoader.Load(_launchOptions.SurfaceDefinitionPath);
        _simulationEngine = SimulationEngineDefinitionLoader.Load(_launchOptions.SimulationEngineDefinitionPath);
        _trackOptions = TrackDefinitionFileLoader.LoadCatalog(TrackDefinitionFileLoader.DefaultTrackDirectory, TrackCatalog.All);
        _carSelection = FindCarSelection(_launchOptions.VehicleDefinitionPath);
        _transmissionSelection = _launchOptions.StartInManualTransmission ? 1 : 0;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = InternalWidth,
            PreferredBackBufferHeight = InternalHeight,
            SynchronizeWithVerticalRetrace = true,
            PreferMultiSampling = false
        };

        Window.Title = "R Type Honda Racing";
        Window.AllowUserResizing = true;
        IsMouseVisible = false;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _renderTarget = new RenderTarget2D(
            GraphicsDevice,
            InternalWidth,
            InternalHeight,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);

        _textures = GeneratedTextures.Create(GraphicsDevice);
        _sceneRenderer = new SceneRenderer(GraphicsDevice, _textures);
        _camera = new ChaseCamera(InternalWidth / (float)InternalHeight);
        _hud = new HudRenderer(GraphicsDevice);
        _menu = new MenuRenderer(GraphicsDevice);
        _menuSounds = new MenuSoundSystem();
        _vehicleAudio = new VehicleAudioSystem();
    }

    protected override void UnloadContent()
    {
        _vehicleAudio?.Dispose();
        _menuSounds?.Dispose();
        _telemetryLogger.Dispose();
        _menu?.Dispose();
        _hud?.Dispose();
        _sceneRenderer?.Dispose();
        _track?.Dispose();
        _textures?.Dispose();
        _renderTarget?.Dispose();
        _spriteBatch?.Dispose();
    }

    protected override void Update(GameTime gameTime)
    {
        _latestControls = _inputReader.Read();
        UpdateMouseState();

        if (_latestControls.ToggleDebugRequested)
        {
            _showDebug = !_showDebug;
        }

        float dt = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 1f / 20f);
        switch (_flowState)
        {
            case GameFlowState.MainMenu:
                UpdateMainMenu();
                break;
            case GameFlowState.EventSelect:
                UpdateEventMenu();
                break;
            case GameFlowState.CarSelect:
                UpdateCarSelect();
                break;
            case GameFlowState.TrackSelect:
                UpdateTrackSelect();
                break;
            case GameFlowState.PreRace:
                UpdatePreRace(gameTime.ElapsedGameTime);
                break;
            case GameFlowState.Racing:
                UpdateRacing(dt, gameTime.ElapsedGameTime);
                break;
            case GameFlowState.Results:
                UpdateResults();
                break;
        }

        _menu?.Update(
            gameTime,
            _flowState == GameFlowState.MainMenu,
            _mainSelection,
            _flowState == GameFlowState.EventSelect,
            _eventSelection);
        IsMouseVisible = IsMenuFlowState(_flowState);
        UpdateVehicleAudio(dt);
        _elapsedSinceStart += gameTime.ElapsedGameTime;
        UpdateFrameCounter(gameTime);

        if (_launchOptions.AutoExitMilliseconds is int autoExitMs &&
            _elapsedSinceStart.TotalMilliseconds >= autoExitMs)
        {
            Exit();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_renderTarget is null ||
            _spriteBatch is null ||
            _sceneRenderer is null ||
            _camera is null ||
            _hud is null ||
            _menu is null)
        {
            return;
        }

        GraphicsDevice.SetRenderTarget(_renderTarget);
        GraphicsDevice.Viewport = new Viewport(0, 0, InternalWidth, InternalHeight);
        GraphicsDevice.Clear(_flowState is GameFlowState.PreRace or GameFlowState.Racing ? SceneRenderer.FogColor : LetterboxColor);

        if (_track is not null &&
            _vehicle is not null &&
            _flowState is GameFlowState.PreRace or GameFlowState.Racing or GameFlowState.Results)
        {
            _sceneRenderer.Draw(_track, _vehicle.State, _camera);
        }

        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.LinearClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        DrawNativeOverlay(_spriteBatch, _hud);
        _spriteBatch.End();

        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.LinearClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            transformMatrix: UiScaleMatrix);

        DrawOverlay(_spriteBatch, _hud, _menu);
        _spriteBatch.End();

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(LetterboxColor);

        Rectangle destination = LowResolutionScaler.GetDestinationRectangle(
            GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight,
            InternalWidth,
            InternalHeight);

        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.Opaque,
            SamplerState.LinearClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        _spriteBatch.Draw(_renderTarget, destination, Color.White);
        _spriteBatch.End();

        base.Draw(gameTime);
    }

    private void DrawNativeOverlay(SpriteBatch spriteBatch, HudRenderer hud)
    {
        if (_vehicle is null || _flowState is not (GameFlowState.PreRace or GameFlowState.Racing))
        {
            return;
        }

        hud.DrawTachometer(spriteBatch, _vehicle.State);
    }

    private void DrawOverlay(SpriteBatch spriteBatch, HudRenderer hud, MenuRenderer menu)
    {
        switch (_flowState)
        {
            case GameFlowState.MainMenu:
                menu.DrawMain(spriteBatch, _mainSelection);
                break;
            case GameFlowState.EventSelect:
                menu.DrawEvent(spriteBatch, _eventSelection);
                break;
            case GameFlowState.CarSelect:
                menu.DrawCarSelect(
                    spriteBatch,
                    [.. CarOptions.Select(option => option.Label)],
                    _carSelection,
                    _showTransmissionPopup,
                    _transmissionSelection);
                break;
            case GameFlowState.TrackSelect:
                menu.DrawTrackSelect(spriteBatch, _trackOptions, _trackSelection, _showDirectionPopup, _directionSelection);
                break;
            case GameFlowState.PreRace when _vehicle is not null && _camera is not null:
                hud.Draw(
                    spriteBatch,
                    _vehicle.State,
                    _latestInput,
                    _showDebug,
                    _framesPerSecond,
                    _camera.ModeName,
                    _paused,
                    _latestControls.ControllerConnected,
                    TimeSpan.Zero,
                    _raceSession?.State);
                menu.DrawCountdown(spriteBatch, GetCountdownText());
                break;
            case GameFlowState.Racing when _vehicle is not null && _camera is not null:
                hud.Draw(
                    spriteBatch,
                    _vehicle.State,
                    _latestInput,
                    _showDebug,
                    _framesPerSecond,
                    _camera.ModeName,
                    _paused,
                    _latestControls.ControllerConnected,
                    _raceElapsed,
                    _raceSession?.State);
                if (_raceElapsed.TotalSeconds < 0.75)
                {
                    menu.DrawCountdown(spriteBatch, "GO");
                }

                break;
            case GameFlowState.Results when _raceSession is not null:
                menu.DrawResults(spriteBatch, _raceSession.State, _resultsSelection);
                break;
        }
    }

    private void UpdateMainMenu()
    {
        if (MoveSelection(ref _mainSelection, MainMenuOptionCount))
        {
            _menuSounds?.PlayClick();
        }

        bool mouseConfirmRequested = UpdateMainMenuMouseSelection();
        bool confirmRequested = _latestControls.MenuConfirmRequested || mouseConfirmRequested;
        bool cancelRequested = _latestControls.MenuCancelRequested || _mouseRightClicked;

        if (confirmRequested)
        {
            if (_mainSelection == MainMenuArcadeIndex)
            {
                _menuSounds?.PlayDecision();
                _eventSelection = 0;
                _flowState = GameFlowState.EventSelect;
            }
            else if (_mainSelection is 1 or 2)
            {
                _menuSounds?.PlayNotAllowed();
            }
            else if (_mainSelection == MainMenuQuitIndex)
            {
                _menuSounds?.PlayDecision();
                Exit();
            }
        }
        else if (cancelRequested)
        {
            if (_mainSelection != MainMenuQuitIndex)
            {
                _mainSelection = MainMenuQuitIndex;
            }

            _menuSounds?.PlayCancel();
        }
    }

    private void UpdateEventMenu()
    {
        if (MoveSelection(ref _eventSelection, ArcadeMenuOptionCount))
        {
            _menuSounds?.PlayClick();
        }

        bool mouseConfirmRequested = UpdateArcadeMenuMouseSelection();
        if (_latestControls.MenuConfirmRequested || mouseConfirmRequested)
        {
            if (_eventSelection is ArcadeMenuSingleRaceIndex or ArcadeMenuTimeTrialIndex)
            {
                _menuSounds?.PlayDecision();
                _flowState = GameFlowState.CarSelect;
            }
            else if (_eventSelection == ArcadeMenuBackIndex)
            {
                _menuSounds?.PlayCancel();
                _flowState = GameFlowState.MainMenu;
            }
            else
            {
                _menuSounds?.PlayNotAllowed();
            }
        }
        else if (_latestControls.MenuCancelRequested || _mouseRightClicked)
        {
            _menuSounds?.PlayCancel();
            _flowState = GameFlowState.MainMenu;
        }
    }

    private void UpdateCarSelect()
    {
        if (_showTransmissionPopup)
        {
            if (MoveSelection(ref _transmissionSelection, 2))
            {
                _menuSounds?.PlayClick();
            }

            bool mouseConfirmRequested = UpdatePopupMouseSelection(ref _transmissionSelection, 2);
            if (_latestControls.MenuConfirmRequested || mouseConfirmRequested)
            {
                _menuSounds?.PlayDecision();
                _showTransmissionPopup = false;
                _flowState = GameFlowState.TrackSelect;
            }
            else if (_latestControls.MenuCancelRequested || _mouseRightClicked)
            {
                _menuSounds?.PlayCancel();
                _showTransmissionPopup = false;
            }

            return;
        }

        if (MoveSelection(ref _carSelection, CarOptions.Length))
        {
            _menuSounds?.PlayClick();
        }

        bool carMouseConfirmRequested = UpdateListMouseSelection(ref _carSelection, CarOptions.Length, 118);
        if (_latestControls.MenuConfirmRequested || carMouseConfirmRequested)
        {
            _menuSounds?.PlayConfirm();
            _showTransmissionPopup = true;
        }
        else if (_latestControls.MenuCancelRequested || _mouseRightClicked)
        {
            _menuSounds?.PlayCancel();
            _flowState = GameFlowState.EventSelect;
        }
    }

    private void UpdateTrackSelect()
    {
        if (_showDirectionPopup)
        {
            if (MoveSelection(ref _directionSelection, 2))
            {
                _menuSounds?.PlayClick();
            }

            bool mouseConfirmRequested = UpdatePopupMouseSelection(ref _directionSelection, 2);
            if (_latestControls.MenuConfirmRequested || mouseConfirmRequested)
            {
                _menuSounds?.PlayDecision();
                _showDirectionPopup = false;
                BeginPreRace();
            }
            else if (_latestControls.MenuCancelRequested || _mouseRightClicked)
            {
                _menuSounds?.PlayCancel();
                _showDirectionPopup = false;
            }

            return;
        }

        if (MoveSelection(ref _trackSelection, _trackOptions.Length))
        {
            _menuSounds?.PlayClick();
        }

        bool trackMouseConfirmRequested = IsMouseClickedOnListItem(1, 185, out _);
        if (_latestControls.MenuConfirmRequested || trackMouseConfirmRequested)
        {
            _menuSounds?.PlayConfirm();
            _showDirectionPopup = true;
        }
        else if (_latestControls.MenuCancelRequested || _mouseRightClicked)
        {
            _menuSounds?.PlayCancel();
            _flowState = GameFlowState.CarSelect;
        }
    }

    private void BeginPreRace()
    {
        if (_textures is null || _camera is null)
        {
            return;
        }

        _track?.Dispose();
        TrackDefinition trackDefinition = _trackOptions[_trackSelection];
        bool reverse = _directionSelection == 1;
        _track = TrackScene.Create(GraphicsDevice, _textures, trackDefinition, reverse, _surfaceLibrary);

        VehicleSimulationParameters parameters = VehicleDefinitionLoader.LoadSimulationParameters(CarOptions[_carSelection].VehiclePath);
        _vehicle = new SimpleVehicleSimulator(_track, _track.StartPosition, _track.StartHeadingRadians, parameters, _simulationEngine);
        _vehicle.SetManualTransmission(_transmissionSelection == 1);
        RpmPresentationSmoother.Update(_vehicle.State, 0f);
        _vehicleAudio?.SetVehicle(parameters.Audio);
        _camera.SetMode(CameraMode.Chase1, _vehicle.State, reset: true);
        _paused = false;
        _latestInput = new VehicleInput(0f, 0f, 0f);
        _raceSession = null;
        _resultsSelection = 0;
        _preRaceElapsed = TimeSpan.Zero;
        _raceElapsed = TimeSpan.Zero;
        _flowState = GameFlowState.PreRace;
        UpdatePreRaceCamera();
    }

    private void UpdatePreRace(TimeSpan elapsed)
    {
        if (_latestControls.MenuCancelRequested)
        {
            _flowState = GameFlowState.TrackSelect;
            return;
        }

        float dt = Math.Min((float)elapsed.TotalSeconds, 1f / 20f);
        _latestInput = _latestControls.Vehicle;
        _vehicle?.UpdateRaceStartHold(_latestInput, dt);
        UpdateVehiclePresentation(dt);
        _preRaceElapsed += elapsed;
        UpdatePreRaceCamera();

        if (_preRaceElapsed.TotalSeconds >= 3.0)
        {
            if (_vehicle is null || _track is null)
            {
                return;
            }

            _flowState = GameFlowState.Racing;
            _raceElapsed = TimeSpan.Zero;
            _raceSession = new RaceSession(_track, TimeAttackLapCount);
            _camera?.SetMode(CameraMode.InCar, _vehicle.State, reset: false);
            _telemetryLogger.Start(
                CarOptions[_carSelection].VehiclePath,
                _vehicle.State,
                _trackOptions[_trackSelection],
                _directionSelection == 1);
        }
    }

    private void UpdatePreRaceCamera()
    {
        if (_vehicle is null || _camera is null)
        {
            return;
        }

        float seconds = (float)_preRaceElapsed.TotalSeconds;
        float t = SmoothStep01(seconds / 3.0f);
        Vector3 start = _vehicle.State.Position + _vehicle.State.Forward * 6.8f + _vehicle.State.Right * 3.6f + Vector3.Up * 2.35f;
        Vector3 middle = _vehicle.State.Position - _vehicle.State.Forward * 3.6f + _vehicle.State.Right * 2.4f + Vector3.Up * 2.25f;
        (Vector3 end, Vector3 inCarTarget) = _camera.GetInCarPose(_vehicle.State);
        Vector3 position = QuadraticBezier(start, middle, end, t);
        Vector3 carTarget = _vehicle.State.Position + Vector3.Up * 0.78f + _vehicle.State.Forward * 0.45f;
        Vector3 target = Vector3.Lerp(carTarget, inCarTarget, SmoothStep01((seconds - 1.35f) / 1.65f));
        _camera.SetLookAt(position, target);
        if (seconds >= 2.85f)
        {
            _camera.SetMode(CameraMode.InCar, _vehicle.State, reset: false);
        }
    }

    private void UpdateRacing(float dt, TimeSpan elapsed)
    {
        if (_latestControls.ExitRequested)
        {
            Exit();
        }

        if (_latestControls.PauseRequested)
        {
            _paused = !_paused;
        }

        if (_vehicle is not null)
        {
            if (_latestControls.ToggleTransmissionModeRequested)
            {
                _vehicle.ToggleTransmissionMode();
            }

            if (_latestControls.ToggleViewRequested && _camera is not null)
            {
                _camera.CycleMode(_vehicle.State);
            }
        }

        _latestInput = _paused ? new VehicleInput(0f, 0f, 0f) : _latestControls.Vehicle;

        if (!_paused)
        {
            _vehicle?.Update(_latestInput, dt);
            if (_vehicle is not null)
            {
                _raceSession?.Update(_vehicle.State, elapsed);
                _raceElapsed = _raceSession?.State.RaceTime ?? _raceElapsed + elapsed;
                UpdateVehiclePresentation(dt);
                _camera?.Update(_vehicle.State, dt, _latestControls.LookBehind);
                _telemetryLogger.Log(_raceElapsed, dt, _latestInput, _vehicle.State);
                if (_raceSession?.State.Finished == true)
                {
                    _telemetryLogger.Stop();
                    _flowState = GameFlowState.Results;
                }
            }
        }
    }

    private void UpdateResults()
    {
        if (MoveSelection(ref _resultsSelection, 2))
        {
            _menuSounds?.PlayClick();
        }

        bool mouseConfirmRequested = UpdateListMouseSelection(ref _resultsSelection, 2, 175);
        if (_latestControls.MenuConfirmRequested || mouseConfirmRequested)
        {
            _menuSounds?.PlayDecision();
            if (_resultsSelection == 0)
            {
                BeginPreRace();
            }
            else
            {
                _flowState = GameFlowState.TrackSelect;
            }
        }
        else if (_latestControls.MenuCancelRequested || _mouseRightClicked)
        {
            _menuSounds?.PlayCancel();
            _flowState = GameFlowState.TrackSelect;
        }
    }

    private void UpdateVehiclePresentation(float dt)
    {
        if (_vehicle is null)
        {
            return;
        }

        RpmPresentationSmoother.Update(_vehicle.State, dt);
    }

    private void UpdateVehicleAudio(float dt)
    {
        if (_vehicleAudio is null)
        {
            return;
        }

        bool active = _vehicle is not null && _flowState is GameFlowState.PreRace or GameFlowState.Racing;
        if (!active || _vehicle is null)
        {
            _vehicleAudio.Stop();
            return;
        }

        _vehicleAudio.Update(_vehicle.State, _camera?.Mode ?? CameraMode.Chase1, active, _paused, dt);
    }

    private void UpdateMouseState()
    {
        MouseState mouse = Mouse.GetState();
        _previousUiMousePosition = _uiMousePosition;
        _uiMousePosition = GetUiMousePosition(mouse);
        _mouseLeftClicked = false;
        _mouseRightClicked = false;
        _mouseMovedThisFrame = false;

        if (_mouseStateInitialized)
        {
            _mouseLeftClicked = _uiMousePosition.HasValue &&
                mouse.LeftButton == ButtonState.Pressed &&
                _previousMouse.LeftButton == ButtonState.Released;
            _mouseRightClicked = _uiMousePosition.HasValue &&
                mouse.RightButton == ButtonState.Pressed &&
                _previousMouse.RightButton == ButtonState.Released;
            _mouseMovedThisFrame = _uiMousePosition.HasValue &&
                _previousUiMousePosition.HasValue &&
                Vector2.DistanceSquared(_uiMousePosition.Value, _previousUiMousePosition.Value) >= 1f;
        }
        else
        {
            _mouseStateInitialized = true;
        }

        _previousMouse = mouse;
    }

    private Vector2? GetUiMousePosition(MouseState mouse)
    {
        Rectangle destination = LowResolutionScaler.GetDestinationRectangle(
            GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight,
            InternalWidth,
            InternalHeight);
        if (destination.Width <= 0 ||
            destination.Height <= 0 ||
            !destination.Contains(mouse.X, mouse.Y))
        {
            return null;
        }

        float x = (mouse.X - destination.X) * (InternalWidth / (float)destination.Width);
        float y = (mouse.Y - destination.Y) * (InternalHeight / (float)destination.Height);
        return new Vector2(x, y);
    }

    private bool UpdateMainMenuMouseSelection()
    {
        if (_uiMousePosition is not Vector2 uiPosition ||
            !MenuRenderer.TryHitMainMenuItem(uiPosition, out int hoveredIndex))
        {
            return false;
        }

        ApplyMouseHover(ref _mainSelection, hoveredIndex);
        return _mouseLeftClicked;
    }

    private bool UpdateArcadeMenuMouseSelection()
    {
        if (_uiMousePosition is not Vector2 uiPosition ||
            !MenuRenderer.TryHitArcadeMenuItem(uiPosition, out int hoveredIndex))
        {
            return false;
        }

        ApplyMouseHover(ref _eventSelection, hoveredIndex);
        return _mouseLeftClicked;
    }

    private bool UpdateListMouseSelection(ref int selection, int itemCount, int startY)
    {
        if (_uiMousePosition is not Vector2 uiPosition ||
            !MenuRenderer.TryHitListItem(uiPosition, itemCount, startY, out int hoveredIndex))
        {
            return false;
        }

        ApplyMouseHover(ref selection, hoveredIndex);
        return _mouseLeftClicked;
    }

    private bool UpdatePopupMouseSelection(ref int selection, int itemCount)
    {
        if (_uiMousePosition is not Vector2 uiPosition ||
            !MenuRenderer.TryHitPopupItem(uiPosition, itemCount, out int hoveredIndex))
        {
            return false;
        }

        ApplyMouseHover(ref selection, hoveredIndex);
        return _mouseLeftClicked;
    }

    private bool IsMouseClickedOnListItem(int itemCount, int startY, out int index)
    {
        index = -1;
        return _mouseLeftClicked &&
            _uiMousePosition is Vector2 uiPosition &&
            MenuRenderer.TryHitListItem(uiPosition, itemCount, startY, out index);
    }

    private void ApplyMouseHover(ref int selection, int hoveredIndex)
    {
        if (hoveredIndex == selection)
        {
            return;
        }

        selection = hoveredIndex;
        if (_mouseMovedThisFrame)
        {
            _menuSounds?.PlayClick();
        }
    }

    private bool MoveSelection(ref int selection, int count)
    {
        if (count <= 1)
        {
            selection = 0;
            return false;
        }

        int direction = _latestControls.MenuVertical != 0
            ? _latestControls.MenuVertical
            : _latestControls.MenuHorizontal;
        if (direction == 0)
        {
            return false;
        }

        int previousSelection = selection;
        selection = (selection + direction + count) % count;
        return selection != previousSelection;
    }

    private static bool IsMenuFlowState(GameFlowState state)
    {
        return state is GameFlowState.MainMenu or
            GameFlowState.EventSelect or
            GameFlowState.CarSelect or
            GameFlowState.TrackSelect or
            GameFlowState.Results;
    }

    private string GetCountdownText()
    {
        double seconds = _preRaceElapsed.TotalSeconds;
        if (seconds < 1.0)
        {
            return "3";
        }

        if (seconds < 2.0)
        {
            return "2";
        }

        return "1";
    }

    private static float SmoothStep01(float value)
    {
        float t = MathHelper.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Vector3 QuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
    {
        float inverse = 1f - MathHelper.Clamp(t, 0f, 1f);
        return start * (inverse * inverse) + control * (2f * inverse * t) + end * (t * t);
    }

    private static int FindCarSelection(string vehicleDefinitionPath)
    {
        for (int i = 0; i < CarOptions.Length; i++)
        {
            if (PathsMatch(CarOptions[i].VehiclePath, vehicleDefinitionPath))
            {
                return i;
            }
        }

        return 0;
    }

    private static bool PathsMatch(string left, string right)
    {
        return NormalizePath(left).Equals(NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    private void UpdateFrameCounter(GameTime gameTime)
    {
        _framesThisSecond++;
        _fpsTimer += gameTime.ElapsedGameTime.TotalSeconds;

        if (_fpsTimer >= 1.0)
        {
            _framesPerSecond = _framesThisSecond;
            _framesThisSecond = 0;
            _fpsTimer -= 1.0;
        }
    }

    private enum GameFlowState
    {
        MainMenu,
        EventSelect,
        CarSelect,
        TrackSelect,
        PreRace,
        Racing,
        Results
    }

    private sealed record CarMenuOption(string Label, string VehiclePath);
}
