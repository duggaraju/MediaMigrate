namespace MediaMigrate.Contracts
{
    /// <summary>
    /// Options for querying Azure Media Services entities using ODATA.
    /// </summary>
    public record QueryOptions
    {
        public DateTime? CreatedAfter { get; set; }

        public DateTime? CreatedBefore { get; set; }

        public string[]? Entities { get; set; }

        public string? Filter { get; set; }

        public string? GetFilter()
        {
            string? filter = null;
            if (Entities != null)
            {
                filter = string.Join(" or ", Entities.Select(n => $"name eq '{n}'"));
            }
            else
            {
                if (CreatedAfter != null)
                {
                    filter = $"options/created gt {CreatedAfter:T}";
                }

                if (CreatedBefore != null)
                {
                    filter = filter == null ? string.Empty : $"{filter} and ";
                    filter += $"options/created lt {CreatedBefore:T}";
                }
            }
            return filter ?? Filter;
        }
    }

    public class StorageQueryOptions
    {
        public string ContainerPrefix { get; set; } = "asset-";
    }
}
