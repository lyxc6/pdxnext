using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Enums;

namespace PDX.SDK.Internal.Util;

internal class CancellationTokenRegister : ICancellationTokenRegister
{
	private readonly ILogger _logger;

	private ConcurrentDictionary<CancellationToken, CancellationTokenRegistration> _registrations = new ConcurrentDictionary<CancellationToken, CancellationTokenRegistration>();

	public CancellationTokenRegister(ILogger logger = null)
	{
		_logger = logger;
	}

	public CancellationToken GetToken(CancellationTokenRegistrationType type, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		_logger?.Log(string.Format("[{0}] Issuing a new cancellation token for type {1}", "GetToken", type), LogLevel.L0_Info, flowData2);
		CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		CancellationTokenRegistration cancellationTokenRegistration = new CancellationTokenRegistration
		{
			RegistrationType = type,
			TokenSource = cancellationTokenSource
		};
		_registrations.TryAdd(cancellationTokenSource.Token, cancellationTokenRegistration);
		return cancellationTokenRegistration.TokenSource.Token;
	}

	public void RemoveToken(CancellationToken token)
	{
		_registrations.TryRemove(token, out var _);
	}

	public void CancelAllTokensOfType(CancellationTokenRegistrationType type, FlowData flowData = null)
	{
		PerformCancel(_registrations.Values.Where((CancellationTokenRegistration x) => x.RegistrationType == type).ToList(), flowData);
	}

	public void CancelAllTokens(FlowData flowData = null)
	{
		PerformCancel(_registrations.Values.ToList(), flowData);
	}

	private void PerformCancel(List<CancellationTokenRegistration> registrations, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		_logger?.Log(string.Format("[{0}] About to cancel {1} token registrations", "PerformCancel", registrations.Count), LogLevel.L0_Info, flowData2);
		foreach (CancellationTokenRegistration registration in registrations)
		{
			try
			{
				registration.TokenSource.Cancel();
			}
			catch
			{
			}
			finally
			{
				RemoveToken(registration.TokenSource.Token);
			}
		}
	}
}
