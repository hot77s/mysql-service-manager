using System;
using System.Collections.Generic;

namespace MySqlServiceController;

internal sealed class IniDocument
{
	private readonly Dictionary<string, Dictionary<string, string>> _sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

	public static IniDocument Parse(string text)
	{
		IniDocument document = new IniDocument();
		if (string.IsNullOrEmpty(text))
		{
			return document;
		}
		string currentSection = string.Empty;
		string[] array = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		foreach (string item in array)
		{
			string text2 = item.Trim();
			if (text2.Length == 0 || text2[0] == ';' || text2[0] == '#')
			{
				continue;
			}
			if (text2[0] == '[')
			{
				int num = text2.IndexOf(']');
				if (num > 1)
				{
					currentSection = text2.Substring(1, num - 1).Trim();
				}
				continue;
			}
			int num2 = text2.IndexOf('=');
			if (num2 <= 0)
			{
				continue;
			}
			string text3 = NormalizeKey(text2.Substring(0, num2).Trim());
			string text4 = text2.Substring(num2 + 1).Trim();
			if (text4.Length >= 2)
			{
				char c = text4[0];
				if ((c == '"' || c == '\'') && text4[text4.Length - 1] == c)
				{
					text4 = text4.Substring(1, text4.Length - 2);
				}
			}
			document.Set(currentSection, text3, text4);
		}
		return document;
	}

	public static string NormalizeKey(string key)
	{
		return (key ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
	}

	private void Set(string section, string key, string value)
	{
		if (!_sections.TryGetValue(section, out Dictionary<string, string> value2))
		{
			value2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			_sections[section] = value2;
		}
		value2[key] = value;
	}

	public bool HasSection(string section)
	{
		return _sections.ContainsKey(section ?? string.Empty);
	}

	public string Get(string section, string key)
	{
		if (_sections.TryGetValue(section ?? string.Empty, out Dictionary<string, string> value) && value.TryGetValue(NormalizeKey(key), out string value2))
		{
			return value2;
		}
		return null;
	}

	public string GetFirst(string key)
	{
		string text = Get("mysqld", key);
		if (text == null)
		{
			text = Get("server", key);
		}
		if (text == null)
		{
			text = Get("mariadb", key);
		}
		if (text == null)
		{
			text = Get("mariadbd", key);
		}
		return text;
	}
}
