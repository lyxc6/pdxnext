using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.Legal.Models;
using PDX.SDK.Contracts.Service.Legal.Result;

namespace PDX.SDK.Contracts.Service.Legal;

public interface ILegalService
{
	Task<GetDocumentResult> DownloadDocument(string title, string type, Language? lang = null);

	Task<GetAllDocumentResult> GetNotices(Language? lang = null);

	Task<GetDocumentResult> GetPrivacy(Language? lang = null);

	Task<GetDocumentResult> GetEula(Language? lang = null);

	Task<ListUnviewedResult> ListUnviewed();

	Task<PDX.SDK.Contracts.Result> MarkAsViewed(Document document);

	Task<PDX.SDK.Contracts.Result> MarkAsViewed(string title, string type);
}
