using System.Threading;
using PDX.SDK.Internal.Enums;

namespace PDX.SDK.Internal.Util;

internal class CancellationTokenRegistration
{
	public CancellationTokenRegistrationType RegistrationType { get; set; }

	public CancellationTokenSource TokenSource { get; set; }
}
