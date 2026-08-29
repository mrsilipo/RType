using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RType.Audio;
using RType.Camera;
using RType.Data;
using RType.Input;
using RType.Rendering;
using RType.Telemetry;
using RType.Ui;
using RType.Vehicle;
using RType.World;

namespace RType.Core;

public sealed class RacingGame : Game
{
    public const int InternalWidth = 1920;
    public const int InternalHeight = 1080;
    private const int TimeAttackLapCount = 3;
    private const int MainMenuOptionCount = 5;
    private const int MainMenuArcadeIndex = 0;
    private const int MainMenuEngineRoomIndex = 1;
    private const int MainMenuOptionsIndex = 3;
    private const int MainMenuQuitIndex = 4;
    private const int ArcadeMenuOptionCount = 4;
    private const int ArcadeMenuSingleRaceIndex = 0;
    private const int ArcadeMenuTimeTrialIndex = 1;
    private const int ArcadeMenuBackIndex = 3;
    private const int OptionsMenuOptionCount = 3;
    private const int OptionsMenuLayoutAIndex = 0;
    private const int OptionsMenuLayoutBIndex = 1;
    private const int OptionsMenuBackIndex = 2;
    private const int RacePauseOptionCount = 2;
    private const int RacePauseContinueIndex = 0;
    private const int RacePauseExitIndex = 1;

    private static readonly Color LetterboxColor = new(8, 9, 10);
    private static readonly Matrix UiScaleMatrix = Matrix.CreateScale(
        InternalWidth / (float)UiLayout.Width,
        InternalHeight / (float)UiLayout.Height,
        1f);
    private static readonly CarMenuOption[] CarOptions =
    [
        new("EK9 SHOWROOM STOCK", "Data/PurchaseCars/2000_Ek9_Stock.json"),
        new("EK9 K20A K-SWAP", "Data/Garage/OwnedVehicles/vehicle_0003_k20a_swap_ek9.json"),
        new("EK9 B20/VTEC CLUB", "Data/Garage/OwnedVehicles/vehicle_0004_b20vtec_ek9.json"),
        new("EK9 K24/K20 PRO", "Data/Garage/OwnedVehicles/vehicle_0005_k24_k20_pro_ek9.json")
    ];
    private static readonly ControlLayoutOption[] ControlLayoutOptions =
    [
        new("Layout A", "Data/Controls/racing_xbox360_layout_a.json"),
        new("Layout B", "Data/Controls/racing_xbox360_layout_b.json")
    ];

    private readonly GraphicsDeviceManager _graphics;
    private readonly GameLaunchOptions _launchOptions;
    private readonly SurfaceLibrary _surfaceLibrary;
    private readonly SimulationEngineParameters _simulationEngine;
    private readonly DrivabilityTuningOverlay _drivabilityTuning;
    private readonly TrackDefinition[] _trackOptions;
    private RacingInputReader _inputReader;

    private SpriteBatch? _spriteBatch;
    private RenderTarget2D? _renderTarget;
    private GeneratedTextures? _textures;
    private TrackScene? _track;
    private SceneRenderer? _sceneRenderer;
    private ClassicFourWheelVehicleSimulator? _vehicle;
    private ChaseCamera? _camera;
    private HudRenderer? _hud;
    private RearViewMirrorRenderer? _rearViewMirror;
    private MenuRenderer? _menu;
    private MenuSoundSystem? _menuSounds;
    private VehicleAudioSystem? _vehicleAudio;
    private RTypeEngineRoomScreen? _engineRoom;
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
    private int _optionsSelection;
    private int _controlLayoutSelection;
    private int _racePauseSelection;
    private bool _showTransmissionPopup;
    private bool _showDirectionPopup;
    private MouseState _previousMouse;
    private Vector2? _uiMousePosition;
    private Vector2? _previousUiMousePosition;
    private bool _mouseLeftClicked;
    private bool _mouseRightClicked;
    private bool _mouseMovedThisFrame;
    private bool _mouseStateInitialized;
    private float _controllerRumbleLeft;
    private float _controllerRumbleRight;

    public RacingGame(GameLaunchOptions launchOptions)
    {
        _launchOptions = launchOptions;
        _inputReader = new RacingInputReader(ControlSchemeLoader.Load(_launchOptions.ControlSchemePath));
        _surfaceLibrary = SurfaceLibraryLoader.Load(_launchOptions.SurfaceDefinitionPath);
        _simulationEngine = SimulationEngineDefinitionLoader.Load(_launchOptions.SimulationEngineDefinitionPath);
        _drivabilityTuning = new DrivabilityTuningOverlay(_simulationEngine, _launchOptions.SimulationEngineDefinitionPath);
        _trackOptions = TrackDefinitionFileLoader.LoadCatalog(TrackDefinitionFileLoader.DefaultTrackDirectory, TrackCatalog.All);
        _carSelection = FindCarSelection(_launchOptions.VehiclePath);
        _transmissionSelection = _launchOptions.StartInManualTransmission ? 1 : 0;
        _controlLayoutSelection = FindControlLayoutSelection(_launchOptions.ControlSchemePath);
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = InternalWidth,
            PreferredBackBufferHeight = InternalHeight,
            SynchronizeWithVerticalRetrace = true,
            PreferMultiSampling = false
        };

        Window.Title = "R Type Honda Racing";
        Window.AllowUserResizing = true;
        Exiting += (_, _) => StopControllerRumble();
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
        _rearViewMirror = new RearViewMirrorRenderer(GraphicsDevice);
        _menu = new MenuRenderer(GraphicsDevice);
        _menuSounds = new MenuSoundSystem();
        _vehicleAudio = new VehicleAudioSystem();
        _engineRoom = new RTypeEngineRoomScreen(GraphicsDevice, _launchOptions);
    }

    protected override void UnloadContent()
    {
        StopControllerRumble();
        _vehicleAudio?.Dispose();
        _engineRoom?.Dispose();
        _menuSounds?.Dispose();
        _telemetryLogger.Dispose();
        _menu?.Dispose();
        _rearViewMirror?.Dispose();
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
            case GameFlowState.EngineRoom:
                UpdateEngineRoom(gameTime);
                break;
            case GameFlowState.Options:
                UpdateOptionsMenu();
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
        IsMouseVisible = IsMenuFlowState(_flowState) || _paused;
        UpdateVehicleAudio(dt);
        UpdateControllerRumble(dt);
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

        if (_track is not null &&
            _vehicle is not null &&
            _flowState is GameFlowState.PreRace or GameFlowState.Racing or GameFlowState.Results)
        {
            RenderRearViewMirror();
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

        _rearViewMirror?.Draw(spriteBatch);
        hud.DrawTachometer(spriteBatch, _vehicle.State);
    }

    private void RenderRearViewMirror()
    {
        if (_rearViewMirror is null ||
            _sceneRenderer is null ||
            _track is null ||
            _vehicle is null ||
            _renderTarget is null)
        {
            return;
        }

        _rearViewMirror.Render(_sceneRenderer, _track, _vehicle.State);
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Viewport = new Viewport(0, 0, InternalWidth, InternalHeight);
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
            case GameFlowState.EngineRoom:
                _engineRoom?.Draw(spriteBatch);
                break;
            case GameFlowState.Options:
                menu.DrawOptions(spriteBatch, _optionsSelection, _controlLayoutSelection);
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
                    _racePauseSelection,
                    _latestControls.ControllerConnected,
                    TimeSpan.Zero,
                    _raceSession?.State,
                    _drivabilityTuning.CreateView());
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
                    _racePauseSelection,
                    _latestControls.ControllerConnected,
                    _raceElapsed,
                    _raceSession?.State,
                    _drivabilityTuning.CreateView());
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
            else if (_mainSelection == MainMenuEngineRoomIndex)
            {
                _menuSounds?.PlayDecision();
                _engineRoom?.Activate();
                _vehicleAudio?.Stop();
                _flowState = GameFlowState.EngineRoom;
            }
            else if (_mainSelection == MainMenuOptionsIndex)
            {
                _menuSounds?.PlayDecision();
                _optionsSelection = _controlLayoutSelection;
                _flowState = GameFlowState.Options;
            }
            else if (_mainSelection == 2)
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

    private void UpdateOptionsMenu()
    {
        if (MoveSelection(ref _optionsSelection, OptionsMenuOptionCount))
        {
            _menuSounds?.PlayClick();
        }

        bool mouseConfirmRequested = UpdateListMouseSelection(ref _optionsSelection, OptionsMenuOptionCount, 185);
        if (_latestControls.MenuConfirmRequested || mouseConfirmRequested)
        {
            if (_optionsSelection == OptionsMenuBackIndex)
            {
                _menuSounds?.PlayCancel();
                _flowState = GameFlowState.MainMenu;
                return;
            }

            ApplyControlLayout(_optionsSelection);
            _menuSounds?.PlayConfirm();
        }
        else if (_latestControls.MenuCancelRequested || _mouseRightClicked)
        {
            _menuSounds?.PlayCancel();
            _flowState = GameFlowState.MainMenu;
        }
    }

    private void UpdateEngineRoom(GameTime gameTime)
    {
        if (_engineRoom is null)
        {
            _flowState = GameFlowState.MainMenu;
            return;
        }

        _engineRoom.Update(gameTime);
        if (_engineRoom.ExitRequested)
        {
            _menuSounds?.PlayCancel();
            _engineRoom.ClearExitRequest();
            _flowState = GameFlowState.MainMenu;
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

        VehicleSimulationParameters parameters = LoadSelectedRaceParameters();
        _vehicle = new ClassicFourWheelVehicleSimulator(_track, _track.StartPosition, _track.StartHeadingRadians, parameters, _simulationEngine);
        _vehicle.SetManualTransmission(_transmissionSelection == 1);
        RpmPresentationSmoother.Update(_vehicle.State, 0f);
        RaceEnginePresentationBridge.ApplyAudioState(_vehicle.State, parameters, 0f);
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
        _drivabilityTuning.Update();

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
                CarOptions[_carSelection].BuildPath,
                _vehicle.State,
                _trackOptions[_trackSelection],
                _directionSelection == 1,
                false);
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
        _drivabilityTuning.Update();

        if (_latestControls.ExitRequested)
        {
            Exit();
        }

        bool pauseToggledThisFrame = false;
        if (_latestControls.PauseRequested)
        {
            _paused = !_paused;
            _racePauseSelection = RacePauseContinueIndex;
            pauseToggledThisFrame = true;
            if (_paused)
            {
                _menuSounds?.PlayConfirm();
            }
            else
            {
                _menuSounds?.PlayCancel();
            }
        }

        _latestInput = _paused ? new VehicleInput(0f, 0f, 0f) : _latestControls.Vehicle;

        if (_paused)
        {
            if (!pauseToggledThisFrame)
            {
                UpdateRacePauseMenu();
            }

            return;
        }

        if (pauseToggledThisFrame)
        {
            return;
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

        if (!_paused)
        {
            _vehicle?.Update(_latestInput, dt);
            if (_vehicle is not null)
            {
                _raceSession?.Update(_vehicle.State, elapsed);
                _raceElapsed = _raceSession?.State.RaceTime ?? _raceElapsed + elapsed;
                UpdateVehiclePresentation(dt);
                _camera?.Update(_vehicle.State, dt, _latestControls.LookBehind, _track);
                _telemetryLogger.Log(_raceElapsed, dt, _latestInput, _vehicle.State);
                if (_raceSession?.State.Finished == true)
                {
                    _telemetryLogger.Stop();
                    _flowState = GameFlowState.Results;
                }
            }
        }
    }

    private void UpdateRacePauseMenu()
    {
        if (MoveSelection(ref _racePauseSelection, RacePauseOptionCount))
        {
            _menuSounds?.PlayClick();
        }

        bool mouseConfirmRequested = UpdateRacePauseMouseSelection();
        if (_latestControls.MenuConfirmRequested || mouseConfirmRequested)
        {
            if (_racePauseSelection == RacePauseContinueIndex)
            {
                _menuSounds?.PlayConfirm();
                _paused = false;
            }
            else if (_racePauseSelection == RacePauseExitIndex)
            {
                _menuSounds?.PlayDecision();
                ExitRaceToTrackSelect();
            }
        }
        else if (_latestControls.MenuCancelRequested || _mouseRightClicked)
        {
            _menuSounds?.PlayCancel();
            _paused = false;
        }
    }

    private void ExitRaceToTrackSelect()
    {
        _paused = false;
        _racePauseSelection = RacePauseContinueIndex;
        _telemetryLogger.Stop();
        _vehicleAudio?.Stop();
        StopControllerRumble();
        _flowState = GameFlowState.TrackSelect;
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
        RaceEnginePresentationBridge.ApplyAudioState(_vehicle.State, _vehicle.Parameters, dt);
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

    private void UpdateControllerRumble(float dt)
    {
        bool active = _vehicle is not null && _flowState is GameFlowState.PreRace or GameFlowState.Racing && !_paused;
        if (!active || _vehicle is null || !GamePad.GetState(PlayerIndex.One).IsConnected)
        {
            FadeControllerRumble(dt, 16f);
            return;
        }

        VehicleState state = _vehicle.State;
        float targetLeft = MathHelper.Clamp(state.SurfaceRumbleLeft, 0f, 1f);
        float targetRight = MathHelper.Clamp(state.SurfaceRumbleRight, 0f, 1f);
        float rise = MathHelper.Clamp(1f - MathF.Exp(-32f * dt), 0f, 1f);
        float fall = MathHelper.Clamp(1f - MathF.Exp(-12f * dt), 0f, 1f);
        _controllerRumbleLeft = MathHelper.Lerp(_controllerRumbleLeft, targetLeft, targetLeft > _controllerRumbleLeft ? rise : fall);
        _controllerRumbleRight = MathHelper.Lerp(_controllerRumbleRight, targetRight, targetRight > _controllerRumbleRight ? rise : fall);
        ApplyControllerRumble();
    }

    private void FadeControllerRumble(float dt, float rate)
    {
        float blend = MathHelper.Clamp(1f - MathF.Exp(-rate * dt), 0f, 1f);
        _controllerRumbleLeft = MathHelper.Lerp(_controllerRumbleLeft, 0f, blend);
        _controllerRumbleRight = MathHelper.Lerp(_controllerRumbleRight, 0f, blend);
        if (_controllerRumbleLeft <= 0.001f && _controllerRumbleRight <= 0.001f)
        {
            StopControllerRumble();
            return;
        }

        ApplyControllerRumble();
    }

    private void ApplyControllerRumble()
    {
        GamePad.SetVibration(
            PlayerIndex.One,
            MathHelper.Clamp(_controllerRumbleLeft, 0f, 1f),
            MathHelper.Clamp(_controllerRumbleRight, 0f, 1f));
    }

    private void StopControllerRumble()
    {
        _controllerRumbleLeft = 0f;
        _controllerRumbleRight = 0f;
        GamePad.SetVibration(PlayerIndex.One, 0f, 0f);
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

    private bool UpdateRacePauseMouseSelection()
    {
        if (_uiMousePosition is not Vector2 uiPosition ||
            !HudRenderer.TryHitRacePauseItem(uiPosition, out int hoveredIndex))
        {
            return false;
        }

        ApplyMouseHover(ref _racePauseSelection, hoveredIndex);
        return _mouseLeftClicked;
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
            GameFlowState.Options or
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

    private static int FindCarSelection(string vehiclePath)
    {
        for (int i = 0; i < CarOptions.Length; i++)
        {
            if (PathsMatch(CarOptions[i].BuildPath, vehiclePath) ||
                VehiclePathMigration.IsLegacyStockEk9VehicleDefinitionPath(vehiclePath))
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
        EngineRoom,
        Options,
        CarSelect,
        TrackSelect,
        PreRace,
        Racing,
        Results
    }

    private VehicleSimulationParameters LoadSelectedRaceParameters()
    {
        if (!string.IsNullOrWhiteSpace(_launchOptions.GarageProfilePath))
        {
            GarageRuntimeVehicleSelection selected = GarageRuntimeVehicleResolver.Resolve(
                _launchOptions.GarageProfilePath,
                _launchOptions.GarageVehicleIdOrPath,
                _launchOptions.GarageSetupIdOrPath);
            return selected.Parameters;
        }

        return VehicleBuildDefinitionLoader.LoadSimulationParameters(CarOptions[_carSelection].BuildPath);
    }

    private sealed record CarMenuOption(string Label, string BuildPath);

    private void ApplyControlLayout(int layoutIndex)
    {
        if (layoutIndex < 0 || layoutIndex >= ControlLayoutOptions.Length)
        {
            return;
        }

        _controlLayoutSelection = layoutIndex;
        _optionsSelection = layoutIndex;
        _inputReader = new RacingInputReader(ControlSchemeLoader.Load(ControlLayoutOptions[layoutIndex].Path));
    }

    private static int FindControlLayoutSelection(string path)
    {
        for (int i = 0; i < ControlLayoutOptions.Length; i++)
        {
            if (PathsMatch(ControlLayoutOptions[i].Path, path))
            {
                return i;
            }
        }

        return 0;
    }

    private sealed record ControlLayoutOption(string Label, string Path);
}
