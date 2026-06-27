using HotChocolate.AspNetCore.Tests.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace StrawberryShake.CodeGeneration.CSharp.Integration.RecursiveEntitySelfReference;

public class RecursiveEntitySelfReferenceTest : ServerTestBase
{
    public RecursiveEntitySelfReferenceTest(TestServerFactory serverFactory) : base(serverFactory)
    {
    }

    [Fact]
    public async Task Execute_SelfReference_Should_Keep_BestFriend_Fields_When_SelectionSets_Differ()
    {
        // arrange
        // selfishGuy.bestFriend points back to selfishGuy (same entity id), but the two
        // selection sets differ: the outer set has id/firstName/lastName, the inner set has
        // firstName/age/phone. The outer write must not overwrite the inner write's
        // age/phone fields with defaults.
        var ct = new CancellationTokenSource(20_000).Token;
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddGraphQLServer()
            .AddQueryType<Query>();
        serviceCollection.AddRecursiveEntitySelfReferenceClient().ConfigureInMemoryClient();
        IServiceProvider services = serviceCollection.BuildServiceProvider();
        var client = services.GetRequiredService<RecursiveEntitySelfReferenceClient>();

        // act
        var result = await client.GetSelfishGuy.ExecuteAsync(ct);

        // assert
        result.EnsureNoErrors();
        var bestFriend = result.Data!.SelfishGuy.BestFriend!;
        Assert.Equal(42, bestFriend.Age);
        Assert.Equal("123-456-7890", bestFriend.Phone);
    }

    public class Query
    {
        public Person GetSelfishGuy()
        {
            var selfishGuy = new Person
            {
                Id = "1",
                FirstName = "John",
                LastName = "Doe",
                Age = 42,
                Phone = "123-456-7890",
                ZipCode = "12345"
            };

            selfishGuy.BestFriend = selfishGuy;

            return selfishGuy;
        }
    }

    public class Person
    {
        public string Id { get; set; } = null!;

        public string FirstName { get; set; } = null!;

        public string LastName { get; set; } = null!;

        public int Age { get; set; }

        public string Phone { get; set; } = null!;

        public string ZipCode { get; set; } = null!;

        public Person? BestFriend { get; set; }
    }
}
