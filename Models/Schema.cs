namespace MermaidServer.Models;

public class Schema
{
    public List<Entity> Entities { get; set; } = new();
    public List<Relationship> Relationships { get; set; } = new();
}

public class Entity
{
    public string Name { get; set; } = string.Empty;
    public List<EntityAttribute> Attributes { get; set; } = new();
}

public class EntityAttribute
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
}

public class Relationship
{
    public string FromEntity { get; set; } = string.Empty;
    public string ToEntity { get; set; } = string.Empty;
    public string FromCardinality { get; set; } = string.Empty;
    public string ToCardinality { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
