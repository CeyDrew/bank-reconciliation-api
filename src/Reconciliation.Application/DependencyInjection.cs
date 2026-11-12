using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reconciliation.Application.Accounts;
using Reconciliation.Application.Customers;
using Reconciliation.Application.Invoices;
using Reconciliation.Application.Matching;
using Reconciliation.Application.Transactions;

namespace Reconciliation.Application;

public static class DependencyInjection
{
    /// <summary>Registers application-layer services (use cases, validators, matching engine).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // API messages are always English, whatever the server's culture is.
        ValidatorOptions.Global.LanguageManager.Enabled = false;
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<CustomerService>();
        services.AddScoped<AccountService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<TransactionImportService>();

        services.AddSingleton<MatchingEngine>();
        services.AddScoped<ReconciliationService>();
        services.AddScoped<MatchQueries>();

        return services;
    }
}
