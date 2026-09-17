using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KolesoYoutubeDownloader.Converters
{
    public class PercentToGridLengthConverter : IValueConverter
    {
        public object Convert(object pValue, Type pTargetType, object pParameter, CultureInfo pCulture)
        {
            if (pValue is double lPercent)
            {
                bool lIsRemainder = pParameter != null && pParameter.ToString() == "Remainder";

                // Если это вторая колонка (остаток), возвращаем (100 - процент)
                return lIsRemainder
                    ? new GridLength(100 - lPercent, GridUnitType.Star)
                    : new GridLength(lPercent, GridUnitType.Star);
            }
            return new GridLength(0);
        }

        public object ConvertBack(object pValue, Type pTargetType, object pParameter, CultureInfo pCulture)
        {
            throw new NotSupportedException();
        }
    }
}