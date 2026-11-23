using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Service.DataStorage.KeyValue.Models;
using PDX.SDK.Contracts.Service.DataStorage.KeyValue.Result;

namespace PDX.SDK.Contracts.Service.DataStorage.KeyValue;

public interface IKeyValueService
{
	Task<PDX.SDK.Contracts.Result> Delete(Entry entry);

	Task<PDX.SDK.Contracts.Result> Delete(IEnumerable<Entry> entries);

	Task<PDX.SDK.Contracts.Result> Delete(IEnumerable<string> keys);

	Task<PDX.SDK.Contracts.Result> Delete(params string[] keys);

	Task<PDX.SDK.Contracts.Result> Delete(string key);

	Task<PDX.SDK.Contracts.Result> DeleteAll();

	Task<GetAllResult> Get(IEnumerable<string> keys);

	Task<GetAllResult> Get(params string[] keys);

	Task<GetResult> Get(string key);

	Task<GetAllResult> GetAll();

	Task<PDX.SDK.Contracts.Result> Set(Dictionary<string, string> keyValuePairs);

	Task<PDX.SDK.Contracts.Result> Set(Entry entry);

	Task<PDX.SDK.Contracts.Result> Set(IEnumerable<Entry> entries);

	Task<PDX.SDK.Contracts.Result> Set(string key, string value);
}
