using Mediator;
using Newtonsoft.Json.Linq;

namespace WFInfo.Domain;

public sealed record DataResponse(JObject? Data);

public sealed record DataRequest(DataTypes Type) : IRequest<DataResponse>;
