using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PDX.SDK.Contracts.Exceptions;

namespace PDX.SDK.Internal.Platypatch.Models;

internal class FolderManifest
{
	private const string NamePrefix = "Name:";

	private const string HashPrefix = "Hash:";

	public string RepoName { get; private set; }

	public string HashAlgorithm { get; private set; }

	public ManifestFileEntry[] FileEntries { get; private set; }

	public string RemoteFolderPath { get; }

	public string Version { get; }

	public string RawContents { get; }

	public FolderManifest(string remoteFolderPath, string contents, string version)
	{
		RemoteFolderPath = remoteFolderPath;
		Version = version;
		RawContents = contents;
		ParseContent(contents);
	}

	private void ParseContent(string contents)
	{
		List<ManifestFileEntry> list = new List<ManifestFileEntry>();
		string[] array = contents.Split('\n');
		foreach (string text in array)
		{
			if (!string.IsNullOrEmpty(text))
			{
				if (text.StartsWith("Name:"))
				{
					RepoName = ParseValueLine(text);
				}
				else if (text.StartsWith("Hash:"))
				{
					HashAlgorithm = ParseValueLine(text);
				}
				else
				{
					list.Add(ParseEntryLine(text));
				}
			}
		}
		FileEntries = list.ToArray();
	}

	private string ParseValueLine(string line)
	{
		string text = line.Substring(line.IndexOf(':') + 1).Trim();
		if (string.IsNullOrEmpty(text))
		{
			throw new PlatypatchException("Missing value after parsing key value line '" + line + "'");
		}
		return text;
	}

	private ManifestFileEntry ParseEntryLine(string line)
	{
		MatchCollection matchCollection = new Regex("((?<=\")[^\"]*(?=\"(,|$)+)|(?<=,|^)[^,\"]*(?=,|$))").Matches(line.Trim());
		if (matchCollection.Count < 4)
		{
			throw new PlatypatchException("Encountered bad manifest file entry line " + line);
		}
		ManifestFileEntry manifestFileEntry = new ManifestFileEntry();
		manifestFileEntry.RelativePath = ParseRelativePath(line, matchCollection[0].Value);
		manifestFileEntry.Size = ParseSize(line, matchCollection[1].Value);
		manifestFileEntry.Hash = ParseHash(line, matchCollection[2].Value);
		manifestFileEntry.Flags = ParseFlags(line, matchCollection[3].Value);
		if (matchCollection.Count == 6)
		{
			manifestFileEntry.CompressedSize = ParseSize(line, matchCollection[4].Value);
			manifestFileEntry.CompressionType = ParseCompressionType(line, matchCollection[5].Value);
		}
		return manifestFileEntry;
	}

	private string ParseCompressionType(string line, string compressionType)
	{
		if (string.IsNullOrEmpty(compressionType))
		{
			throw new PlatypatchException("Encountered bad compression type (" + compressionType + ") when parsing manifest entry line '" + line + "'");
		}
		return compressionType;
	}

	private byte[] ParseHash(string line, string base64Hash)
	{
		try
		{
			if (base64Hash.Contains("+") || base64Hash.Contains("/"))
			{
				return Convert.FromBase64String(base64Hash);
			}
			base64Hash = base64Hash.Replace('-', '+').Replace('_', '/');
			return Convert.FromBase64String(base64Hash);
		}
		catch (Exception innerException)
		{
			throw new PlatypatchException("Encountered bad hash (" + base64Hash + ") when parsing manifest entry line '" + line + "'", innerException);
		}
	}

	private static string ParseRelativePath(string line, string relativePath)
	{
		if (string.IsNullOrEmpty(relativePath))
		{
			throw new PlatypatchException("Encountered bad relative path (" + relativePath + ") when parsing manifest entry line '" + line + "'");
		}
		return relativePath.Replace('\\', Path.DirectorySeparatorChar);
	}

	private static long ParseSize(string line, string rawSize)
	{
		if (string.IsNullOrEmpty(rawSize) || !long.TryParse(rawSize, out var result) || result < 0)
		{
			throw new PlatypatchException("Encountered bad size (" + rawSize + ") when parsing manifest entry line '" + line + "'");
		}
		return result;
	}

	private ManifestFileEntryFlags ParseFlags(string line, string stringFlags)
	{
		if (string.IsNullOrEmpty(stringFlags))
		{
			throw new PlatypatchException("Encountered bad flags (" + stringFlags + ") when parsing manifest entry line '" + line + "'");
		}
		ManifestFileEntryFlags manifestFileEntryFlags = ManifestFileEntryFlags.None;
		foreach (char c in stringFlags)
		{
			manifestFileEntryFlags = c switch
			{
				'd' => manifestFileEntryFlags | ManifestFileEntryFlags.Directory, 
				'r' => manifestFileEntryFlags | ManifestFileEntryFlags.Readable, 
				'w' => manifestFileEntryFlags | ManifestFileEntryFlags.Writable, 
				'x' => manifestFileEntryFlags | ManifestFileEntryFlags.Executable, 
				'c' => manifestFileEntryFlags | ManifestFileEntryFlags.Compressed, 
				's' => manifestFileEntryFlags | ManifestFileEntryFlags.Symlink, 
				_ => throw new PlatypatchException($"Encountered unknown flag ('{c}') when parsing flags ('{stringFlags}') in manifest entry line '{line}'"), 
			};
		}
		return manifestFileEntryFlags;
	}
}
