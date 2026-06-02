namespace thuvienso.DTOs
{
    public class DocumentFilterResponse<T>
    {
        public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
    }
}
