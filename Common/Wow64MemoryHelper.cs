using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MultiPresenceProfiles.Common;

public struct SectionInfo
{
	public string Name;
	public ulong VirtualAddress;
	public uint VirtualSize;
}

public static class Wow64MemoryHelper
{
	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CloseHandle(IntPtr hObject);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

	[DllImport("ntdll.dll", SetLastError = true)]
	public static extern int NtWow64QueryInformationProcess64(
		IntPtr ProcessHandle,
		int ProcessInformationClass,
		ref PROCESS_BASIC_INFORMATION64 ProcessInformation,
		int ProcessInformationLength,
		out int ReturnLength);

	[DllImport("ntdll.dll", SetLastError = true)]
	public static extern int NtWow64ReadVirtualMemory64(
		IntPtr ProcessHandle,
		ulong BaseAddress,
		[Out] byte[] Buffer,
		ulong BufferLength,
		out ulong ReturnLength);

	[StructLayout(LayoutKind.Sequential)]
	public struct PROCESS_BASIC_INFORMATION64
	{
		public ulong ExitStatus;
		public ulong PebBaseAddress;
		public ulong AffinityMask;
		public ulong BasePriority;
		public ulong UniqueProcessId;
		public ulong InheritedFromUniqueProcessId;
	}

	public static bool IsProcess64Bit(IntPtr hProcess)
	{
		if (!Environment.Is64BitOperatingSystem)
		{
			return false;
		}

		if (IsWow64Process(hProcess, out bool isWow64))
		{
			return !isWow64;
		}

		return false;
	}

	public static bool Get64BitModuleInfo(IntPtr hProcess, out ulong imageBase, out int imageSize, out List<SectionInfo> sections)
	{
		imageBase = 0;
		imageSize = 0;
		sections = new List<SectionInfo>();

		try
		{
			PROCESS_BASIC_INFORMATION64 pbi = new PROCESS_BASIC_INFORMATION64();
			int retLen;
			int status = NtWow64QueryInformationProcess64(hProcess, 0, ref pbi, Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION64)), out retLen);
			if (status != 0 || pbi.PebBaseAddress == 0)
			{
				return false;
			}

			byte[] pebBuf = new byte[32];
			ulong readLen;
			int rPeb = NtWow64ReadVirtualMemory64(hProcess, pbi.PebBaseAddress, pebBuf, (ulong)pebBuf.Length, out readLen);
			if (rPeb != 0 || readLen < 24)
			{
				return false;
			}

			imageBase = BitConverter.ToUInt64(pebBuf, 0x10);
			if (imageBase == 0)
			{
				return false;
			}

			Parse64BitSections(hProcess, imageBase, out imageSize, sections);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool Get64BitModuleByName(IntPtr hProcess, string targetModuleName, out ulong imageBase, out int imageSize, out List<SectionInfo> sections)
	{
		imageBase = 0;
		imageSize = 0;
		sections = new List<SectionInfo>();
		if (string.IsNullOrWhiteSpace(targetModuleName))
		{
			return false;
		}

		try
		{
			PROCESS_BASIC_INFORMATION64 pbi = new PROCESS_BASIC_INFORMATION64();
			int retLen;
			if (NtWow64QueryInformationProcess64(hProcess, 0, ref pbi, Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION64)), out retLen) != 0 || pbi.PebBaseAddress == 0)
			{
				return false;
			}

			byte[] pebBuf = new byte[32];
			ulong readLen;
			if (NtWow64ReadVirtualMemory64(hProcess, pbi.PebBaseAddress, pebBuf, (ulong)pebBuf.Length, out readLen) != 0 || readLen < 24)
			{
				return false;
			}

			ulong ldr = BitConverter.ToUInt64(pebBuf, 0x18);
			if (ldr == 0)
			{
				return false;
			}

			byte[] ldrBuf = new byte[32];
			if (NtWow64ReadVirtualMemory64(hProcess, ldr + 0x10, ldrBuf, 16, out readLen) != 0 || readLen < 16)
			{
				return false;
			}

			ulong listHead = ldr + 0x10;
			ulong cur = BitConverter.ToUInt64(ldrBuf, 0);
			byte[] entryBuf = new byte[0x70];
			for (int i = 0; i < 256 && cur != 0 && cur != listHead; i++)
			{
				if (NtWow64ReadVirtualMemory64(hProcess, cur, entryBuf, (ulong)entryBuf.Length, out readLen) != 0 || readLen < 0x68)
				{
					break;
				}

				ulong next = BitConverter.ToUInt64(entryBuf, 0);
				ulong dllBase = BitConverter.ToUInt64(entryBuf, 0x30);
				ushort nameLen = BitConverter.ToUInt16(entryBuf, 0x58);
				ulong namePtr = BitConverter.ToUInt64(entryBuf, 0x60);

				if (dllBase != 0 && nameLen > 0 && nameLen <= 512 && namePtr != 0)
				{
					byte[] nameBytes = new byte[nameLen];
					if (NtWow64ReadVirtualMemory64(hProcess, namePtr, nameBytes, (ulong)nameBytes.Length, out readLen) == 0 && readLen == (ulong)nameBytes.Length)
					{
						string baseName = Encoding.Unicode.GetString(nameBytes);
						if (string.Equals(baseName, targetModuleName, StringComparison.OrdinalIgnoreCase))
						{
							imageBase = dllBase;
							Parse64BitSections(hProcess, imageBase, out imageSize, sections);
							return true;
						}
					}
				}

				cur = next;
			}
		}
		catch
		{
		}

		return false;
	}

	private static void Parse64BitSections(IntPtr hProcess, ulong imageBase, out int imageSize, List<SectionInfo> sections)
	{
		imageSize = 0;
		byte[] peBuf = new byte[0x1000];
		ulong readLen;
		if (NtWow64ReadVirtualMemory64(hProcess, imageBase, peBuf, (ulong)peBuf.Length, out readLen) == 0 && readLen >= 0x40)
		{
			uint e_lfanew = BitConverter.ToUInt32(peBuf, 0x3C);
			if (e_lfanew > 0 && e_lfanew < 0x800 && e_lfanew + 0x100 <= peBuf.Length)
			{
				uint sizeOfImage = BitConverter.ToUInt32(peBuf, (int)(e_lfanew + 0x18 + 0x38));
				imageSize = (int)sizeOfImage;

				ushort numberOfSections = BitConverter.ToUInt16(peBuf, (int)(e_lfanew + 4 + 2));
				ushort sizeOfOptionalHeader = BitConverter.ToUInt16(peBuf, (int)(e_lfanew + 4 + 16));
				int sectionTableOffset = (int)(e_lfanew + 4 + 20 + sizeOfOptionalHeader);

				for (int i = 0; i < numberOfSections; i++)
				{
					int secOff = sectionTableOffset + (i * 40);
					if (secOff + 40 <= peBuf.Length)
					{
						string secName = Encoding.ASCII.GetString(peBuf, secOff, 8).TrimEnd('\0');
						uint virtualSize = BitConverter.ToUInt32(peBuf, secOff + 8);
						uint virtualAddress = BitConverter.ToUInt32(peBuf, secOff + 12);
						sections.Add(new SectionInfo
						{
							Name = secName,
							VirtualAddress = imageBase + virtualAddress,
							VirtualSize = virtualSize
						});
					}
				}
			}
		}

		if (imageSize <= 0)
		{
			imageSize = 256 * 1024 * 1024;
		}
	}

	public static bool ReadMemory(IntPtr hProcess, bool is64BitTarget, ulong address, byte[] buffer, int length, out int bytesRead)
	{
		bytesRead = 0;
		if (length <= 0)
		{
			return true;
		}

		if (is64BitTarget && IntPtr.Size == 4)
		{
			ulong read;
			int status = NtWow64ReadVirtualMemory64(hProcess, address, buffer, (ulong)length, out read);
			bytesRead = (int)read;
			return status == 0 && bytesRead == length;
		}
		else
		{
			return ReadProcessMemory(hProcess, (IntPtr)(long)address, buffer, length, out bytesRead) && bytesRead == length;
		}
	}
}
