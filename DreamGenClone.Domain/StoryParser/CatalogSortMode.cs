namespace DreamGenClone.Domain.StoryParser;

public enum CatalogSortMode
{
    NewestFirst = 0,
    UrlTitleAsc = 1
}

public enum ParseErrorMode
{
    FailFast = 0,
    PartialSuccess = 1
}

public enum ParseStatus
{
    Success = 0,
    PartialSuccess = 1,
    Failed = 2
}
