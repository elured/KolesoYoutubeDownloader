using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace KolesoYoutubeDownloader.Converters
{
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object pValue, Type pTargetType, object pParameter, CultureInfo pCulture)
        {
            if (pValue is bool lBoolValue)
            {
                return !lBoolValue;
            }
            return false;
        }

        public object ConvertBack(object pValue, Type pTargetType, object pParameter, CultureInfo pCulture)
        {
            if (pValue is bool lBoolValue)
            {
                return !lBoolValue;
            }

            return false;
        }
    }
}
