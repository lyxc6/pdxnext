using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Service.Telemetry.Models;
using PDX.SDK.Contracts.Service.Telemetry.Result;

namespace PDX.SDK.Contracts.Service.Telemetry;

public interface ITelemetryService
{
	bool IsEnabled { get; }

	Task<PDX.SDK.Contracts.Result> Exit();

	Task<SendResult> Send<T>(IList<T> events) where T : IEvent;

	Task<SendResult> Send<T>(T @event) where T : IEvent;

	Task<PDX.SDK.Contracts.Result> Start();
}
