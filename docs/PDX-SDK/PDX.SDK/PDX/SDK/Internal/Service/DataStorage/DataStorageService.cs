using PDX.SDK.Contracts.Service.DataStorage;
using PDX.SDK.Contracts.Service.DataStorage.CloudSave;
using PDX.SDK.Contracts.Service.DataStorage.KeyValue;
using PDX.SDK.Internal.Service.DataStorage.CloudSave;
using PDX.SDK.Internal.Service.DataStorage.KeyValue;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Service.DataStorage;

internal class DataStorageService : InternalService, IDataStorageService
{
	public IKeyValueService KeyValue { get; set; }

	public ICloudSaveService CloudSave { get; set; }

	public DataStorageService(Context context, IPathFactory pathFactory)
		: base("DataStorage", context)
	{
		KeyValue = new KeyValueService(context);
		CloudSave = new CloudSaveService(context, pathFactory);
	}
}
