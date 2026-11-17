using System;
using System.IO;
using System.Text;

namespace MySqlServiceController;

internal static class TextDecoder
{
	public const int MaxTextFileBytes = 4 * 1024 * 1024;

	public static string Decode(byte[] buffer, int count)
	{
		if (buffer == null || count <= 0)
		{
			return string.Empty;
		}
		Encoding encoding;
		int offset = 0;
		if (count >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
		{
			encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			offset = 3;
		}
		else if (count >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
		{
			encoding = Encoding.Unicode;
			offset = 2;
		}
		else if (count >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
		{
			encoding = Encoding.BigEndianUnicode;
			offset = 2;
		}
		else
		{
			try
			{
				new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(buffer, 0, count);
				encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			}
			catch
			{
				encoding = Encoding.Default;
			}
		}
		try
		{
			return encoding.GetString(buffer, offset, count - offset);
		}
		catch
		{
			return Encoding.Default.GetString(buffer, 0, count);
		}
	}

	public static bool TryReadFile(string path, out string text, out byte[] rawBytes, out string error)
	{
		text = string.Empty;
		rawBytes = null;
		error = null;
		if (string.IsNullOrWhiteSpace(path))
		{
			error = "路径为空。";
			return false;
		}
		try
		{
			using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			long num = Math.Min(stream.Length, MaxTextFileBytes);
			byte[] array = new byte[(int)num];
			int num2 = 0;
			while (num2 < array.Length)
			{
				int num3 = stream.Read(array, num2, array.Length - num2);
				if (num3 <= 0)
				{
					break;
				}
				num2 += num3;
			}
			rawBytes = array;
			text = Decode(array, num2);
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	public static bool TryReadTail(string path, int maxBytes, out string text, out long fileLength, out bool truncated, out string error)
	{
		text = string.Empty;
		fileLength = 0L;
		truncated = false;
		error = null;
		if (string.IsNullOrWhiteSpace(path))
		{
			error = "路径为空。";
			return false;
		}
		if (maxBytes < 1024)
		{
			maxBytes = 1024;
		}
		try
		{
			using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			fileLength = stream.Length;
			truncated = fileLength > maxBytes;
			long num = (truncated ? (fileLength - maxBytes) : 0);
			stream.Seek(num, SeekOrigin.Begin);
			byte[] array = new byte[(int)(fileLength - num)];
			int num2 = 0;
			while (num2 < array.Length)
			{
				int num3 = stream.Read(array, num2, array.Length - num2);
				if (num3 <= 0)
				{
					break;
				}
				num2 += num3;
			}
			text = Decode(array, num2);
			if (truncated && text.Length > 0)
			{
				int num4 = text.IndexOf('\n');
				if (num4 >= 0 && num4 + 1 < text.Length)
				{
					text = text.Substring(num4 + 1);
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}
}
