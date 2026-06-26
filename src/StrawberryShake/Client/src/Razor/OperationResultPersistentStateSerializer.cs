#if NET10_0_OR_GREATER
using System.Buffers;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace StrawberryShake.Razor;

/// <summary>
/// A <see cref="PersistentComponentStateSerializer{T}"/> for StrawberryShake operation
/// results. It persists the raw transport "data" payload that was captured on the result and
/// rehydrates it through the operation's result builder. This lets a property of type
/// <c>IOperationResult&lt;TData&gt;</c> annotated with <c>[PersistentState]</c> survive the
/// prerender to interactive boundary without re-executing the operation.
/// </summary>
/// <typeparam name="TData">
/// The operation result type.
/// </typeparam>
public sealed class OperationResultPersistentStateSerializer<TData>
    : PersistentComponentStateSerializer<IOperationResult<TData>>
    where TData : class
{
    private static readonly byte[] s_emptyData = "{}"u8.ToArray();

    private readonly IOperationResultBuilder<JsonDocument, TData> _resultBuilder;

    public OperationResultPersistentStateSerializer(
        IOperationResultBuilder<JsonDocument, TData> resultBuilder)
    {
        ArgumentNullException.ThrowIfNull(resultBuilder);

        _resultBuilder = resultBuilder;
    }

    public override void Persist(IOperationResult<TData> value, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(writer);

        if (value.ContextData.TryGetValue(WellKnownContextData.PersistedData, out var payload)
            && payload is JsonElement element)
        {
            using var jsonWriter = new Utf8JsonWriter(writer);
            element.WriteTo(jsonWriter);
        }
        else
        {
            // No captured payload (for example an error result): persist an empty data
            // object so the value still round-trips to a non-null result.
            writer.Write(s_emptyData);
        }
    }

    public override IOperationResult<TData> Restore(ReadOnlySequence<byte> data)
    {
        return _resultBuilder.BuildFromPersistedData(data.ToArray());
    }
}
#endif
