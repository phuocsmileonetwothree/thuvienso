namespace thuvienso.DTOs
{
    public class DocumentFilterParams
    {
        public string? Search { get; set; }
        public int? CategoryId { get; set; }
        public int? PublisherId { get; set; }
        public int? AuthorId { get; set; }
        public string? Sort { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 12;
    }
}
