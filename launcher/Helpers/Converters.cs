using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HermesLauncher.Helpers;

public class IntToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter != null && int.TryParse(parameter.ToString(), out var targetInt))
        {
            return intValue == targetInt;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue && parameter != null && int.TryParse(parameter.ToString(), out var targetInt))
        {
            return targetInt;
        }
        return Binding.DoNothing;
    }
}

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter != null && int.TryParse(parameter.ToString(), out var targetInt))
        {
            return intValue == targetInt ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is bool b && b;
        if (parameter?.ToString() == "Inverse")
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null)
        {
            return parameter.ToString()!;
        }
        return Binding.DoNothing;
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StringEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isEmpty = string.IsNullOrWhiteSpace(value?.ToString());
        if (parameter?.ToString() == "Inverse")
            isEmpty = !isEmpty;
        return isEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DoubleToPercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return $"{Math.Clamp(Math.Round(d * 100), 0, 100)}%";
        }
        return "0%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LatencyToBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush FastBrush = new(System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99)); // #34D399 (Green < 100ms)
    private static readonly System.Windows.Media.SolidColorBrush MediumBrush = new(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24)); // #FBBF24 (Amber < 300ms)
    private static readonly System.Windows.Media.SolidColorBrush SlowBrush = new(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71)); // #F87171 (Red >= 300ms)
    private static readonly System.Windows.Media.SolidColorBrush MutedBrush = new(System.Windows.Media.Color.FromRgb(0x7C, 0x8C, 0xA3)); // #7C8CA3

    static LatencyToBrushConverter()
    {
        FastBrush.Freeze();
        MediumBrush.Freeze();
        SlowBrush.Freeze();
        MutedBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int ms)
        {
            if (ms <= 0) return MutedBrush;
            if (ms < 100) return FastBrush;
            if (ms < 300) return MediumBrush;
            return SlowBrush;
        }
        return MutedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

