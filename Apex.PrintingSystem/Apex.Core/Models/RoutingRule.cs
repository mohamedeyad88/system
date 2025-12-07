namespace Apex.Core.Models
{
    public class RoutingRule
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Priority { get; set; } = 0;
        public string ConditionsJson { get; set; } = "{}";
        public string ActionsJson { get; set; } = "{}";

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? TargetPrinter
        {
            get
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(ActionsJson);
                    if (doc.RootElement.TryGetProperty("TargetPrinter", out var prop))
                        return prop.GetString();
                }
                catch { }
                return null;
            }
        }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? TargetPool
        {
            get
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(ActionsJson);
                    if (doc.RootElement.TryGetProperty("TargetPool", out var prop))
                        return prop.GetString();
                }
                catch { }
                return null;
            }
        }
    }
}
