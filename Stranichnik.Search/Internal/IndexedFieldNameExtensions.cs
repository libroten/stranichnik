namespace Stranichnik.Search.Internal;

internal static class IndexedFieldNameExtensions
{
    public static string ToResultFieldName(this IndexedFieldName field)
    {
        return field switch
        {
            IndexedFieldName.Title => "title",
            IndexedFieldName.UrlHost => "url_host",
            IndexedFieldName.UrlDomainParts => "url_domain_parts",
            IndexedFieldName.UrlPathParts => "url_path_parts",
            IndexedFieldName.UrlText => "url_text",
            IndexedFieldName.Tags => "tags",
            IndexedFieldName.Notes => "notes",
            _ => field.ToString()
        };
    }
}
