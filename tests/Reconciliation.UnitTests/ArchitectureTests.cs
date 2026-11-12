using FluentAssertions;
using Reconciliation.Application;
using Reconciliation.Domain;

namespace Reconciliation.UnitTests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_does_not_depend_on_other_layers()
    {
        var references = typeof(DomainAssembly).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        references.Should().NotContain(["Reconciliation.Application", "Reconciliation.Infrastructure", "Reconciliation.Api"]);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_api()
    {
        var references = typeof(DependencyInjection).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        references.Should().NotContain(["Reconciliation.Infrastructure", "Reconciliation.Api"]);
    }
}
