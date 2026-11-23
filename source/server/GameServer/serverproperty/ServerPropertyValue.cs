using System;
using System.Globalization;

namespace DOL.GS.ServerProperties
{
	/// <summary>
	/// Reads a stored server property's text as the type of its default (1.2.3, from a r/daoc report: no endurance
	/// regeneration at all). Before 1.2.3 a value the server couldn't read left the setting at the C# field's own default,
	/// which is 0 for a double with no initializer: endurance_regen_amount_modifier stored blank or as "1.0x"
	/// multiplied every endurance tick, buffs and Tireless included, by 0. Pure.
	/// </summary>
	public static class ServerPropertyValue
	{
		private static readonly IFormatProvider EnUs = new CultureInfo("en-US", false).NumberFormat;

		/// <summary>
		/// The stored text as <paramref name="type"/>, read as the server always has (en-US numbers), except that a number
		/// written with a comma decimal ("1,0", "0,5", "1,25": one comma, no point, not three digits after it) is read as that
		/// number; en-US alone reads "1,0" as 10 and "0,5" as 5 (the comma as a thousands separator). False when the text can't
		/// be read as the type (blank, missing, "1.0x" for a number; "yes" for a switch).
		/// </summary>
		public static bool TryRead(string text, Type type, out object value)
		{
			value = null;
			if (text == null || type == null)
				return false;
			if (IsDecimalType(type) && TryReadCommaDecimal(text, out double commaNumber))
			{
				value = Convert.ChangeType(commaNumber, type, CultureInfo.InvariantCulture);
				return true;
			}
			try
			{
				value = Convert.ChangeType(text, type, EnUs);
				return true;
			}
			catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
			{
				value = null;
				return false;
			}
		}

		/// <summary>"1,0", "0,5", "-1,25": one comma, no point, one, two or four or more digits after it. Pure.</summary>
		public static bool TryReadCommaDecimal(string text, out double number)
		{
			number = 0;
			string trimmed = text?.Trim() ?? "";
			int comma = trimmed.IndexOf(',');
			if (comma <= 0 || trimmed.IndexOf(',', comma + 1) >= 0 || trimmed.IndexOf('.') >= 0)
				return false;
			int decimals = trimmed.Length - comma - 1;
			if (decimals == 0 || decimals == 3)
				return false;
			return double.TryParse(trimmed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
		}

		/// <summary>
		/// The value the server uses: the stored text when it can be read, otherwise <paramref name="defaultValue"/> (the
		/// attribute's default). <paramref name="fellBack"/> tells the caller to log the bad value.
		/// </summary>
		public static object ReadOrDefault(string text, object defaultValue, out bool fellBack)
		{
			if (defaultValue == null)
				throw new ArgumentNullException(nameof(defaultValue));
			if (TryRead(text, defaultValue.GetType(), out object value))
			{
				fellBack = false;
				return value;
			}
			fellBack = true;
			return defaultValue;
		}

		private static bool IsDecimalType(Type type) => type == typeof(double) || type == typeof(float) || type == typeof(decimal);

	}
}
