using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Telemetry.Models;

namespace PDX.SDK.Contracts.Service.Telemetry;

public interface IBuilder<T> where T : IEvent
{
	T AddEvent(string name);

	List<T> Build();
}
