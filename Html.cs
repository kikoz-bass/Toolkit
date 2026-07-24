using System;
using System.Linq;
using System.Reflection;
using System.Text;
using CodeEffects.Rule.Common.Attributes;

namespace CodeEffects.Toolkit
{
	public class Html
	{
		public static string GenerateForm(Type type, FormSettings settings = null)
			=> GenerateForm(type, instance: null, prefix: "", depth: 0, settings ?? new FormSettings());

		public static string GenerateForm(object instance, FormSettings settings = null)
			=> GenerateForm(instance.GetType(), instance, prefix: "", depth: 0, settings ?? new FormSettings());

		private static string GenerateForm(Type type, object instance, string prefix, int depth, FormSettings settings)
		{
			var sa = type.GetCustomAttribute<SourceAttribute>();
			if(sa != null) { settings.DefaultNestingDepth = sa.MaxTypeNestingLevel; }

			var sb = new StringBuilder();

			var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(p => p.CanWrite && !IsExcluded(p));

			foreach(var prop in props)
			{
				if(IsValueTyped(prop.PropertyType))
				{
					var value = instance != null ? prop.GetValue(instance) : null;
					sb.AppendLine(RenderForm(prop, prefix, settings, value));
				}
				else if(IsNestableReferenceType(prop.PropertyType) && depth < settings.DefaultNestingDepth)
				{
					var fa = prop.GetCustomAttribute<FieldAttribute>();
					var nestedInstance = instance != null ? prop.GetValue(instance) : null;

					if(settings.GenerateFieldsets) sb.AppendLine($"<fieldset><legend>{fa?.DisplayName ?? prop.Name}</legend>");
					sb.AppendLine(GenerateForm(prop.PropertyType, nestedInstance, prop.Name + ".", depth + 1, settings));
					if(settings.GenerateFieldsets) sb.AppendLine("</fieldset>");
				}
			}

			return sb.ToString();
		}

		private static string RenderForm(PropertyInfo prop, string prefix, FormSettings settings, object value)
		{
			var name = prefix + prop.Name;
			var (display, fa) = GetDisplayName(prop, prefix);

			var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
			var label = $"<label for='{name}'>{display}:</label>";
			string extra = "";

			if(type.IsEnum)
			{
				// Render enums as drop downs
				var options = string.Join("", Enum.GetValues(type)
					.Cast<object>()
					.Where(v => !IsEnumMemberExcluded(type, v))
					.Select(v =>
					{
						var selected = value != null && Convert.ToInt32(v) == Convert.ToInt32(value) ? " selected" : "";
						return $"<option value='{Convert.ToInt32(v)}'{selected}>{v}</option>";
					}));

				if(settings.DropDownCssClass != null)
					extra = $" class='{settings.DropDownCssClass}'";

				return $"{label}<select id='{name}' name='{name}'{extra}>{options}</select>";
			}

			string inputType =
				type == typeof(bool) ? "checkbox" :
				type == typeof(DateTime) ? "date" :
				type == typeof(TimeSpan) ? "time" :
				type == typeof(int) || type == typeof(long) || type == typeof(short) ? "number" :
				type == typeof(decimal) || type == typeof(double) || type == typeof(float) ? "number" :
				type == typeof(Guid) ? "text" :
				"text";

			if(inputType == "text" && fa != null && fa.Filter != null)
			{
				if(fa.Filter.ToLower() == "phone") inputType = "tel";
				else if(fa.Filter.ToLower() == "email") inputType = "email";
			}

			if(inputType == "checkbox")
			{
				var isChecked = value is true ? " checked" : "";

				if(settings.CheckboxCssClass != null)
					extra = $" class='{settings.CheckboxCssClass}'";

				return $"{label}<input type='checkbox' id='{name}' name='{name}'{isChecked}{extra} />";
			}
			else if(inputType == "number")
			{
				if(type == typeof(decimal) || type == typeof(double) || type == typeof(float))
					extra += " step='any'";

				if(fa != null)
				{
					extra += $" min='{fa?.Min ?? int.MinValue}'";
					extra += $" max='{fa?.Max ?? int.MaxValue}'";
				}
			}
			else if(inputType == "text")
			{
				extra += $" autocomplete='on'";
				if(fa != null) extra += $" maxlength='{fa.Max}'"; // Max defaults to long.MaxValue
			}

			if(settings.InputCssClass != null)
				extra += $" class='{settings.InputCssClass}'";

			string valueAttr = "";

			if(value != null)
			{
				valueAttr = inputType == "date"
					? $" value='{((DateTime)value):yyyy-MM-dd}'"
					: $" value='{value}'";
			}

			return $"{label}<input type='{inputType}' id='{name}' name='{name}'{valueAttr}{extra} />";
		}

		private static bool IsNestableReferenceType(Type t)
			=> t.IsClass && t != typeof(string);

		private static bool IsValueTyped(Type t)
		{
			// We are only interested in value type properties
			var underlying = Nullable.GetUnderlyingType(t) ?? t;
			return underlying.IsValueType || underlying == typeof(string);
		}

		private static bool IsExcluded(PropertyInfo prop)
		{
			// Ignor excluded properties
			return prop.GetCustomAttributes(inherit: true)
				.Any(a => a is ExcludeFromEvaluationAttribute);
		}

		private static bool IsEnumMemberExcluded(Type enumType, object value)
		{
			var field = enumType.GetField(value.ToString());

			return field != null && field.GetCustomAttributes(inherit: false)
				.Any(a => a is ExcludeFromEvaluationAttribute);
		}

		private static (string displayName, FieldAttribute fa) GetDisplayName(PropertyInfo prop, string prefix)
		{
			var displayName = prefix + prop.Name;

			var pa = prop.GetCustomAttributes<ParentAttribute>();
			var fa = prop.GetCustomAttribute<FieldAttribute>();

			if(pa != null && pa.Count() > 0)
			{
				foreach(var a in pa)
				{
					if(a.ParentName != null && a.ParentName == prefix.TrimEnd('.'))
					{
						displayName = a.DisplayName;
						break;
					}
				}
			}
			else if(fa != null && fa.DisplayName != null)
				displayName = fa.DisplayName;

			return (displayName, fa);
		}
	}
}