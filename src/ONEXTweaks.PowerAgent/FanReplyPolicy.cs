using System;
using System.Globalization;
using System.IO;

namespace ONEXTweaks.Power
{
    internal static class FanReplyPolicy
    {
        internal static int Percent(object value)
        {
            // A missing measurement is not firmware fan-stop at0%. Boolean
            // replies also cannot establish a percentage measurement.
            if (value == null || value is bool)
                throw new IOException("OEM fan speed reply did not contain a percentage");
            int percent;
            try { percent = Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch (Exception error)
            {
                if (!(error is FormatException) && !(error is InvalidCastException) && !(error is OverflowException)) throw;
                throw new IOException("OEM fan speed reply could not be read as a percentage", error);
            }
            if (percent < 0 || percent > 100)
                throw new IOException("OEM fan speed reply is outside the percentage range");
            return percent;
        }
    }
}
