using System.Text.RegularExpressions;
using MermaidServer.Models;

namespace MermaidServer.Services;

public class MermaidParser
{
    private readonly ILogger<MermaidParser> _logger;

    public MermaidParser(ILogger<MermaidParser> logger)
    {
        _logger = logger;
    }

    public async Task<Schema> ParseAsync(string mermaidContent)
    {
        return await Task.Run(() => Parse(mermaidContent));
    }

    public Schema Parse(string mermaidContent)
    {
        _logger.LogInformation("Parse started");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var schema = new Schema();
        var lines = mermaidContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        Entity? currentEntity = null;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Skip diagram declaration and comments
            if (trimmed.StartsWith("erDiagram") || trimmed.StartsWith("%%"))
                continue;
            
            // Entity declaration (e.g., "USER {")
            if (trimmed.EndsWith("{") && !trimmed.Contains("||"))
            {
                var entityName = trimmed.Replace("{", "").Trim();
                currentEntity = new Entity { Name = entityName };
                schema.Entities.Add(currentEntity);
                continue;
            }
            
            // End of entity
            if (trimmed == "}")
            {
                currentEntity = null;
                continue;
            }
            
            // Attribute line (e.g., "int user_id PK")
            if (currentEntity != null)
            {
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var attr = new EntityAttribute
                    {
                        DataType = parts[0],
                        Name = parts[1],
                        IsPrimaryKey = parts.Length > 2 && parts[2].Contains("PK"),
                        IsForeignKey = parts.Length > 2 && parts[2].Contains("FK")
                    };
                    currentEntity.Attributes.Add(attr);
                }
                continue;
            }
            
            // Relationship line (e.g., "USER ||--o{ POST : "writes"")
            if (trimmed.Contains("--") && trimmed.Contains(":"))
            {
                var colonIndex = trimmed.IndexOf(':');
                var relationshipPart = trimmed.Substring(0, colonIndex).Trim();
                var label = trimmed.Substring(colonIndex + 1).Trim().Trim('"');
                
                // Split on -- to get left and right sides
                var sides = relationshipPart.Split("--");
                if (sides.Length == 2)
                {
                    var leftSide = sides[0].Trim();
                    var rightSide = sides[1].Trim();
                    
                    // Extract entity names (word characters before/after cardinality symbols)
                    var fromMatch = System.Text.RegularExpressions.Regex.Match(leftSide, @"(\w+)\s*([\|\}o\{]+)$");
                    var toMatch = System.Text.RegularExpressions.Regex.Match(rightSide, @"^([\|\}o\{]+)\s*(\w+)");
                    
                    if (fromMatch.Success && toMatch.Success)
                    {
                        schema.Relationships.Add(new Relationship
                        {
                            FromEntity = fromMatch.Groups[1].Value,
                            FromCardinality = fromMatch.Groups[2].Value,
                            ToCardinality = toMatch.Groups[1].Value,
                            ToEntity = toMatch.Groups[2].Value,
                            Label = label
                        });
                    }
                }
            }
        }
        
        sw.Stop();
        _logger.LogInformation("Parse completed in {ElapsedMs}ms. Found {EntityCount} entities and {RelationshipCount} relationships", 
            sw.ElapsedMilliseconds, schema.Entities.Count, schema.Relationships.Count);
        return schema;
    }
}
