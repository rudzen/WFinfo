namespace WFInfo.Domain;

public enum DataUpdateType
{
    Drop,
    Market
}

public sealed record DataUpdatedAt(
    string Date,
    DataUpdateType Type
);
