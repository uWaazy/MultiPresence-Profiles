using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MultiPresenceProfiles.Common;

public interface IMemoryAccessor
{
	bool IsAttached { get; }
	Process AttachedProcess { get; }
	ulong GetModuleBaseAddress(string moduleName);
	T Read<T>(ulong address, bool isAbsolute = true) where T : unmanaged;
	bool TryReadBytes(ulong address, int length, out byte[] buffer);
	string ReadString(ulong address, int maxBytes, bool isAbsolute = true, Encoding encoding = null, bool stopAtNull = true);
	ulong GetPointer64(ulong baseAddress, uint[] offsets, bool isAbsolute = true);
}

public static class JsonHelper
{
	public static T Deserialize<T>(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return default;
		}
		var serializer = new DataContractJsonSerializer(typeof(T));
		using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
		{
			return (T)serializer.ReadObject(ms);
		}
	}
}

public sealed class PresenceSnapshot
{
	public string DiscordAppId { get; set; }
	public string State { get; set; }
	public string Details { get; set; }
	public string LargeImageKey { get; set; }
	public string LargeImageText { get; set; }
	public string SmallImageKey { get; set; }
	public string SmallImageText { get; set; }
	public long? StartTimestampUnix { get; set; }
	public long? EndTimestampUnix { get; set; }
	public Dictionary<string, string> Tokens { get; } = new Dictionary<string, string>();

	public void Set(string key, object value)
	{
		if (!string.IsNullOrWhiteSpace(key))
		{
			Tokens[key] = value?.ToString() ?? string.Empty;
		}
	}
}
