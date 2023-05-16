using System;

namespace Bow2.FA.Helpers
{
    internal static class Converter
    {
        internal static short ConvertEnumToShort<TEnum>(string enumString) where TEnum : struct, Enum
        {
            if (Enum.TryParse(enumString, ignoreCase: true, out TEnum enumValue))
            {
                return Convert.ToInt16(enumValue);
            }

            throw new ArgumentException("Invalid enum value");
        }
    }
}
