using System.Globalization;

namespace VIGIL.Controls
{
    /// <summary>
    ///  How figures from the analysis service are written out. This only formats: it rounds
    ///  for reading and never changes what a number means.
    /// </summary>
    public static class Numbers
    {
        private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

        /// <summary>A number to three significant figures, with a true minus sign and no plus.</summary>
        public static string Plain(double value)
        {
            if (value == 0 || double.IsNaN(value))
            {
                return "0";
            }
            int decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(Math.Abs(value))), 0, 8);
            string text = Math.Abs(value).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), British);
            return (value < 0 ? "−" : "") + text;
        }

        /// <summary>A number to three significant figures with its sign always shown, for a change or a slope.</summary>
        public static string Signed(double value) => value > 0 ? "+" + Plain(value) : Plain(value);

        /// <summary>
        ///  A p-value: three decimals down to 0.001, and below that as a power of ten, such as 1.4e-5.
        ///  The analysis stores a p-value too small for its arithmetic as exactly 0, which is not a
        ///  probability of nothing: it is shown as below the smallest value that arithmetic can hold.
        /// </summary>
        public static string P(double value)
        {
            if (value >= 0.001)
            {
                return value.ToString(value >= 0.01 ? "0.00" : "0.000", British);
            }
            return value <= 0 ? "below 1e-16" : value.ToString("0.0e-0", British);
        }

        /// <summary>A whole number with thousands separators.</summary>
        public static string Count(int value) => value.ToString("N0", British);

        /// <summary>A share from 0 to 1 as a whole percentage.</summary>
        public static string Percent(double share) => (share * 100).ToString("0", British) + "%";

        /// <summary>The first day of a month as "June 2019".</summary>
        public static string Month(DateOnly date) => date.ToString("MMMM yyyy", British);
    }
}
