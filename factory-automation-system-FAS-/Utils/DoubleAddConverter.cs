// Utils/DoubleAddConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace factory_automation_system_FAS_.Utils
{
    /// <summary>
    /// XAML 바인딩 값(double)에 파라미터(double)를 더하는 Converter
    /// 예) Y - 150 => ConverterParameter = -150
    /// </summary>
    public sealed class DoubleAddConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double v = ToDouble(value, culture);
            double add = ToDouble(parameter, culture);
            return v + add;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double v = ToDouble(value, culture);
            double add = ToDouble(parameter, culture);
            return v - add;
        }

        private static double ToDouble(object obj, CultureInfo culture)
        {
            if (obj is null) return 0;

            if (obj is double d) return d;
            if (obj is float f) return f;
            if (obj is int i) return i;
            if (obj is long l) return l;

            var s = obj.ToString();
            if (string.IsNullOrWhiteSpace(s)) return 0;

            // xaml ConverterParameter는 문자열로 들어오는 경우가 많음
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var inv))
                return inv;

            if (double.TryParse(s, NumberStyles.Float, culture, out var loc))
                return loc;

            return 0;
        }
    }
}
