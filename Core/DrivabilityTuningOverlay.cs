using System.Globalization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RType.Data;
using RType.Vehicle;

namespace RType.Core;

public sealed class DrivabilityTuningOverlay
{
    private const int RowsPerPage = 10;
    private static readonly string SaveDirectory = Path.Combine("Data", "Simulation", "TuningSaves");

    private readonly SimulationEngineParameters _parameters;
    private readonly string _defaultsPath;
    private readonly List<TunableVariable> _variables;
    private readonly List<string> _messages = [];
    private KeyboardState _previousKeyboard;
    private bool _visible;
    private bool _loadListVisible;
    private int _selectedIndex;
    private int _selectedSaveIndex;
    private string[] _saveFiles = [];

    public DrivabilityTuningOverlay(SimulationEngineParameters parameters, string defaultsPath)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _defaultsPath = defaultsPath ?? throw new ArgumentNullException(nameof(defaultsPath));
        _variables = CreateVariables(_parameters);
    }

    public bool Visible => _visible;

    public DrivabilityTuningOverlayView CreateView()
    {
        int safeSelected = Math.Clamp(_selectedIndex, 0, Math.Max(0, _variables.Count - 1));
        int pageStart = safeSelected / RowsPerPage * RowsPerPage;
        int pageEnd = Math.Min(_variables.Count, pageStart + RowsPerPage);
        List<DrivabilityTuningRow> rows = new(pageEnd - pageStart);
        for (int i = pageStart; i < pageEnd; i++)
        {
            TunableVariable variable = _variables[i];
            rows.Add(new DrivabilityTuningRow(
                i,
                variable.Group,
                variable.DisplayName,
                variable.Path,
                variable.FormatValue(),
                variable.FormatLimits(),
                variable.HighImpact,
                i == safeSelected));
        }

        TunableVariable selected = _variables[safeSelected];
        return new DrivabilityTuningOverlayView(
            _visible,
            _loadListVisible,
            pageStart / RowsPerPage + 1,
            Math.Max(1, (int)MathF.Ceiling(_variables.Count / (float)RowsPerPage)),
            rows,
            selected.DisplayName,
            selected.Path,
            selected.Explanation,
            selected.HigherText,
            selected.LowerText,
            IsExplanationHeld(),
            _saveFiles.Select((file, index) => new DrivabilityTuningSaveRow(
                index,
                Path.GetFileNameWithoutExtension(file),
                index == _selectedSaveIndex)).ToArray(),
            _messages.TakeLast(3).ToArray());
    }

    public void Update()
    {
        KeyboardState keyboard = Keyboard.GetState();

        if (Pressed(keyboard, Keys.Tab))
        {
            _visible = !_visible;
            _loadListVisible = false;
        }

        if (_visible)
        {
            if (Pressed(keyboard, Keys.Escape))
            {
                if (_loadListVisible)
                {
                    _loadListVisible = false;
                }
                else
                {
                    _visible = false;
                }
            }
            else if (_loadListVisible)
            {
                UpdateLoadList(keyboard);
            }
            else
            {
                UpdateVariableList(keyboard);
            }
        }

        _previousKeyboard = keyboard;
    }

    private void UpdateVariableList(KeyboardState keyboard)
    {
        int vertical = ReadVerticalEdge(keyboard);
        if (vertical != 0)
        {
            _selectedIndex = Wrap(_selectedIndex + vertical, _variables.Count);
        }

        int horizontal = ReadHorizontalEdge(keyboard);
        if (horizontal != 0)
        {
            _variables[_selectedIndex].Adjust(horizontal);
            AddMessage($"{_variables[_selectedIndex].DisplayName} = {_variables[_selectedIndex].FormatValue()}");
        }

        if (Pressed(keyboard, Keys.OemTilde))
        {
            ReloadDefaults();
        }

        if (Pressed(keyboard, Keys.D1))
        {
            SaveCurrentValues();
        }

        if (Pressed(keyboard, Keys.D2))
        {
            RefreshSaveList();
            _loadListVisible = true;
            _selectedSaveIndex = Math.Clamp(_selectedSaveIndex, 0, Math.Max(0, _saveFiles.Length - 1));
            AddMessage(_saveFiles.Length == 0 ? "No saved tuning files found." : "Select saved tuning file.");
        }
    }

    private void UpdateLoadList(KeyboardState keyboard)
    {
        if (_saveFiles.Length == 0)
        {
            if (Pressed(keyboard, Keys.D2) || Pressed(keyboard, Keys.Enter))
            {
                _loadListVisible = false;
            }

            return;
        }

        int vertical = ReadVerticalEdge(keyboard);
        if (vertical != 0)
        {
            _selectedSaveIndex = Wrap(_selectedSaveIndex + vertical, _saveFiles.Length);
        }

        if (Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.D2))
        {
            LoadValues(_saveFiles[_selectedSaveIndex]);
            _loadListVisible = false;
        }
    }

    private void ReloadDefaults()
    {
        SimulationEngineParameters defaults = SimulationEngineDefinitionLoader.Load(_defaultsPath);
        foreach (TunableVariable variable in _variables)
        {
            variable.CopyFrom(defaults);
        }

        AddMessage("Defaults reloaded.");
    }

    private void SaveCurrentValues()
    {
        Directory.CreateDirectory(SaveDirectory);
        string fileName = $"drivability_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string path = Path.Combine(SaveDirectory, fileName);
        Dictionary<string, object> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (TunableVariable variable in _variables)
        {
            values[variable.Path] = variable.ReadObject();
        }

        var payload = new
        {
            schemaVersion = 1,
            savedAtLocal = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
            source = _defaultsPath,
            values
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        RefreshSaveList();
        AddMessage($"Saved {fileName}.");
    }

    private void LoadValues(string path)
    {
        if (!File.Exists(path))
        {
            AddMessage("Saved tuning file missing.");
            RefreshSaveList();
            return;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("values", out JsonElement values) ||
            values.ValueKind != JsonValueKind.Object)
        {
            AddMessage("Saved tuning file has no values.");
            return;
        }

        int applied = 0;
        foreach (TunableVariable variable in _variables)
        {
            if (!values.TryGetProperty(variable.Path, out JsonElement value))
            {
                continue;
            }

            if (variable.TrySetFromJson(value))
            {
                applied++;
            }
        }

        AddMessage($"Loaded {Path.GetFileNameWithoutExtension(path)} ({applied} values).");
    }

    private void RefreshSaveList()
    {
        if (!Directory.Exists(SaveDirectory))
        {
            _saveFiles = [];
            _selectedSaveIndex = 0;
            return;
        }

        _saveFiles = Directory.GetFiles(SaveDirectory, "*.json")
            .OrderByDescending(File.GetLastWriteTime)
            .Take(10)
            .ToArray();
        _selectedSaveIndex = Math.Clamp(_selectedSaveIndex, 0, Math.Max(0, _saveFiles.Length - 1));
    }

    private bool IsExplanationHeld()
    {
        KeyboardState keyboard = Keyboard.GetState();
        return keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
    }

    private int ReadVerticalEdge(KeyboardState keyboard)
    {
        bool up = Pressed(keyboard, Keys.Up);
        bool down = Pressed(keyboard, Keys.Down);
        if (up == down)
        {
            return 0;
        }

        return up ? -1 : 1;
    }

    private int ReadHorizontalEdge(KeyboardState keyboard)
    {
        bool left = Pressed(keyboard, Keys.Left);
        bool right = Pressed(keyboard, Keys.Right);
        if (left == right)
        {
            return 0;
        }

        return left ? -1 : 1;
    }

    private bool Pressed(KeyboardState keyboard, Keys key)
    {
        return keyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);
    }

    private static int Wrap(int value, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        int result = value % count;
        return result < 0 ? result + count : result;
    }

    private void AddMessage(string message)
    {
        _messages.Add(message);
        if (_messages.Count > 12)
        {
            _messages.RemoveAt(0);
        }
    }

    private static List<TunableVariable> CreateVariables(SimulationEngineParameters parameters)
    {
        return
        [
            new FloatVariable(parameters, "Steering Angle Table", "0 km/h Angle", "classicFourWheel.steering.zeroKmhAngleDegrees", true, 4f, 60f, 0.5f, p => p.ClassicFourWheel.Steering.ZeroKmhAngleDegrees, (p, v) => p.ClassicFourWheel.Steering.ZeroKmhAngleDegrees = v, "Maximum front wheel angle at walking speed.", "Higher makes parking-speed turns and U-turns sharper.", "Lower makes low-speed steering calmer."),
            new FloatVariable(parameters, "Steering Angle Table", "60 km/h Angle", "classicFourWheel.steering.sixtyKmhAngleDegrees", true, 4f, 55f, 0.5f, p => p.ClassicFourWheel.Steering.SixtyKmhAngleDegrees, (p, v) => p.ClassicFourWheel.Steering.SixtyKmhAngleDegrees = v, "Maximum front wheel angle at 60 km/h.", "Higher gives stronger medium-speed turn-in.", "Lower reduces medium-speed bite."),
            new FloatVariable(parameters, "Steering Angle Table", "120 km/h Angle", "classicFourWheel.steering.oneTwentyKmhAngleDegrees", true, 3f, 45f, 0.5f, p => p.ClassicFourWheel.Steering.OneTwentyKmhAngleDegrees, (p, v) => p.ClassicFourWheel.Steering.OneTwentyKmhAngleDegrees = v, "Maximum front wheel angle at 120 km/h.", "Higher gives more high-speed authority.", "Lower reduces high-speed tyre overload."),
            new FloatVariable(parameters, "Steering Angle Table", "200 km/h Angle", "classicFourWheel.steering.twoHundredKmhAngleDegrees", true, 2f, 35f, 0.5f, p => p.ClassicFourWheel.Steering.TwoHundredKmhAngleDegrees, (p, v) => p.ClassicFourWheel.Steering.TwoHundredKmhAngleDegrees = v, "Maximum front wheel angle at 200 km/h.", "Higher lets the car rotate more at very high speed.", "Lower makes fast straights steadier."),
            new FloatVariable(parameters, "Steering Response", "Steer Speed", "classicFourWheel.steering.steerSpeedDegreesPerSecond", true, 30f, 720f, 5f, p => p.ClassicFourWheel.Steering.SteerSpeedDegreesPerSecond, (p, v) => p.ClassicFourWheel.Steering.SteerSpeedDegreesPerSecond = v, "How fast the road wheels move toward player input.", "Higher makes steering snap to lock faster.", "Lower makes steering smoother and less abrupt."),
            new FloatVariable(parameters, "Steering Response", "Return Speed", "classicFourWheel.steering.returnSpeedDegreesPerSecond", true, 30f, 900f, 5f, p => p.ClassicFourWheel.Steering.ReturnSpeedDegreesPerSecond, (p, v) => p.ClassicFourWheel.Steering.ReturnSpeedDegreesPerSecond = v, "How fast the road wheels return toward center.", "Higher makes the car straighten quicker after input release.", "Lower lets steering unwind more slowly."),
            new FloatVariable(parameters, "Front Tyres", "Front Cornering Stiffness", "classicFourWheel.frontTyres.corneringStiffness", true, 1f, 20f, 0.1f, p => p.ClassicFourWheel.FrontTyres.CorneringStiffness, (p, v) => p.ClassicFourWheel.FrontTyres.CorneringStiffness = v, "Shape of front lateral force rise before peak slip.", "Higher gives sharper front bite.", "Lower makes front grip build more gently."),
            new FloatVariable(parameters, "Front Tyres", "Front Peak Slip", "classicFourWheel.frontTyres.peakSlipAngleDegrees", true, 2f, 18f, 0.25f, p => p.ClassicFourWheel.FrontTyres.PeakSlipAngleDegrees, (p, v) => p.ClassicFourWheel.FrontTyres.PeakSlipAngleDegrees = v, "Slip angle where front tyres reach peak grip.", "Higher tolerates more steering angle before saturation.", "Lower peaks earlier and understeers sooner."),
            new FloatVariable(parameters, "Front Tyres", "Front Falloff Slip", "classicFourWheel.frontTyres.falloffSlipAngleDegrees", false, 4f, 45f, 0.5f, p => p.ClassicFourWheel.FrontTyres.FalloffSlipAngleDegrees, (p, v) => p.ClassicFourWheel.FrontTyres.FalloffSlipAngleDegrees = v, "Slip angle where overloaded front tyres settle onto sliding grip.", "Higher makes the slide transition broader.", "Lower makes overload happen sooner."),
            new FloatVariable(parameters, "Front Tyres", "Front Max Grip", "classicFourWheel.frontTyres.maxGrip", true, 0.3f, 2.5f, 0.02f, p => p.ClassicFourWheel.FrontTyres.MaxGrip, (p, v) => p.ClassicFourWheel.FrontTyres.MaxGrip = v, "Front axle peak grip multiplier against static front load.", "Higher increases front turning and braking capacity.", "Lower makes front understeer arrive sooner."),
            new FloatVariable(parameters, "Front Tyres", "Front Sliding Grip", "classicFourWheel.frontTyres.slidingGrip", true, 0.1f, 1.2f, 0.02f, p => p.ClassicFourWheel.FrontTyres.SlidingGrip, (p, v) => p.ClassicFourWheel.FrontTyres.SlidingGrip = v, "Front grip retained after the tyre is past falloff slip.", "Higher keeps the nose biting while sliding.", "Lower makes overloaded fronts feel more icy."),
            new FloatVariable(parameters, "Rear Tyres", "Rear Cornering Stiffness", "classicFourWheel.rearTyres.corneringStiffness", true, 1f, 20f, 0.1f, p => p.ClassicFourWheel.RearTyres.CorneringStiffness, (p, v) => p.ClassicFourWheel.RearTyres.CorneringStiffness = v, "Shape of rear lateral force rise before peak slip.", "Higher makes the rear resist rotation sooner.", "Lower lets the rear rotate more easily."),
            new FloatVariable(parameters, "Rear Tyres", "Rear Peak Slip", "classicFourWheel.rearTyres.peakSlipAngleDegrees", true, 2f, 20f, 0.25f, p => p.ClassicFourWheel.RearTyres.PeakSlipAngleDegrees, (p, v) => p.ClassicFourWheel.RearTyres.PeakSlipAngleDegrees = v, "Slip angle where rear tyres reach peak grip.", "Higher keeps the rear stable over more slip.", "Lower makes the rear break away earlier."),
            new FloatVariable(parameters, "Rear Tyres", "Rear Falloff Slip", "classicFourWheel.rearTyres.falloffSlipAngleDegrees", false, 4f, 50f, 0.5f, p => p.ClassicFourWheel.RearTyres.FalloffSlipAngleDegrees, (p, v) => p.ClassicFourWheel.RearTyres.FalloffSlipAngleDegrees = v, "Slip angle where overloaded rear tyres settle onto sliding grip.", "Higher makes rear slides progressive.", "Lower makes rear grip fall off sooner."),
            new FloatVariable(parameters, "Rear Tyres", "Rear Max Grip", "classicFourWheel.rearTyres.maxGrip", true, 0.3f, 2.5f, 0.02f, p => p.ClassicFourWheel.RearTyres.MaxGrip, (p, v) => p.ClassicFourWheel.RearTyres.MaxGrip = v, "Rear axle peak grip multiplier against static rear load.", "Higher stabilizes the tail.", "Lower gives more rotation and oversteer."),
            new FloatVariable(parameters, "Rear Tyres", "Rear Sliding Grip", "classicFourWheel.rearTyres.slidingGrip", true, 0.1f, 1.2f, 0.02f, p => p.ClassicFourWheel.RearTyres.SlidingGrip, (p, v) => p.ClassicFourWheel.RearTyres.SlidingGrip = v, "Rear grip retained after the tyre is past falloff slip.", "Higher keeps slides controllable and stable.", "Lower makes the rear slide wider."),
            new FloatVariable(parameters, "Grip Budget", "Grip Ellipse Exponent", "classicFourWheel.gripBudget.combinedGripExponent", true, 1.2f, 4f, 0.05f, p => p.ClassicFourWheel.GripBudget.CombinedGripExponent, (p, v) => p.ClassicFourWheel.GripBudget.CombinedGripExponent = v, "How longitudinal and lateral force compete for each axle's grip budget.", "Higher is more square, allowing more combined throttle and steering.", "Lower is more circular, making throttle/brake consume cornering grip faster."),
            new FloatVariable(parameters, "Yaw", "Yaw Inertia Scale", "classicFourWheel.yaw.inertiaScale", true, 0.2f, 4f, 0.05f, p => p.ClassicFourWheel.Yaw.InertiaScale, (p, v) => p.ClassicFourWheel.Yaw.InertiaScale = v, "Multiplier on vehicle yaw inertia.", "Higher makes the car resist rotation more.", "Lower makes the car rotate faster."),
            new FloatVariable(parameters, "Yaw", "Yaw Damping", "classicFourWheel.yaw.damping", false, 0f, 2f, 0.02f, p => p.ClassicFourWheel.Yaw.Damping, (p, v) => p.ClassicFourWheel.Yaw.Damping = v, "Small rotational damping after tyre forces have acted.", "Higher calms yaw oscillation.", "Lower leaves yaw recovery almost entirely to tyres."),
            new FloatVariable(parameters, "Yaw", "Lateral Velocity Damping", "classicFourWheel.yaw.lateralVelocityDamping", false, 0f, 1f, 0.01f, p => p.ClassicFourWheel.Yaw.LateralVelocityDamping, (p, v) => p.ClassicFourWheel.Yaw.LateralVelocityDamping = v, "Numerical chassis lateral damping. Keep at zero unless tyres need help.", "Higher artificially kills sideways motion.", "Lower keeps lateral velocity controlled by tyres."),
            new FloatVariable(parameters, "Low Speed", "Slip Speed Floor", "classicFourWheel.lowSpeed.slipSpeedFloorMetersPerSecond", true, 0.5f, 8f, 0.1f, p => p.ClassicFourWheel.LowSpeed.SlipSpeedFloorMetersPerSecond, (p, v) => p.ClassicFourWheel.LowSpeed.SlipSpeedFloorMetersPerSecond = v, "Effective forward speed floor used only for stable slip-angle math near zero speed.", "Higher makes crawling slip calculations calmer.", "Lower makes near-stop steering more reactive but less stable."),
            new FloatVariable(parameters, "Resistance", "Rolling Resistance", "classicFourWheel.resistance.rollingResistanceMultiplier", false, 0f, 4f, 0.05f, p => p.ClassicFourWheel.Resistance.RollingResistanceMultiplier, (p, v) => p.ClassicFourWheel.Resistance.RollingResistanceMultiplier = v, "Multiplier on rolling resistance.", "Higher makes coasting bleed speed faster.", "Lower lets the car coast longer."),
            new FloatVariable(parameters, "Resistance", "Aero Drag", "classicFourWheel.resistance.aeroDragMultiplier", false, 0f, 4f, 0.05f, p => p.ClassicFourWheel.Resistance.AeroDragMultiplier, (p, v) => p.ClassicFourWheel.Resistance.AeroDragMultiplier = v, "Multiplier on straight-line aerodynamic drag.", "Higher reduces high-speed acceleration and top speed.", "Lower gives more speed on straights.")
        ];
    }

    private abstract class TunableVariable
    {
        protected TunableVariable(
            string group,
            string displayName,
            string path,
            bool highImpact,
            string explanation,
            string higherText,
            string lowerText)
        {
            Group = group;
            DisplayName = displayName;
            Path = path;
            HighImpact = highImpact;
            Explanation = explanation;
            HigherText = higherText;
            LowerText = lowerText;
        }

        public string Group { get; }

        public string DisplayName { get; }

        public string Path { get; }

        public bool HighImpact { get; }

        public string Explanation { get; }

        public string HigherText { get; }

        public string LowerText { get; }

        public abstract string FormatValue();

        public abstract string FormatLimits();

        public abstract object ReadObject();

        public abstract void Adjust(int direction);

        public abstract void CopyFrom(SimulationEngineParameters source);

        public abstract bool TrySetFromJson(JsonElement element);
    }

    private sealed class FloatVariable : TunableVariable
    {
        private readonly float _minimum;
        private readonly float _maximum;
        private readonly float _step;
        private readonly SimulationEngineParameters _target;
        private readonly Func<SimulationEngineParameters, float> _getter;
        private readonly Action<SimulationEngineParameters, float> _setter;

        public FloatVariable(
            SimulationEngineParameters parameters,
            string group,
            string displayName,
            string path,
            bool highImpact,
            float minimum,
            float maximum,
            float step,
            Func<SimulationEngineParameters, float> getter,
            Action<SimulationEngineParameters, float> setter,
            string explanation,
            string higherText,
            string lowerText)
            : base(group, displayName, path, highImpact, explanation, higherText, lowerText)
        {
            _target = parameters;
            _minimum = minimum;
            _maximum = maximum;
            _step = step;
            _getter = getter;
            _setter = setter;
        }

        public override string FormatValue()
        {
            return _getter(_target).ToString(_step < 0.01f ? "0.000" : _step < 0.1f ? "0.00" : "0.0", CultureInfo.InvariantCulture);
        }

        public override string FormatLimits()
        {
            string format = _step < 0.01f ? "0.000" : _step < 0.1f ? "0.00" : "0.0";
            return $"{_minimum.ToString(format, CultureInfo.InvariantCulture)}-{_maximum.ToString(format, CultureInfo.InvariantCulture)} STEP {_step.ToString(format, CultureInfo.InvariantCulture)}";
        }

        public override object ReadObject()
        {
            return _getter(_target);
        }

        public override void Adjust(int direction)
        {
            Set(_getter(_target) + _step * direction);
        }

        public override void CopyFrom(SimulationEngineParameters source)
        {
            Set(_getter(source));
        }

        public override bool TrySetFromJson(JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.Number && element.TryGetSingle(out float value))
            {
                Set(value);
                return true;
            }

            return false;
        }

        private void Set(float value)
        {
            _setter(_target, MathHelper.Clamp(value, _minimum, _maximum));
        }
    }
}

public sealed record DrivabilityTuningOverlayView(
    bool Visible,
    bool LoadListVisible,
    int Page,
    int PageCount,
    IReadOnlyList<DrivabilityTuningRow> Rows,
    string SelectedName,
    string SelectedPath,
    string Explanation,
    string HigherText,
    string LowerText,
    bool ShowExplanation,
    IReadOnlyList<DrivabilityTuningSaveRow> Saves,
    IReadOnlyList<string> Messages);

public sealed record DrivabilityTuningRow(
    int Index,
    string Group,
    string DisplayName,
    string Path,
    string Value,
    string Limits,
    bool HighImpact,
    bool Selected);

public sealed record DrivabilityTuningSaveRow(
    int Index,
    string Name,
    bool Selected);


