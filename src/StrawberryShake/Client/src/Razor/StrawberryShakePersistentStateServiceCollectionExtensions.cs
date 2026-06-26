#if NET10_0_OR_GREATER
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace StrawberryShake.Razor;

/// <summary>
/// Service collection extensions for persisting StrawberryShake operation results across
/// the Blazor prerender to interactive boundary.
/// </summary>
public static class StrawberryShakePersistentStateServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="PersistentComponentStateSerializer{T}"/> for
    /// <c>IOperationResult&lt;TData&gt;</c>, so that a component or service property of that
    /// type annotated with <c>[PersistentState]</c> is persisted during a server prerender
    /// and rehydrated on the interactive client without re-executing the operation.
    /// </summary>
    /// <typeparam name="TData">
    /// The operation result type (the type argument of <c>IOperationResult&lt;&gt;</c>).
    /// </typeparam>
    /// <param name="services">
    /// The service collection. The matching StrawberryShake client must already be
    /// registered so that <c>IOperationResultBuilder&lt;JsonDocument, TData&gt;</c> can be
    /// resolved.
    /// </param>
    /// <returns>
    /// The service collection so that further calls can be chained.
    /// </returns>
    public static IServiceCollection AddPersistentOperationResult<TData>(
        this IServiceCollection services)
        where TData : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<
            PersistentComponentStateSerializer<IOperationResult<TData>>,
            OperationResultPersistentStateSerializer<TData>>();

        return services;
    }
}
#endif
