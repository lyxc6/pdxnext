using System.Collections.Generic;

namespace PDX.SDK.Internal.Util;

public class FileLocks
{
	private static object AccessList = new object();

	private static Dictionary<string, AsyncReaderWriterLock> FileLockList = null;

	public FileLocks()
	{
		FileLockList = new Dictionary<string, AsyncReaderWriterLock>();
	}

	public AsyncReaderWriterLock FetchLock(string absolutePath)
	{
		AsyncReaderWriterLock value = null;
		lock (AccessList)
		{
			if (!FileLockList.TryGetValue(absolutePath, out value))
			{
				value = new AsyncReaderWriterLock();
				FileLockList.Add(absolutePath, value);
			}
		}
		return value;
	}
}
