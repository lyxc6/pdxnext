using System.IO;

namespace PDX.SDK.Internal.Platypatch.Models;

internal class PatchOperation
{
	public PatchOperationType Type { get; private set; }

	public string RelativePath { get; private set; }

	public PatchOperation(string operationsLine)
	{
		string[] array = operationsLine.Split(':');
		if (array.Length == 2)
		{
			Type = ParseType(array[0].Trim());
			RelativePath = array[1].Replace('\\', Path.DirectorySeparatorChar).Trim();
		}
	}

	private PatchOperationType ParseType(string code)
	{
		return code switch
		{
			"n" => PatchOperationType.New, 
			"p" => PatchOperationType.Patch, 
			"d" => PatchOperationType.Delete, 
			"c" => PatchOperationType.CreateDirectory, 
			"s" => PatchOperationType.Symlink, 
			"k" => PatchOperationType.UpdateFlags, 
			_ => PatchOperationType.Unknown, 
		};
	}
}
