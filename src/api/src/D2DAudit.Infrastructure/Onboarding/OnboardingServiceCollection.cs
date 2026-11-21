using D2DAudit.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace D2DAudit.Infrastructure.Onboarding;

public static class OnboardingServiceCollection
{
    public static IServiceCollection AddOnboardingServices(this IServiceCollection services, IConfiguration configuration, IdentityMode mode)
    {
        var billing = BillingOptions.From(configuration);
        services.AddSingleton(new OnboardingIdentity(mode));
        services.AddSingleton(billing);
        services.AddSingleton<AppLinks>();
        services.AddScoped<ISignupService, SignupService>();
        services.AddScoped<SignupProvisioningRunner>();
        services.AddSingleton<SignupProvisioningQueue>();
        services.AddSingleton<ISignupProvisioningQueue>(provider => provider.GetRequiredService<SignupProvisioningQueue>());
        services.AddHostedService(provider => provider.GetRequiredService<SignupProvisioningQueue>());
        services.AddScoped<IOnboarding, OnboardingPortal>();
        services.AddScoped<IFeatureGate, FeatureGateService>();
        services.AddScoped<IBilling, BillingPortal>();
        services.AddScoped<IBillingWebhooks, BillingWebhooks>();
        services.AddScoped<ITeamAccess, TeamAccessPortal>();
        services.AddScoped<IInviteAcceptance, InviteAcceptance>();
        services.AddScoped<IRiskInsights, RiskInsightsPortal>();
        if (billing.StripeConfigured)
        {
            services.AddHttpClient<IBillingProvider, StripeBillingProvider>(client => client.Timeout = TimeSpan.FromSeconds(20));
        }
        else
        {
            services.AddSingleton<IBillingProvider, MockBillingProvider>();
        }

        return services;
    }
}
