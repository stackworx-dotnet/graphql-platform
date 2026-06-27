using static StrawberryShake.CodeGeneration.CSharp.GeneratorTestHelper;

namespace StrawberryShake.CodeGeneration.CSharp;

// Regression guard for ChilliCream/graphql-platform#5593, where two object types
// sharing a common interface base (Exercise and Survey both implementing Activity)
// caused the generator to emit a duplicate base class. The scenario generates
// correctly on the current type-system pipeline; these tests lock that in.
public class InheritanceGeneratorTests
{
    private const string EntityInterfaceSchema =
        @"type Query {
                activities: [Activity!]!
                activitiesPaged: ActivitiesPagedConnection
            }

            interface Activity {
                id: Uuid!
                activityType: String!
                description: String
                name: String!
            }

            type Exercise implements Activity {
                id: Uuid!
                activityType: String!
                category: ExerciseCategory!
                description: String
                name: String!
            }

            type Survey implements Activity {
                id: Uuid!
                activityType: String!
                description: String
                name: String!
            }

            type ActivitiesPagedConnection {
                nodes: [Activity!]
                totalCount: Int!
            }

            enum ExerciseCategory {
                NONE
                CATEGORY1
                CATEGORY2
            }

            scalar Uuid";

    [Fact]
    public void Interface_Entity_With_Two_Implementations_InlineFragments()
    {
        AssertResult(
            @"query GetActivities {
                    activities {
                        id
                        activityType
                        name
                        ... on Exercise {
                            category
                        }
                        ... on Survey {
                            description
                        }
                    }
                }",
            EntityInterfaceSchema,
            "extend schema @key(fields: \"id\")");
    }

    [Fact]
    public void Interface_Entity_Connection_With_Two_Implementations()
    {
        AssertResult(
            @"query GetActivitiesPaged {
                    activitiesPaged {
                        nodes {
                            id
                            activityType
                            name
                            ... on Exercise {
                                category
                            }
                            ... on Survey {
                                description
                            }
                        }
                        totalCount
                    }
                }",
            EntityInterfaceSchema,
            "extend schema @key(fields: \"id\")");
    }
}
