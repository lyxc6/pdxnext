using PDX.SDK.Contracts.Service.DataStorage.CloudSave;
using PDX.SDK.Contracts.Service.DataStorage.KeyValue;

namespace PDX.SDK.Contracts.Service.DataStorage;

public interface IDataStorageService
{
	ICloudSaveService CloudSave { get; set; }

	IKeyValueService KeyValue { get; set; }
}
