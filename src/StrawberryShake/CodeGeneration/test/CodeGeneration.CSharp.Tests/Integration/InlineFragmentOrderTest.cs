using HotChocolate.AspNetCore.Tests.Utilities;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace StrawberryShake.CodeGeneration.CSharp.Integration.InlineFragmentOrder;

public class InlineFragmentOrderTest : ServerTestBase
{
    public InlineFragmentOrderTest(TestServerFactory serverFactory) : base(serverFactory)
    {
    }

    [Fact]
    public async Task Execute_InlineFragmentOrder_Should_Map_Bird_When_Fragment_Only_In_Second_Field()
    {
        // arrange
        // The query requests the Bird inline fragment only under "shelters" (the second
        // top-level field). "people" is processed first and omits the Bird fragment.
        // The first shelter's pet is a Bird, so its WingSpan must survive deserialization.
        var ct = new CancellationTokenSource(20_000).Token;
        var serviceCollection = new ServiceCollection();
        serviceCollection
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .AddType<IPet>()
            .AddType<Dog>()
            .AddType<Cat>()
            .AddType<Bird>();
        serviceCollection.AddInlineFragmentOrderClient().ConfigureInMemoryClient();
        IServiceProvider services = serviceCollection.BuildServiceProvider();
        var client = services.GetRequiredService<InlineFragmentOrderClient>();

        // act
        var result = await client.NotWorkingQuery.ExecuteAsync(ct);

        // assert
        result.EnsureNoErrors();
        var firstShelterPet =
            Assert.IsType<INotWorkingQuery_Shelters_Pet_Bird>(
                result.Data!.Shelters[0].Pet,
                exactMatch: false);
        Assert.Equal(42, firstShelterPet.WingSpan);
    }

    public class Query
    {
        public Person[] GetPeople() =>
        [
            new Person { Id = 1, Name = "Alice", Pet = new Dog { Id = 101, Breed = "Bulldog" } },
            new Person { Id = 2, Name = "Bob", Pet = new Cat { Id = 102, FurColor = "Black" } }
        ];

        public Shelter[] GetShelters() =>
        [
            new Shelter { Id = 1, Location = "Downtown", Pet = new Bird { Id = 201, WingSpan = 42 } },
            new Shelter { Id = 2, Location = "Uptown", Pet = new Dog { Id = 202, Breed = "Labrador" } }
        ];
    }

    [InterfaceType("IPet")]
    public interface IPet
    {
        int Id { get; }
    }

    public class Dog : IPet
    {
        public int Id { get; set; }

        public string Breed { get; set; } = null!;
    }

    public class Cat : IPet
    {
        public int Id { get; set; }

        public string FurColor { get; set; } = null!;
    }

    public class Bird : IPet
    {
        public int Id { get; set; }

        public int WingSpan { get; set; }
    }

    public class Person
    {
        public int Id { get; set; }

        public string Name { get; set; } = null!;

        public IPet? Pet { get; set; }
    }

    public class Shelter
    {
        public int Id { get; set; }

        public string Location { get; set; } = null!;

        public IPet? Pet { get; set; }
    }
}
