using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Contracts.Util;
using PDX.SDK.Internal.Enums;
using TinyJson;

namespace PDX.SDK.Internal.Util;

internal class FileIO : IFileIO
{
	private readonly Context _context;

	private readonly IPathFactory _pathFactory;

	private const string SessionIdFileName = "sessionId";

	private const string LegalFileName = "legal";

	private const string SessionDataFileName = "sessionData";

	private const string PlayerDataFileName = "playerData";

	private const string AchievementsMetadataFileName = "metadata";

	private const string AchievementsImagedataFileName = "imagedata";

	private const string DatabaseFileName = "database.json";

	public IDiskIO DiskIO { get; set; }

	public FileIO(Context context, IPathFactory pathFactory)
	{
		_context = context;
		_pathFactory = pathFactory;
		DiskIO = new DiskIODefaultWindows();
	}

	public Task<OperationResult<string>> ReadText(FilePath path, FlowData flowData = null)
	{
		string path2 = GetPath(path);
		return ReadText(path2, flowData);
	}

	public async Task<OperationResult<string>> ReadText(string path, FlowData flowData = null)
	{
		string result = null;
		AsyncReaderWriterLock readerWriterLock = Context.DiskFileLocks.FetchLock(path);
		await readerWriterLock.AcquireReaderLock().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using Stream stream = CreateReadStream(path, flowData);
			if (stream == null)
			{
				return new OperationResult<string>
				{
					Status = new FileIOStatus
					{
						Success = false,
						ErrorCode = DiskIOError.FileNotFound,
						Message = "Path does not exist"
					}
				};
			}
			using StreamReader sr = new StreamReader(stream);
			result = await sr.ReadToEndAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OutOfMemoryException ex)
		{
			_context.Log(ex.ToString(), LogLevel.L3_Error, flowData);
			return new OperationResult<string>
			{
				Status = new FileIOStatus
				{
					Success = false,
					ErrorCode = DiskIOError.OutOfSpace,
					Message = ex.Message
				}
			};
		}
		catch (IOException ex2)
		{
			_context.Log(ex2.ToString(), LogLevel.L3_Error, flowData);
			return new OperationResult<string>
			{
				Status = new FileIOStatus
				{
					Success = false,
					ErrorCode = DiskIOError.IOError,
					Message = ex2.Message
				}
			};
		}
		catch (Exception ex3)
		{
			_context.Log(ex3.ToString(), LogLevel.L2_Warning, flowData);
			return new OperationResult<string>
			{
				Status = new FileIOStatus
				{
					Success = false,
					ErrorCode = DiskIOError.Error,
					Message = ex3.Message
				}
			};
		}
		finally
		{
			if (DiskIO is DiskIODefaultWindows && (DiskIO as DiskIODefaultWindows).FileHandle != null)
			{
				(DiskIO as DiskIODefaultWindows).FileHandle.Dispose();
				(DiskIO as DiskIODefaultWindows).FileHandle = null;
			}
			readerWriterLock.ReleaseReaderLock();
		}
		return new OperationResult<string>
		{
			Status = new FileIOStatus
			{
				Success = true
			},
			Result = result
		};
	}

	public async Task<OperationResult<byte[]>> ReadBytes(string path, FlowData flowData = null)
	{
		byte[] result = null;
		AsyncReaderWriterLock readerWriterLock = Context.DiskFileLocks.FetchLock(path);
		await readerWriterLock.AcquireReaderLock().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using Stream stream = CreateReadStream(path, flowData);
			if (stream == null)
			{
				return new OperationResult<byte[]>
				{
					Status = new FileIOStatus
					{
						Success = false,
						ErrorCode = DiskIOError.FileNotFound,
						Message = "Path does not exist"
					}
				};
			}
			byte[] buffer = new byte[4096];
			using MemoryStream ms = new MemoryStream();
			int count;
			while ((count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(continueOnCapturedContext: false)) > 0)
			{
				await ms.WriteAsync(buffer, 0, count).ConfigureAwait(continueOnCapturedContext: false);
			}
			result = ms.ToArray();
		}
		catch (OutOfMemoryException ex)
		{
			_context.Log(ex.ToString(), LogLevel.L3_Error, flowData);
			return new OperationResult<byte[]>
			{
				Status = new FileIOStatus
				{
					Success = false,
					ErrorCode = DiskIOError.OutOfSpace,
					Message = ex.Message
				}
			};
		}
		catch (IOException ex2)
		{
			_context.Log(ex2.ToString(), LogLevel.L3_Error, flowData);
			return new OperationResult<byte[]>
			{
				Status = new FileIOStatus
				{
					Success = false,
					ErrorCode = DiskIOError.IOError,
					Message = ex2.Message
				}
			};
		}
		catch (Exception ex3)
		{
			_context.Log(ex3.ToString(), LogLevel.L2_Warning, flowData);
			return new OperationResult<byte[]>
			{
				Status = new FileIOStatus
				{
					Success = false,
					ErrorCode = DiskIOError.Error,
					Message = ex3.Message
				}
			};
		}
		finally
		{
			if (DiskIO is DiskIODefaultWindows && (DiskIO as DiskIODefaultWindows).FileHandle != null)
			{
				(DiskIO as DiskIODefaultWindows).FileHandle.Dispose();
				(DiskIO as DiskIODefaultWindows).FileHandle = null;
			}
			readerWriterLock.ReleaseReaderLock();
		}
		return new OperationResult<byte[]>
		{
			Status = new FileIOStatus
			{
				Success = true
			},
			Result = result
		};
	}

	public Task<OperationResult<T>> ReadJSON<T>(FilePath path, FlowData flowData = null)
	{
		string path2 = GetPath(path);
		return InternalReadJSON<T>(path2, flowData);
	}

	public Task<OperationResult<T>> ReadJSON<T>(string path, FlowData flowData = null)
	{
		string path2 = GetPath(path);
		return InternalReadJSON<T>(path2, flowData);
	}

	public async Task<FileIOStatus> Write(string path, Stream data, FlowData flowData = null)
	{
		_context.Log(string.Format(Defaults.Culture, "Writing file '{0}' to disk", path), LogLevel.L1_Debug, flowData);
		AsyncReaderWriterLock readerWriterLock = Context.DiskFileLocks.FetchLock(path);
		await readerWriterLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using Stream fs = CreateWriteStream(path);
			if (data.CanSeek)
			{
				data.Position = 0L;
			}
			await data.CopyToAsync(fs).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (IOException ex)
		{
			DiskIOError errorCode = DiskIOError.IOError;
			int num = -2147024857;
			int num2 = -2147024784;
			if (ex.HResult == num || ex.HResult == num2)
			{
				errorCode = DiskIOError.OutOfSpace;
			}
			_context.Log(ex.ToString(), LogLevel.L3_Error);
			return new FileIOStatus
			{
				Success = false,
				ErrorCode = errorCode,
				Message = ex.Message
			};
		}
		catch (Exception ex2)
		{
			_context.Log(ex2.ToString(), LogLevel.L3_Error);
			DiskIOError errorCode2 = DiskIOError.Error;
			if (ex2 is NotSupportedException || ex2 is SecurityException || ex2 is UnauthorizedAccessException)
			{
				errorCode2 = DiskIOError.NotAllowed;
			}
			return new FileIOStatus
			{
				Success = false,
				ErrorCode = errorCode2,
				Message = ex2.Message
			};
		}
		finally
		{
			if (DiskIO is DiskIODefaultWindows && (DiskIO as DiskIODefaultWindows).FileHandle != null)
			{
				(DiskIO as DiskIODefaultWindows).FileHandle.Dispose();
				(DiskIO as DiskIODefaultWindows).FileHandle = null;
			}
			readerWriterLock.ReleaseWriterLock();
		}
		return new FileIOStatus
		{
			Success = true
		};
	}

	public Task<FileIOStatus> Write(FilePath path, string data, FlowData flowData = null)
	{
		return Write(GetPath(path), new MemoryStream(Encoding.UTF8.GetBytes(data)), flowData);
	}

	public Task<FileIOStatus> WriteJSON(FilePath path, object data, FlowData flowData = null)
	{
		return Write(GetPath(path), new MemoryStream(Encoding.UTF8.GetBytes(data.ToJson())), flowData);
	}

	public Task<FileIOStatus> WriteJSON(string path, object data, FlowData flowData = null)
	{
		return Write(path, new MemoryStream(Encoding.UTF8.GetBytes(data.ToJson())), flowData);
	}

	public async Task<FileIOStatus> Append(string path, Stream data, FlowData flowData = null)
	{
		_context.Log(string.Format(Defaults.Culture, "Appending to file '{0}' on disk", path), LogLevel.L1_Debug, flowData);
		AsyncReaderWriterLock readerWriterLock = Context.DiskFileLocks.FetchLock(path);
		await readerWriterLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using Stream fs = CreateAppendStream(path);
			data.Seek(0L, SeekOrigin.Begin);
			await data.CopyToAsync(fs).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (IOException ex)
		{
			DiskIOError errorCode = DiskIOError.IOError;
			int num = -2147024857;
			int num2 = -2147024784;
			if (ex.HResult == num || ex.HResult == num2)
			{
				errorCode = DiskIOError.OutOfSpace;
			}
			_context.Log(ex.ToString(), LogLevel.L3_Error);
			return new FileIOStatus
			{
				Success = false,
				ErrorCode = errorCode,
				Message = ex.Message
			};
		}
		catch (Exception ex2)
		{
			_context.Log(ex2.ToString(), LogLevel.L3_Error);
			DiskIOError errorCode2 = DiskIOError.Error;
			if (ex2 is NotSupportedException || ex2 is SecurityException || ex2 is UnauthorizedAccessException)
			{
				errorCode2 = DiskIOError.NotAllowed;
			}
			return new FileIOStatus
			{
				Success = false,
				ErrorCode = errorCode2,
				Message = ex2.Message
			};
		}
		finally
		{
			if (DiskIO is DiskIODefaultWindows && (DiskIO as DiskIODefaultWindows).FileHandle != null)
			{
				(DiskIO as DiskIODefaultWindows).FileHandle.Dispose();
				(DiskIO as DiskIODefaultWindows).FileHandle = null;
			}
			readerWriterLock.ReleaseWriterLock();
		}
		return new FileIOStatus
		{
			Success = true
		};
	}

	public Task<FileIOStatus> Append(FilePath path, string data, FlowData flowData = null)
	{
		return Append(GetPath(path), new MemoryStream(Encoding.UTF8.GetBytes(data)), flowData);
	}

	public Task<FileIOStatus> Delete(FilePath path)
	{
		return Delete(GetPath(path));
	}

	public Task<FileIOStatus> Delete(string path)
	{
		return PerformDiskOperationAndCatch(delegate
		{
			DiskIO.Delete(path);
		}, path);
	}

	public Task<FileIOStatus> DeleteDirectory(string path, bool recursive = false)
	{
		return PerformDiskOperationAndCatch(delegate
		{
			DiskIO.DeleteDirectory(path, recursive);
		}, path);
	}

	public Task<FileIOStatus> CreateDirectory(string path)
	{
		return PerformDiskOperationAndCatch(delegate
		{
			DiskIO.CreateDirectory(path);
		}, path);
	}

	public Task<OperationResult<List<string>>> ListDirectories(string path)
	{
		return PerformDiskOperationAndCatch(() => DiskIO.ListDirectories(path) ?? new List<string>(), null, new List<string>());
	}

	public Task<OperationResult<List<string>>> ListFiles(string path)
	{
		return PerformDiskOperationAndCatch(() => DiskIO.ListFiles(path) ?? new List<string>(), null, new List<string>());
	}

	public Task<FileIOStatus> Move(string sourcePath, string targetPath)
	{
		return PerformDiskOperationAndCatch(delegate
		{
			DiskIO.Move(sourcePath, targetPath);
		}, sourcePath);
	}

	private async Task<FileIOStatus> PerformDiskOperationAndCatch(Action action, string lockPath = null)
	{
		return (await PerformDiskOperationAndCatch(delegate
		{
			action();
			return (object)null;
		}, lockPath).ConfigureAwait(continueOnCapturedContext: false)).Status;
	}

	private async Task<OperationResult<T>> PerformDiskOperationAndCatch<T>(Func<T> func, string lockPath = null, T defaultValue = default(T))
	{
		FileIOStatus status = new FileIOStatus
		{
			Success = false
		};
		AsyncReaderWriterLock readerWriterLock = null;
		T returnValue = defaultValue;
		if (lockPath != null)
		{
			readerWriterLock = Context.DiskFileLocks.FetchLock(lockPath);
			await readerWriterLock.AcquireReaderLock().ConfigureAwait(continueOnCapturedContext: false);
		}
		try
		{
			returnValue = func();
			status.Success = true;
		}
		catch (IOException ex)
		{
			_context.Log(ex.ToString(), LogLevel.L3_Error);
			status.ErrorCode = DiskIOError.IOError;
			status.Message = ex.Message;
		}
		catch (UnauthorizedAccessException ex2)
		{
			_context.Log(ex2.ToString(), LogLevel.L3_Error);
			status.ErrorCode = DiskIOError.NotAllowed;
			status.Message = ex2.Message;
		}
		catch (Exception ex3)
		{
			_context.Log(ex3.ToString(), LogLevel.L2_Warning);
			status.ErrorCode = DiskIOError.Error;
			status.Message = ex3.Message;
		}
		finally
		{
			readerWriterLock?.ReleaseReaderLock();
		}
		return new OperationResult<T>
		{
			Status = status,
			Result = returnValue
		};
	}

	public bool PathExists(string path)
	{
		return DiskIO.PathExists(path);
	}

	public bool PathExists(FilePath path)
	{
		return DiskIO.PathExists(GetPath(path));
	}

	public string GetPath(FilePath path)
	{
		return path switch
		{
			FilePath.None => throw new Exception("No valid path has been set! path: FilePath.None"), 
			FilePath.SessionId => GetPath("sessionId"), 
			FilePath.LegalCache => GetPath("legal"), 
			FilePath.SessionData => GetPath("sessionData"), 
			FilePath.PlayerData => GetPath("playerData"), 
			FilePath.AchievementsMetadata => _pathFactory.GetAchievementsPath("metadata"), 
			FilePath.AchievementsImagedata => _pathFactory.GetAchievementsPath("imagedata"), 
			FilePath.StateCache => GetPath("database.json"), 
			_ => GetPath("default"), 
		};
	}

	internal Stream CreateReadStream(string path, FlowData flowData = null)
	{
		if (!DiskIO.PathExists(path))
		{
			return null;
		}
		_context.Log(string.Format(Defaults.Culture, "Reading file '{0}' from disk", path), LogLevel.L1_Debug, flowData);
		return DiskIO.CreateFileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
	}

	internal Stream CreateAppendStream(string path, FlowData flowData = null)
	{
		if (!DiskIO.PathExists(path))
		{
			return null;
		}
		return DiskIO.CreateFileStream(path, FileMode.Append, FileAccess.Write, FileShare.Write);
	}

	internal Stream CreateWriteStream(string path, FlowData flowData = null)
	{
		string directoryName = DiskIO.GetDirectoryName(path);
		if (!DiskIO.PathExists(directoryName))
		{
			DiskIO.CreateDirectory(directoryName);
		}
		return DiskIO.CreateFileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write);
	}

	internal Stream CreateReadWriteStream(string path, FlowData flowData = null)
	{
		string directoryName = DiskIO.GetDirectoryName(path);
		if (!DiskIO.PathExists(directoryName))
		{
			DiskIO.CreateDirectory(directoryName);
		}
		return DiskIO.CreateFileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
	}

	private string GetPath(params string[] path)
	{
		List<string> list = path.Where((string s) => !string.IsNullOrEmpty(s)).ToList();
		return _pathFactory.GetPath(Path.Combine(list.ToArray()));
	}

	private async Task<OperationResult<T>> InternalReadJSON<T>(string fullPath, FlowData flowData = null)
	{
		OperationResult<string> operationResult = await ReadText(fullPath, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!operationResult.Status.Success || operationResult.Result == null || operationResult.Result == string.Empty)
		{
			return new OperationResult<T>
			{
				Status = operationResult.Status,
				Result = default(T)
			};
		}
		try
		{
			return new OperationResult<T>
			{
				Status = new FileIOStatus
				{
					Success = true
				},
				Result = operationResult.Result.FromJson<T>()
			};
		}
		catch (Exception ex)
		{
			return new OperationResult<T>
			{
				Status = new FileIOStatus
				{
					Success = false,
					ErrorCode = DiskIOError.Error,
					Message = ex.Message
				},
				Result = default(T)
			};
		}
	}
}
