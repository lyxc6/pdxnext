using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Util;
using PDX.SDK.Internal.Enums;

namespace PDX.SDK.Internal.Util;

[Preserve]
internal interface IFileIO
{
	IDiskIO DiskIO { get; set; }

	Task<OperationResult<string>> ReadText(FilePath path, FlowData flowData = null);

	Task<OperationResult<string>> ReadText(string path, FlowData flowData = null);

	Task<OperationResult<byte[]>> ReadBytes(string path, FlowData flowData = null);

	Task<OperationResult<T>> ReadJSON<T>(FilePath path, FlowData flowData = null);

	Task<OperationResult<T>> ReadJSON<T>(string path, FlowData flowData = null);

	Task<FileIOStatus> Write(string path, Stream data, FlowData flowData = null);

	Task<FileIOStatus> Write(FilePath path, string data, FlowData flowData = null);

	Task<FileIOStatus> WriteJSON(FilePath path, object data, FlowData flowData = null);

	Task<FileIOStatus> WriteJSON(string path, object data, FlowData flowData = null);

	Task<FileIOStatus> Append(string path, Stream data, FlowData flowData = null);

	Task<FileIOStatus> Append(FilePath path, string data, FlowData flowData = null);

	Task<FileIOStatus> Delete(FilePath path);

	Task<FileIOStatus> Delete(string path);

	Task<FileIOStatus> DeleteDirectory(string path, bool recursive = false);

	Task<FileIOStatus> CreateDirectory(string path);

	Task<OperationResult<List<string>>> ListDirectories(string path);

	Task<OperationResult<List<string>>> ListFiles(string path);

	Task<FileIOStatus> Move(string sourcePath, string targetPath);

	bool PathExists(string path);

	bool PathExists(FilePath path);

	string GetPath(FilePath path);
}
