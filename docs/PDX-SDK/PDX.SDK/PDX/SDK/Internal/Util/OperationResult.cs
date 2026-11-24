namespace PDX.SDK.Internal.Util;

internal class OperationResult<T>
{
	public FileIOStatus Status { get; set; }

	public T Result { get; set; }
}
