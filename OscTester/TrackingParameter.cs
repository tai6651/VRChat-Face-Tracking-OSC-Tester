using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace OscTester;

internal sealed class TrackingParameter(int index, Action<int, byte>? onChange, string? displayName = null,
    string? description = null) : INotifyPropertyChanged
{
    private byte value;
    private string valueText = "0";
    private bool hasInvalidValue;

    public int Index => index;
    public string Name => displayName ?? TrackingKeys.Names[index];
    public string? Description => description;
    public bool IsEditable => onChange is not null;
    public bool IsReadOnly => !IsEditable;
    public bool IsFeatured => Name is "EyeClosedRight" or "EyeClosedLeft" or "JawOpen";

    public int Value
    {
        get => value;
        set
        {
            if (IsReadOnly)
                return;
            SetValue((byte)value);
            ValueText = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    public string ValueText
    {
        get => valueText;
        set
        {
            if (IsReadOnly || valueText == value)
                return;

            valueText = value;
            Notify();
            HasInvalidValue = !byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out byte parsed);
            if (!HasInvalidValue)
                SetValue(parsed);
        }
    }

    public bool HasInvalidValue
    {
        get => hasInvalidValue;
        private set
        {
            if (hasInvalidValue == value)
                return;

            hasInvalidValue = value;
            Notify();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Reset()
    {
        value = 0;
        valueText = "0";
        HasInvalidValue = false;
        Notify(nameof(Value));
        Notify(nameof(ValueText));
    }

    internal void ApplyPlaybackValue(byte newValue)
    {
        if (value == newValue && !hasInvalidValue)
            return;

        bool changed = value != newValue;
        value = newValue;
        valueText = newValue.ToString(CultureInfo.InvariantCulture);
        HasInvalidValue = false;
        if (changed)
            Notify(nameof(Value));
        Notify(nameof(ValueText));
    }

    private void SetValue(byte newValue)
    {
        if (value == newValue)
            return;

        value = newValue;
        onChange?.Invoke(index, value);
        Notify(nameof(Value));
    }

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
