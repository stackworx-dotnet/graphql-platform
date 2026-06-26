using System.Buffers;
using System.Text;
using System.Text.Json;

namespace StrawberryShake.Razor;

public class OperationResultPersistentStateSerializerTests
{
    [Fact]
    public void RoundTrips_Result_Without_Rebuilding_From_The_Network()
    {
        // arrange
        var builder = new CapturingResultBuilder(new TestDataFactory());
        var captured = builder.Build(
            new Response<JsonDocument>(
                JsonDocument.Parse(@"{""data"": { ""name"": ""Strawberry"" } }"),
                null));
        var serializer = new OperationResultPersistentStateSerializer<TestData>(builder);
        var buffer = new ArrayBufferWriter<byte>();

        // act
        serializer.Persist(captured, buffer);
        var restored = serializer.Restore(new ReadOnlySequence<byte>(buffer.WrittenMemory));

        // assert
        Assert.NotNull(restored.Data);
        Assert.Equal("Strawberry", builder.LastData!.Value.GetProperty("name").GetString());
    }

    [Fact]
    public void Persist_Without_Captured_Payload_Falls_Back_To_Empty_Data_Object()
    {
        // arrange
        // A result built without capture carries no persisted payload (e.g. an error result).
        var plain = new PlainResultBuilder(new TestDataFactory()).Build(
            new Response<JsonDocument>(
                JsonDocument.Parse(@"{""data"": { ""name"": ""Strawberry"" } }"),
                null));
        var serializer = new OperationResultPersistentStateSerializer<TestData>(
            new CapturingResultBuilder(new TestDataFactory()));
        var buffer = new ArrayBufferWriter<byte>();

        // act
        serializer.Persist(plain, buffer);
        var restored = serializer.Restore(new ReadOnlySequence<byte>(buffer.WrittenMemory));

        // assert
        Assert.Equal("{}", Encoding.UTF8.GetString(buffer.WrittenSpan));
        Assert.NotNull(restored.Data);
    }

    private sealed class TestData;

    private sealed class TestDataInfo : IOperationResultDataInfo
    {
        public IReadOnlyCollection<EntityId> EntityIds { get; } = ArraySegment<EntityId>.Empty;

        public ulong Version { get; }

        public IOperationResultDataInfo WithVersion(ulong version)
            => throw new NotImplementedException();
    }

    private sealed class TestDataFactory : IOperationResultDataFactory<TestData>
    {
        public Type ResultType => typeof(TestData);

        public TestData Create(IOperationResultDataInfo dataInfo, IEntityStoreSnapshot? snapshot = null)
            => new();

        object IOperationResultDataFactory.Create(
            IOperationResultDataInfo dataInfo,
            IEntityStoreSnapshot? snapshot)
            => Create(dataInfo, snapshot);
    }

    private sealed class CapturingResultBuilder : OperationResultBuilder<TestData>
    {
        public CapturingResultBuilder(IOperationResultDataFactory<TestData> resultDataFactory)
        {
            ResultDataFactory = resultDataFactory;
        }

        public JsonElement? LastData { get; private set; }

        protected override bool CapturePersistedData => true;

        protected override IOperationResultDataFactory<TestData> ResultDataFactory { get; }

        protected override IOperationResultDataInfo BuildData(JsonElement obj)
        {
            LastData = obj.Clone();
            return new TestDataInfo();
        }
    }

    private sealed class PlainResultBuilder : OperationResultBuilder<TestData>
    {
        public PlainResultBuilder(IOperationResultDataFactory<TestData> resultDataFactory)
        {
            ResultDataFactory = resultDataFactory;
        }

        protected override IOperationResultDataFactory<TestData> ResultDataFactory { get; }

        protected override IOperationResultDataInfo BuildData(JsonElement obj)
            => new TestDataInfo();
    }
}
