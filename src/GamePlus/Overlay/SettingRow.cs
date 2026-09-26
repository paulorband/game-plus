using System.ComponentModel;

namespace GamePlus.Overlay;

/// <summary>One option of a segmented control.</summary>
public sealed record OptionItem(string Text, bool IsActive, Action Select);

public abstract class SettingRow : INotifyPropertyChanged
{
    public string Label { get; }

    /// <summary>Section heading shown above this row (first row of a group only).</summary>
    public string? Section { get; init; }

    private readonly string? _hint;

    /// <summary>Static hint under the label; HintWhen (when set and non-empty) overrides it, e.g. to explain why a row is unavailable.</summary>
    public string? Hint { get => HintWhen?.Invoke() ?? _hint; init => _hint = value; }

    public Func<string?>? HintWhen { get; init; }

    /// <summary>Hex ("RRGGBB") of a color preview shown beside the value (color rows only).</summary>
    public Func<string>? SwatchHex { get; init; }
    public bool HasSwatch => SwatchHex is not null;
    public System.Windows.Media.Brush? Swatch
    {
        get
        {
            if (SwatchHex is null) return null;
            var (r, g, b) = GamePlus.Models.ColorPresets.Split(SwatchHex());
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)r, (byte)g, (byte)b));
        }
    }

    /// <summary>When set and false, the row is dimmed and skipped by navigation.</summary>
    public Func<bool>? EnabledWhen { get; init; }

    protected SettingRow(string label)
    {
        Label = label;
    }

    public bool HasSection => !string.IsNullOrEmpty(Section);
    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public bool Available => EnabledWhen?.Invoke() ?? true;

    public virtual string ValueText => "";

    // Which control the row template shows.
    public virtual bool IsMaster => false;
    public virtual bool IsStepper => false;
    public virtual bool IsToggle => false;
    public virtual bool IsSlider => false;
    public virtual bool IsSegmented => false;
    public virtual bool IsPosition => false;

    public virtual bool IsOn => false;
    public virtual double Progress => 0;
    public virtual IReadOnlyList<OptionItem> Segments => Array.Empty<OptionItem>();

    // Slider track split (star widths).
    public System.Windows.GridLength FillWidth => new(Math.Clamp(Progress, 0, 1), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength RestWidth => new(1 - Math.Clamp(Progress, 0, 1), System.Windows.GridUnitType.Star);

    // Master card only.
    public virtual bool StatusOn => false;
    public virtual bool ShowSwitch => true;
    public virtual bool ShowInstall => false;
    /// <summary>Master card in warning colors (something is missing).</summary>
    public virtual bool IsAlert => false;
    public virtual bool IsBusy => false;
    public virtual string StatusText => "";
    public virtual IReadOnlyList<string> Pills => Array.Empty<string>();

    public abstract void Increment();
    public abstract void Decrement();
    public virtual void SetFraction(double fraction) { }

    // Hold-to-preview (gamepad hold on left/right): steps move a pending value that is applied only on release,
    // so a held direction doesn't change the real setting at every repeat. Rows with 2 values apply immediately.
    public virtual bool HoldToPreview => false;
    public virtual void Preview(int delta) { if (delta < 0) Decrement(); else Increment(); }
    public virtual void Commit() { }

    /// <summary>Re-reads every bound property (values, pills, availability).</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public event PropertyChangedEventHandler? PropertyChanged;
}

public enum MasterState { NotInstalled, Closed, Opening, Starting, Idle, Scaling }

/// <summary>The top card: shows Lossless Scaling state and fires the toggle hotkey.</summary>
public sealed class ActionRow : SettingRow
{
    private readonly Action _action;
    private readonly Func<MasterState> _state;
    private readonly Func<IReadOnlyList<string>> _pills;

    public ActionRow(string label, Action action, Func<MasterState> state, Func<IReadOnlyList<string>> pills) : base(label)
    {
        _action = action;
        _state = state;
        _pills = pills;
    }

    public override bool IsMaster => true;
    public override bool StatusOn => _state() == MasterState.Scaling;
    public override bool IsOn => _state() is MasterState.Scaling or MasterState.Starting;
    public override bool ShowInstall => _state() == MasterState.NotInstalled;
    public override bool IsAlert => _state() == MasterState.NotInstalled;
    public override bool ShowSwitch => _state() != MasterState.NotInstalled;
    public override bool IsBusy => _state() is MasterState.Opening or MasterState.Starting;

    /// <summary>Seconds left before frame generation starts (shown while the state is Starting).</summary>
    public Func<int>? Countdown { get; init; }
    public override string StatusText => _state() switch
    {
        MasterState.NotInstalled => "Lossless Scaling não está instalado neste PC",
        MasterState.Closed => "Lossless Scaling fechado — será aberto ao ligar",
        MasterState.Opening => "Abrindo o Lossless Scaling…",
        MasterState.Starting => $"A geração de quadros inicia em {Countdown?.Invoke()}s — A cancela",
        MasterState.Scaling => "Ativo",
        _ => "Desligado",
    };
    public override IReadOnlyList<string> Pills => _state() == MasterState.NotInstalled ? ["Disponível na Steam"] : _pills();

    public override void Increment() => _action();
    public override void Decrement() { }
}

public sealed class EnumSettingRow : SettingRow
{
    private readonly string[] _options;
    private readonly Func<string> _getter;
    private readonly Action<string> _setter;
    private readonly string[]? _displayNames;
    private readonly bool _forceStepper;

    public EnumSettingRow(string label, string[] options, Func<string> getter, Action<string> setter, string[]? displayNames = null, bool forceStepper = false)
        : base(label)
    {
        _forceStepper = forceStepper;
        _options = options;
        _getter = getter;
        _setter = setter;
        _displayNames = displayNames;
    }

    private string Display(int index) => _displayNames?[index] ?? _options[index];

    public override bool IsSegmented => _options.Length <= 3 && !_forceStepper;
    public override bool IsStepper => _options.Length > 3 || _forceStepper;

    private int? _pending;

    private string Current => _pending is int p ? _options[p] : _getter();

    public override string ValueText
    {
        get
        {
            var index = Array.IndexOf(_options, Current);
            return index < 0 ? Current : Display(index);
        }
    }

    public override IReadOnlyList<OptionItem> Segments
    {
        get
        {
            var current = Current;
            return _options.Select((option, i) => new OptionItem(Display(i), option == current, () => _setter(option))).ToList();
        }
    }

    private void Move(int delta)
    {
        var index = Array.IndexOf(_options, _getter());
        // A value outside the list (e.g. a custom color) enters it from the matching end.
        index = index < 0 ? (delta > 0 ? 0 : _options.Length - 1) : (index + delta + _options.Length) % _options.Length;
        _setter(_options[index]);
    }

    public override void Increment() => Move(1);
    public override void Decrement() => Move(-1);

    public override bool HoldToPreview => _options.Length > 2;

    public override void Preview(int delta)
    {
        var index = Array.IndexOf(_options, Current);
        _pending = index < 0 ? (delta > 0 ? 0 : _options.Length - 1) : (index + delta + _options.Length) % _options.Length;
    }

    public override void Commit()
    {
        if (_pending is not int p) return;
        _pending = null;
        if (_options[p] != _getter()) _setter(_options[p]);
    }
}

public sealed class BoolSettingRow : SettingRow
{
    private readonly Func<bool> _getter;
    private readonly Action<bool> _setter;

    public BoolSettingRow(string label, Func<bool> getter, Action<bool> setter) : base(label)
    {
        _getter = getter;
        _setter = setter;
    }

    public override bool IsToggle => true;
    public override bool IsOn => _getter();
    public override string ValueText => _getter() ? "Ligado" : "Desligado";

    public override void Increment() => _setter(!_getter());
    public override void Decrement() => _setter(!_getter());
}

public sealed class IntSettingRow : SettingRow
{
    private readonly Func<int> _getter;
    private readonly Action<int> _setter;
    private readonly int _min;
    private readonly int _max;
    private readonly int _step;
    private readonly string _suffix;
    private readonly bool _slider;

    public IntSettingRow(string label, Func<int> getter, Action<int> setter, int min, int max, int step, string suffix = "", bool slider = false)
        : base(label)
    {
        _getter = getter;
        _setter = setter;
        _min = min;
        _max = max;
        _step = step;
        _suffix = suffix;
        _slider = slider;
    }

    public override bool IsSlider => _slider;
    public override bool IsStepper => !_slider;
    private int? _pending;
    private int Current => _pending ?? _getter();

    public override string ValueText => $"{Current}{_suffix}";
    public override double Progress => _max == _min ? 0 : (double)(Current - _min) / (_max - _min);

    public override bool HoldToPreview => true;
    public override void Preview(int delta) => _pending = Math.Clamp(Current + delta * _step, _min, _max);

    public override void Commit()
    {
        if (_pending is not int p) return;
        _pending = null;
        if (p != _getter()) _setter(p);
    }

    public override void Increment() => _setter(Math.Clamp(_getter() + _step, _min, _max));
    public override void Decrement() => _setter(Math.Clamp(_getter() - _step, _min, _max));

    public override void SetFraction(double fraction)
    {
        var steps = Math.Round(Math.Clamp(fraction, 0, 1) * (_max - _min) / _step);
        _setter(Math.Clamp(_min + (int)steps * _step, _min, _max));
    }
}

public sealed class DoubleSettingRow : SettingRow
{
    private readonly Func<double> _getter;
    private readonly Action<double> _setter;
    private readonly double _min;
    private readonly double _max;
    private readonly double _step;
    private readonly bool _slider;

    public DoubleSettingRow(string label, Func<double> getter, Action<double> setter, double min, double max, double step, bool slider = false)
        : base(label)
    {
        _getter = getter;
        _setter = setter;
        _min = min;
        _max = max;
        _step = step;
        _slider = slider;
    }

    public override bool IsSlider => _slider;
    public override bool IsStepper => !_slider;
    private double? _pending;
    private double Current => _pending ?? _getter();

    public override string ValueText => Current.ToString("0.00");
    public override double Progress => _max <= _min ? 0 : (Current - _min) / (_max - _min);

    public override bool HoldToPreview => true;
    public override void Preview(int delta) => _pending = Math.Clamp(Math.Round((Current + delta * _step) * 100) / 100, _min, _max);

    public override void Commit()
    {
        if (_pending is not double p) return;
        _pending = null;
        if (Math.Abs(p - _getter()) > 1e-9) _setter(p);
    }

    public override void Increment() => _setter(Math.Clamp(Math.Round((_getter() + _step) * 100) / 100, _min, _max));
    public override void Decrement() => _setter(Math.Clamp(Math.Round((_getter() - _step) * 100) / 100, _min, _max));

    public override void SetFraction(double fraction)
    {
        var steps = Math.Round(Math.Clamp(fraction, 0, 1) * (_max - _min) / _step);
        _setter(Math.Clamp(Math.Round((_min + steps * _step) * 100) / 100, _min, _max));
    }
}


/// <summary>Screen-corner picker: a 3x3 grid (0 = top left ... 8 = bottom right); left/right walk through the cells.</summary>
public sealed class PositionRow : SettingRow
{
    private static readonly string[] Names =
    [
        "Superior esquerdo", "Superior centro", "Superior direito",
        "Centro esquerdo", "Centro", "Centro direito",
        "Inferior esquerdo", "Inferior centro", "Inferior direito",
    ];

    private readonly Func<int> _getter;
    private readonly Action<int> _setter;

    public PositionRow(string label, Func<int> getter, Action<int> setter) : base(label)
    {
        _getter = getter;
        _setter = setter;
    }

    public static string NameOf(int position) => Names[Math.Clamp(position, 0, 8)];

    public override bool IsPosition => true;
    private int? _pending;
    private int Current => _pending ?? _getter();

    public override string ValueText => NameOf(Current);

    public override IReadOnlyList<OptionItem> Segments =>
        Enumerable.Range(0, 9).Select(i => new OptionItem("", i == Current, () => _setter(i))).ToList();

    public override bool HoldToPreview => true;
    public override void Preview(int delta) => _pending = (Current + delta + 9) % 9;

    public override void Commit()
    {
        if (_pending is not int p) return;
        _pending = null;
        if (p != _getter()) _setter(p);
    }

    public override void Increment() => _setter((_getter() + 1) % 9);
    public override void Decrement() => _setter((_getter() + 8) % 9);
}

/// <summary>Top card of the Monitoring category: shows whether the on-screen display is live and switches it.</summary>
public sealed class MonitoringMasterRow : SettingRow
{
    private readonly Func<Services.Monitoring.MonitoringStatus> _status;
    private readonly Models.MonitoringConfig _config;
    private readonly Func<int> _metricCount;
    private readonly Action _changed;
    private readonly Func<bool> _installed;
    private readonly Func<bool> _opening;
    private readonly Action _open;

    /// <param name="installed">Whether Afterburner is installed (when it isn't running).</param>
    /// <param name="opening">True while the overlay is starting Afterburner/RTSS.</param>
    /// <param name="open">Starts Afterburner/RTSS, then switches the display on.</param>
    public MonitoringMasterRow(string label, Models.MonitoringConfig config, Func<Services.Monitoring.MonitoringStatus> status,
        Func<int> metricCount, Action changed, Func<bool> installed, Func<bool> opening, Action open)
        : base(label)
    {
        _config = config;
        _status = status;
        _metricCount = metricCount;
        _changed = changed;
        _installed = installed;
        _opening = opening;
        _open = open;
    }

    private bool Ready => _status().Ready;
    private bool Missing => !Ready && !_installed() && !_opening();

    public override bool IsMaster => true;
    public override bool IsOn => _opening() || (_config.Enabled && Ready);
    public override bool StatusOn => _config.Enabled && Ready && !_opening();
    public override bool ShowSwitch => !Missing;
    public override bool IsBusy => _opening();
    public override bool IsAlert => Missing;

    public override string StatusText
    {
        get
        {
            var s = _status();
            if (_opening()) return s.Afterburner ? "Abrindo o RTSS…" : "Abrindo o MSI Afterburner…";
            if (Missing) return "MSI Afterburner não está instalado neste PC";
            if (!s.Afterburner) return "MSI Afterburner fechado — será aberto ao ligar";
            if (!s.Rtss) return "RTSS fechado — será aberto ao ligar";
            if (!_config.Enabled) return "Desligado";
            return s.Game is null ? "Ligado — aparece quando um jogo estiver rodando" : $"Visível em {s.Game}";
        }
    }

    public override IReadOnlyList<string> Pills =>
        Missing ? ["Requer Afterburner e RTSS"]
        : !Ready ? ["Sensores indisponíveis"]
        : [$"{_metricCount()} métricas", PositionRow.NameOf(_config.Position), $"Texto {_config.TextScale}%"];

    public override void Increment()
    {
        if (_opening() || Missing) return;
        if (!Ready)
        {
            _open();
            return;
        }
        _config.Enabled = !_config.Enabled;
        _changed();
    }

    public override void Decrement() { }
}
