using System.Reflection;
using D2DAudit.Api.Controllers;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure;
using NetArchTest.Rules;

namespace D2DAudit.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_depends_only_on_the_base_class_library()
    {
        var result = Types.InAssembly(typeof(Tenant).Assembly)
            .Should()
            .NotHaveDependencyOnAny(
                "D2DAudit.Application",
                "D2DAudit.Infrastructure",
                "D2DAudit.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_the_host()
    {
        var result = Types.InAssembly(typeof(Result<>).Assembly)
            .Should()
            .NotHaveDependencyOnAny(
                "D2DAudit.Infrastructure",
                "D2DAudit.Api",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api()
    {
        var result = Types.InAssembly(typeof(DependencyInjection).Assembly)
            .Should()
            .NotHaveDependencyOn("D2DAudit.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Api_types_do_not_use_entity_framework()
    {
        var result = Types.InAssembly(typeof(HealthController).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "D2DAudit.Infrastructure.Persistence")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Production_code_has_no_mutable_static_fields()
    {
        var assemblies = new[]
        {
            typeof(Tenant).Assembly,
            typeof(Result<>).Assembly,
            typeof(DependencyInjection).Assembly,
            typeof(HealthController).Assembly,
        };
        var mutable = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace?.StartsWith("D2DAudit", StringComparison.Ordinal) == true)
            .Where(type => !type.Name.Contains('<', StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(field => !field.IsLiteral && !field.IsInitOnly)
            .Where(field => !field.Name.Contains('<', StringComparison.Ordinal))
            .Select(field => field.DeclaringType?.FullName + "." + field.Name)
            .ToArray();
        Assert.Empty(mutable);
    }
}
