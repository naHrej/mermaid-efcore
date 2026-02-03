using System.Text;
using MermaidServer.Models;

namespace MermaidServer.Services;

public class CSharpGenerator
{
    private readonly ILogger<CSharpGenerator> _logger;

    public CSharpGenerator(ILogger<CSharpGenerator> logger)
    {
        _logger = logger;
    }

    public async Task<string> GenerateEntitiesAsync(Schema schema)
    {
        return await Task.Run(() => GenerateEntities(schema));
    }

    public string GenerateEntities(Schema schema)
    {
        _logger.LogInformation("GenerateEntities started with {EntityCount} entities", schema.Entities.Count);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sb = new StringBuilder();

        var entityNameMap = BuildEntityNameMap(schema);
        
        foreach (var entity in schema.Entities)
        {
            var isOwned = IsOwnedEntity(schema, entity);

            var className = entityNameMap[entity.Name];
            sb.AppendLine($"public class {className}");
            sb.AppendLine("{");

            var usedPropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Generate properties
            foreach (var attr in entity.Attributes)
            {
                var csharpType = MapToCSharpType(attr.DataType);
                var propName = EnsureUniquePropertyName(ToPascalCase(attr.Name), usedPropertyNames);
                
                // FKs and non-PK properties can be nullable
                var isNullable = attr.IsForeignKey || (!attr.IsPrimaryKey && csharpType != "string");
                if (csharpType == "string")
                {
                    var stringType = isNullable ? "string?" : "required string";
                    sb.AppendLine($"    public {stringType} {propName} {{ get; set; }}");
                }
                else
                {
                    var nullableMarker = isNullable ? "?" : "";
                    sb.AppendLine($"    public {csharpType}{nullableMarker} {propName} {{ get; set; }}");
                }
            }
            
            // Add navigation properties based on relationships
            var outgoingRelations = schema.Relationships
                .Where(r => r.FromEntity == entity.Name && r.ToCardinality.Contains("{"))
                .GroupBy(r => r.ToEntity, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var ownedOutgoing = schema.Relationships
                .Where(r => r.FromEntity == entity.Name &&
                       schema.Entities.Any(e => e.Name == r.ToEntity && IsOwnedEntity(schema, e)))
                .GroupBy(r => r.ToEntity, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            
            var incomingOneToOne = schema.Relationships
                .Where(r => r.ToEntity == entity.Name && 
                       r.ToCardinality.Contains("||") &&
                       !IsOwnedEntity(schema, schema.Entities.First(e => e.Name == r.ToEntity)))
                .GroupBy(r => r.FromEntity, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            
            var incomingOptional = schema.Relationships
                .Where(r => r.ToEntity == entity.Name && 
                       r.ToCardinality.Contains("o") &&
                       !r.ToCardinality.Contains("{"))
                .GroupBy(r => r.FromEntity, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            
            if (outgoingRelations.Any() || ownedOutgoing.Any() || incomingOneToOne.Any() || incomingOptional.Any())
            {
                sb.AppendLine();
                sb.AppendLine("    // Navigation properties");
            }
            
            foreach (var rel in outgoingRelations)
            {
                var navType = entityNameMap[rel.ToEntity];
                var navProp = EnsureUniquePropertyName(Pluralize(navType), usedPropertyNames);
                sb.AppendLine($"    public ICollection<{navType}> {navProp} {{ get; set; }} = new List<{navType}>();");
            }

            foreach (var rel in ownedOutgoing)
            {
                var navType = entityNameMap[rel.ToEntity];
                var navProp = EnsureUniquePropertyName(navType, usedPropertyNames);
                sb.AppendLine($"    public {navType}? {navProp} {{ get; set; }}");
            }
            
            foreach (var rel in incomingOneToOne)
            {
                var navType = entityNameMap[rel.FromEntity];
                var navProp = EnsureUniquePropertyName(navType, usedPropertyNames);
                sb.AppendLine($"    public {navType}? {navProp} {{ get; set; }}");
            }
            
            foreach (var rel in incomingOptional)
            {
                var navType = entityNameMap[rel.FromEntity];
                var navProp = EnsureUniquePropertyName(Pluralize(navType), usedPropertyNames);
                sb.AppendLine($"    public ICollection<{navType}> {navProp} {{ get; set; }} = new List<{navType}>();");
            }
            
            sb.AppendLine("}");
            sb.AppendLine();
        }
        
        sw.Stop();
        var result = sb.ToString();
        _logger.LogInformation("GenerateEntities completed in {ElapsedMs}ms. Generated {CharCount} characters", 
            sw.ElapsedMilliseconds, result.Length);
        return result;
    }
    
    public async Task<string> GenerateDbContextAsync(Schema schema)
    {
        return await Task.Run(() => GenerateDbContext(schema));
    }

    public string GenerateDbContext(Schema schema)
    {
        _logger.LogInformation("GenerateDbContext started with {EntityCount} entities", schema.Entities.Count);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sb = new StringBuilder();

        var entityNameMap = BuildEntityNameMap(schema);
        var usedDbSetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine();
        sb.AppendLine("public class AppDbContext : DbContext");
        sb.AppendLine("{");
        sb.AppendLine("    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        // Add DbSets
        foreach (var entity in schema.Entities)
        {
            var entityName = entityNameMap[entity.Name];
            var ownedEntity = IsOwnedEntity(schema, entity);
            if (!ownedEntity)
            {
                var dbSetName = EnsureUniquePropertyName(Pluralize(entityName), usedDbSetNames);
                sb.AppendLine($"    public DbSet<{entityName}> {dbSetName} {{ get; set; }} = default!;");
            }
        }
        
        sb.AppendLine();
        sb.AppendLine("    protected override void OnModelCreating(ModelBuilder modelBuilder)");
        sb.AppendLine("    {");
        
        // Configure owned entities
        var ownedEntities = schema.Entities.Where(e => IsOwnedEntity(schema, e)).ToList();
        if (ownedEntities.Any())
        {
            sb.AppendLine("        // Configure owned entities");
            foreach (var owned in ownedEntities)
            {
                var ownedName = entityNameMap[owned.Name];
                var owner = FindOwnerEntity(schema, owned);
                if (owner != null)
                {
                    var ownerName = entityNameMap[owner.Name];
                    var navProp = ownedName;
                    sb.AppendLine($"        modelBuilder.Entity<{ownerName}>()");
                    sb.AppendLine($"            .OwnsOne(e => e.{navProp});");
                }
            }
            sb.AppendLine();
        }
        
        // Configure composite keys
        var joinTables = FindJoinTables(schema);
        var joinTableNames = new HashSet<string>(joinTables.Select(j => j.Name), StringComparer.OrdinalIgnoreCase);
        var compositeKeyEntities = schema.Entities
            .Where(e => !IsOwnedEntity(schema, e) && !joinTableNames.Contains(e.Name) && e.Attributes.Count(a => a.IsPrimaryKey) > 1)
            .ToList();
        if (compositeKeyEntities.Any())
        {
            sb.AppendLine("        // Configure composite keys");
            foreach (var entity in compositeKeyEntities)
            {
                var entityName = entityNameMap[entity.Name];
                var pkAttributes = entity.Attributes.Where(a => a.IsPrimaryKey).ToList();
                var keys = string.Join(", ", pkAttributes.Select(a => $"e.{ToPascalCase(a.Name)}"));
                sb.AppendLine($"        modelBuilder.Entity<{entityName}>()");
                sb.AppendLine($"            .HasKey(e => new {{ {keys} }});");
            }
            sb.AppendLine();
        }

        // Configure keyless entities (no PKs)
        var keylessEntities = schema.Entities
            .Where(e => !IsOwnedEntity(schema, e) && e.Attributes.All(a => !a.IsPrimaryKey))
            .ToList();
        if (keylessEntities.Any())
        {
            sb.AppendLine("        // Configure keyless entities");
            foreach (var entity in keylessEntities)
            {
                var entityName = entityNameMap[entity.Name];
                sb.AppendLine($"        modelBuilder.Entity<{entityName}>()");
                sb.AppendLine("            .HasNoKey();");
            }
            sb.AppendLine();
        }
        
        // Configure many-to-many relationships (join tables)
        if (joinTables.Any())
        {
            sb.AppendLine("        // Configure many-to-many join tables");
            foreach (var joinTable in joinTables)
            {
                var joinTableName = entityNameMap[joinTable.Name];
                var fkAttrs = joinTable.Attributes.Where(a => a.IsForeignKey && a.IsPrimaryKey).ToList();
                if (fkAttrs.Count == 2)
                {
                    var fk1Name = ToPascalCase(fkAttrs[0].Name.Replace("_id", "").Replace("Id", ""));
                    var fk2Name = ToPascalCase(fkAttrs[1].Name.Replace("_id", "").Replace("Id", ""));
                    var keys = string.Join(", ", fkAttrs.Select(a => $"e.{ToPascalCase(a.Name)}"));
                    sb.AppendLine($"        modelBuilder.Entity<{joinTableName}>()");
                    sb.AppendLine($"            .HasKey(e => new {{ {keys} }});");
                }
            }
        }
        
        sb.AppendLine("    }");
        sb.AppendLine("}");
        
        sw.Stop();
        var result = sb.ToString();
        _logger.LogInformation("GenerateDbContext completed in {ElapsedMs}ms. Generated {CharCount} characters", 
            sw.ElapsedMilliseconds, result.Length);
        return result;
    }
    
    private bool IsOwnedEntity(Schema schema, Entity entity)
    {
        // An entity is owned if it has a 1:1 relationship where its PK is also the FK to parent
        var relationship = schema.Relationships.FirstOrDefault(r => 
            r.ToEntity == entity.Name && 
            r.ToCardinality.Contains("||") &&
            r.FromCardinality.Contains("||"));
        
        if (relationship == null)
            return false;
        
        // Check if entity's PK is the FK to the parent
        var pkAttrs = entity.Attributes.Where(a => a.IsPrimaryKey).Select(a => a.Name).ToList();
        var fkAttrs = entity.Attributes.Where(a => a.IsForeignKey).Select(a => a.Name).ToList();
        
        return pkAttrs.SequenceEqual(fkAttrs);
    }
    
    private Entity? FindOwnerEntity(Schema schema, Entity ownedEntity)
    {
        var relationship = schema.Relationships.FirstOrDefault(r => r.ToEntity == ownedEntity.Name);
        return relationship != null ? schema.Entities.FirstOrDefault(e => e.Name == relationship.FromEntity) : null;
    }
    
    private List<Entity> FindJoinTables(Schema schema)
    {
        return schema.Entities.Where(e =>
        {
            var pkCount = e.Attributes.Count(a => a.IsPrimaryKey);
            var fkCount = e.Attributes.Count(a => a.IsForeignKey);
            // Join table has 2+ composite PKs that are all FKs
            return pkCount >= 2 && fkCount >= 2 && e.Attributes.Where(a => a.IsPrimaryKey).All(a => a.IsForeignKey);
        }).ToList();
    }
        
    private string ToPascalCase(string input)
    {
        var parts = input
            .Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim())
            .Where(word => word.Length > 0)
            .ToList();

        return string.Join("", parts.Select(word =>
            word.Length == 1
                ? char.ToUpper(word[0]).ToString()
                : char.ToUpper(word[0]) + word.Substring(1).ToLower()));
    }

    private string Pluralize(string name)
    {
        if (name.EndsWith("ings", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        if (name.EndsWith("y", StringComparison.OrdinalIgnoreCase) && name.Length > 1)
        {
            var prev = name[^2];
            if (!"aeiou".Contains(char.ToLowerInvariant(prev)))
            {
                return name.Substring(0, name.Length - 1) + "ies";
            }
        }

        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            return name + "es";
        }

        return name + "s";
    }

    private Dictionary<string, string> BuildEntityNameMap(Schema schema)
    {
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in schema.Entities)
        {
            var baseName = ToPascalCase(entity.Name);
            var uniqueName = EnsureUniqueName(baseName, used);
            map[entity.Name] = uniqueName;
        }

        return map;
    }

    private string EnsureUniqueName(string baseName, Dictionary<string, int> used)
    {
        if (!used.TryGetValue(baseName, out var count))
        {
            used[baseName] = 1;
            return baseName;
        }

        count++;
        used[baseName] = count;
        return $"{baseName}{count}";
    }

    private string EnsureUniquePropertyName(string baseName, HashSet<string> used)
    {
        if (used.Add(baseName))
        {
            return baseName;
        }

        var suffix = 2;
        var candidate = $"{baseName}{suffix}";
        while (!used.Add(candidate))
        {
            suffix++;
            candidate = $"{baseName}{suffix}";
        }

        return candidate;
    }
    
    private string MapToCSharpType(string mermaidType)
    {
        return mermaidType.ToLower() switch
        {
            "int" => "int",
            "string" => "string",
            "datetime" => "DateTime",
            "date_of_birth" => "DateTime",
            "created_at" => "DateTime",
            "tagged_at" => "DateTime",
            "timestamp" => "DateTime",
            "duration" => "int",
            _ => "string"
        };
    }
}
