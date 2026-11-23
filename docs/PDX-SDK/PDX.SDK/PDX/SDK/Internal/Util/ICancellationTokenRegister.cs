using System.Threading;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Enums;

namespace PDX.SDK.Internal.Util;

public interface ICancellationTokenRegister
{
	CancellationToken GetToken(CancellationTokenRegistrationType type, FlowData flowData = null);

	void RemoveToken(CancellationToken token);

	void CancelAllTokensOfType(CancellationTokenRegistrationType type, FlowData flowData = null);

	void CancelAllTokens(FlowData flowData = null);
}
