using System.Text.Json;
using StrawberryShake.CodeGeneration.CSharp.Integration.StarWarsUnionList;
using StrawberryShake.CodeGeneration.CSharp.Integration.StarWarsUnionList.State;
using StrawberryShake.Serialization;

namespace StrawberryShake.CodeGeneration.CSharp.Integration.StarWarsUnknownUnionMember;

public class StarWarsUnknownUnionMemberTest
{
    [Fact]
    public void Build_Should_Skip_Element_When_Union_Member_Is_Unknown()
    {
        // arrange
        // The generated client only knows the union members Human, Droid and Starship.
        // The server returns an additional, unknown member ("Reptile") that was added
        // after the client was generated (see issue #6415).
        var builder = CreateSearchHeroBuilder();
        var response = CreateResponse(
            """
            {
                "data": {
                    "search": [
                        { "__typename": "Droid", "id": "ZHJvaWQ6MQ==", "name": "R2-D2" },
                        { "__typename": "Reptile", "id": "cmVwdGlsZTox", "name": "Slither" }
                    ]
                }
            }
            """);

        // act
        var result = builder.Build(response);

        // assert
        result.EnsureNoErrors();
        Assert.Collection(
            result.Data!.Search!,
            known => Assert.Equal("R2-D2", Assert.IsType<SearchHero_Search_Droid>(known).Name),
            unknown => Assert.Null(unknown));
    }

    private static SearchHeroBuilder CreateSearchHeroBuilder()
    {
        var entityStore = new EntityStore();
        var idSerializer = new StarWarsUnionListClientEntityIdFactory();

        var droidNodeMapper = new SearchHero_Search_Friends_Nodes_DroidFromDroidEntityMapper(entityStore);
        var humanNodeMapper = new SearchHero_Search_Friends_Nodes_HumanFromHumanEntityMapper(entityStore);
        var starshipMapper = new SearchHero_Search_StarshipFromStarshipEntityMapper(entityStore);
        var humanMapper = new SearchHero_Search_HumanFromHumanEntityMapper(
            entityStore,
            droidNodeMapper,
            humanNodeMapper);
        var droidMapper = new SearchHero_Search_DroidFromDroidEntityMapper(entityStore);

        var resultDataFactory = new SearchHeroResultFactory(
            entityStore,
            starshipMapper,
            humanMapper,
            droidMapper);

        var serializerResolver = new SerializerResolver(new ISerializer[] { new StringSerializer() });

        return new SearchHeroBuilder(entityStore, idSerializer, resultDataFactory, serializerResolver);
    }

    private static Response<JsonDocument> CreateResponse(string json)
        => new(JsonDocument.Parse(json), null);
}
