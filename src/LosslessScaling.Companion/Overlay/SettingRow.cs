using System.ComponentModel;

namespace LosslessScaling.Companion.Overlay;

/// <summary>One option of a segmented control.</summary>
public sealed record OptionItem(string Text, bool IsActive, Action Select);

public abstract class SettingRow : INotifyPropertyChanged
{
    public string Label { get; }

    /// <summary>Section heading shown above this row (first row of a group only).</summary>
    public string? Section { get; init; }

    public string? Hint { get; init; }

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
    public virtual bool IsBusy => false;
    public virtual string StatusText => "";
    public virtual IReadOnlyList<string> Pills => Array.Empty<string>();

    public abstract void Increment();
    public abstract void Decrement();
    public virtual void SetFraction(double fraction) { }

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

    public EnumSettingRow(string label, string[] options, Func<string> getter, Action<string> setter, string[]? displayNames = null)
        : base(label)
    {
        _options = options;
        _getter = getter;
        _setter = setter;
        _displayNames = displayNames;
    }

    private string Display(int index) => _displayNames?[index] ?? _options[index];

    public override bool IsSegmented => _options.Length <= 3;
    public override bool IsStepper => _options.Length > 3;

    public override string ValueText
    {
        get
        {
            var index = Array.IndexOf(_options, _getter());
            return index < 0 ? _getter() : Display(index);
        }
    }

    public override IReadOnlyList<OptionItem> Segments
    {
        get
        {
            var current = _getter();
            return _options.Select((option, i) => new OptionItem(Display(i), option == current, () => _setter(option))).ToList();
        }
    }

    private void Move(int delta)
    {
        var index = Array.IndexOf(_options, _getter());
        if (index < 0) index = 0;
        index = (index + delta + _options.Length) % _options.Length;
        _setter(_options[index]);
    }

    public override void Increment() => Move(1);
    public override void Decrement() => Move(-1);
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
    public override string ValueText => $"{_getter()}{_suffix}";
    public override double Progress => _max == _min ? 0 : (double)(_getter() - _min) / (_max - _min);

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
    public override string ValueText => _getter().ToString("0.00");
    public override double Progress => _max <= _min ? 0 : (_getter() - _min) / (_max - _min);

    public override void Increment() => _setter(Math.Clamp(Math.Round((_getter() + _step) * 100) / 100, _min, _max));
    public override void Decrement() => _setter(Math.Clamp(Math.Round((_getter() - _step) * 100) / 100, _min, _max));

    public override void SetFraction(double fraction)
    {
        var steps = Math.Round(Math.Clamp(fraction, 0, 1) * (_max - _min) / _step);
        _setter(Math.Clamp(Math.Round((_min + steps * _step) * 100) / 100, _min, _max));
    }
}
