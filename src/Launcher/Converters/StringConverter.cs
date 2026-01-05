using Avalonia.Data.Converters;
using Launcher.Models;
using System;
using System.Globalization;

namespace Launcher.Converters;

public static class StringConverter
{
    public static readonly ToLocaleConverter ToLocale = new();

    public class ToLocaleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not LocaleType localeType)
                return null;

            return Locale.Supported.Find(x => x.Type == localeType);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Locale locale)
                return null;

            return locale.Type;
        }
    }
}