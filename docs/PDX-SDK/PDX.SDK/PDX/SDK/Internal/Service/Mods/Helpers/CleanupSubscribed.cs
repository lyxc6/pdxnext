using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Service.Mods.Helpers;

internal static class CleanupSubscribed
{
	internal static async Task<FileIOStatus> CleanupSubscribedModsFolder(Context context, string modsPath, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		context.Log("Mods.CleanupSubscribedModsFolder", LogLevel.L0_Info, flowData2);
		bool cleanupSuccess = true;
		OperationResult<List<string>> operationResult = await context.FileIO.ListDirectories(modsPath).ConfigureAwait(continueOnCapturedContext: false);
		if (!operationResult.Status.Success)
		{
			return operationResult.Status;
		}
		foreach (string item in operationResult.Result)
		{
			string[] array = new DirectoryInfo(item).Name.Split('_');
			int result = 0;
			if (array.Length != 2 || !int.TryParse(array[0], out result) || !int.TryParse(array[1], out result))
			{
				FileIOStatus fileIOStatus = await context.FileIO.DeleteDirectory(item, recursive: true).ConfigureAwait(continueOnCapturedContext: false);
				if (!fileIOStatus.Success)
				{
					context.Log(fileIOStatus.Message, LogLevel.L2_Warning);
				}
				cleanupSuccess &= fileIOStatus.Success;
			}
		}
		OperationResult<List<string>> operationResult2 = await context.FileIO.ListFiles(modsPath).ConfigureAwait(continueOnCapturedContext: false);
		if (!operationResult2.Status.Success)
		{
			return operationResult2.Status;
		}
		foreach (string item2 in operationResult2.Result)
		{
			FileIOStatus fileIOStatus2 = await context.FileIO.Delete(item2).ConfigureAwait(continueOnCapturedContext: false);
			if (!fileIOStatus2.Success)
			{
				context.Log(fileIOStatus2.Message, LogLevel.L2_Warning);
			}
			cleanupSuccess &= fileIOStatus2.Success;
		}
		return new FileIOStatus
		{
			Success = cleanupSuccess
		};
	}
}
