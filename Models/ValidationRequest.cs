using Newtonsoft.Json;

namespace DocumentValidator.Models
{
    public class ValidationRequest
    {
        [JsonProperty("organizationname")]
        public string OrganizationName { get; set; } = string.Empty;

        [JsonProperty("FEIN")]
        public string Fein { get; set; } = string.Empty;

        [JsonProperty("files")]
        public List<FileData> Files { get; set; } = new List<FileData>();
    }

    public class FileData
    {
        [JsonProperty("FileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonProperty("FileContentBase64")]
        public string FileContentBase64 { get; set; } = string.Empty;
    }
} 